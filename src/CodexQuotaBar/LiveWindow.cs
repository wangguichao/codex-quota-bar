using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaBar.Core;
using Microsoft.Win32;

namespace CodexQuotaBar;

internal sealed class LiveWindow : Window
{
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
    private readonly UsageMonitor? monitor;
    private readonly WindowIdentity? testTarget;
    private readonly string reportPath;
    private readonly bool testData;
    private readonly DispatcherTimer timer;
    private readonly PreferenceStore preferenceStore;
    private readonly LowQuotaAlerts alerts;
    private readonly List<QuotaAlertWindow> toasts = [];
    private readonly QuotaBarView inlineBar = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private QuotaBarView? floatingBar;
    private readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock resetText = new() { Margin = new Thickness(0, 9, 0, 0) };
    private readonly TextBlock diagnostic = new() { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock settingsNotice = new() { Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox groups = new() { MinWidth = 150, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox periods = new() { MinWidth = 170 };
    private readonly Button refresh = new() { Content = "立即刷新", Padding = new Thickness(14, 6, 14, 6) };
    private OverlayWindow? overlay;
    private WindowTracker? tracker;
    private TrayController? tray;
    private bool inTray;
    private WindowState restoreWindowState = WindowState.Normal;
    private readonly CheckBox positionLock = new() { Content = "锁定位置（鼠标穿透）", Margin = new Thickness(0, 12, 0, 0) };
    private DisplayPreferences preferences;
    private BarTheme systemTheme;
    private bool display = true, closing, allowClose, updatingChoices, resumePending;
    private string? groupId, slot, lastReport;
    private int choicesAtRead = -1, lastAlertAtRead = -1;
    internal QuotaPresentation Presentation { get; private set; } = new("Codex", "正在连接", null, "", "正在连接", QuotaTone.Neutral);
    internal UsageView Usage => monitor?.View ?? new(UsageState.IdentityUnverified, null, null, DateTimeOffset.MaxValue, false, 0, 0, false);
    internal bool SourceStopped { get; private set; }
    internal DisplayPreferences Preferences => preferences;
    internal int NotificationCount { get; private set; }
    internal event Action? ReconfigureRequested;
    internal bool IsInTray => inTray;
    internal TrayController? Tray => tray;
    internal OverlayWindow? FloatingOverlay => overlay;
    internal TrackerState? Tracking => tracker?.State;

    internal LiveWindow(LiveConfiguration? config, string reportPath, IUsageProvider? providerOverride = null,
        WindowIdentity? testTarget = null, string? settingsPath = null)
    {
        this.reportPath = reportPath; this.testTarget = testTarget; testData = providerOverride is not null;
        preferenceStore = new(settingsPath ?? Path.ChangeExtension(reportPath, ".settings.json"));
        var loaded = preferenceStore.Load(new(RefreshSeconds: config?.RefreshSeconds ?? 60));
        preferences = loaded.Settings.Preferences; alerts = new(loaded.Settings.AlertKeys);
        groupId = preferences.GroupId; slot = preferences.Slot;
        if (loaded.RecoveredFromError) settingsNotice.Text = "设置文件无法读取，已使用默认显示设置。";
        if (config is not null) monitor = new(providerOverride ?? new CodexUsageProvider(config.CodexExecutable), config.Identity, refreshSeconds: preferences.RefreshSeconds);
        systemTheme = ReadSystemTheme();
        Title = testData ? "Codex额度条 · 自动测试" : "Codex额度条 · 真实额度";
        Width = 700; Height = 600; MinWidth = 660; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 250));
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = testData ? "测试额度" : "Codex 真实额度", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = config is null ? "账号尚未核对，请参照使用说明完成本地配置。" : "显示已核对个人账号的额度，按所选间隔自动更新。", Margin = new Thickness(0, 10, 0, 18), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(inlineBar); panel.Children.Add(resetText); panel.Children.Add(info);
        var selectors = new WrapPanel { Margin = new Thickness(0, 16, 0, 12) };
        AutomationProperties.SetAutomationId(groups, "quota-group"); AutomationProperties.SetName(groups, "额度池");
        AutomationProperties.SetAutomationId(periods, "quota-period"); AutomationProperties.SetName(periods, "额度周期");
        selectors.Children.Add(groups); selectors.Children.Add(periods); panel.Children.Add(selectors);
        var actions = new WrapPanel();
        actions.Children.Add(refresh);
        Button Action(string label, Action action)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(10, 0, 0, 0) };
            button.Click += (_, _) => action(); return button;
        }
        actions.Children.Add(Action("显示横条", () => { display = true; Attach(); Render(); }));
        actions.Children.Add(Action("隐藏横条", StopOverlay));
        actions.Children.Add(Action("上移", () => Shift(-8))); actions.Children.Add(Action("下移", () => Shift(8)));
        actions.Children.Add(Action("重置位置", ResetPosition));
        actions.Children.Add(Action("最小化到托盘", MinimizeToTray));
        actions.Children.Add(Action("重新核对账号", () => ReconfigureRequested?.Invoke()));
        panel.Children.Add(actions);
        var appearance = new WrapPanel { Margin = new Thickness(0, 20, 0, 10) };
        ComboBox PreferenceChoice(string id, string label, IReadOnlyList<Choice> choices, string selected, Action<string> apply)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });
            var combo = new ComboBox { MinWidth = 130, ItemsSource = choices };
            AutomationProperties.SetAutomationId(combo, id); AutomationProperties.SetName(combo, label);
            combo.SelectedItem = choices.FirstOrDefault(c => c.Id == selected);
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice value) apply(value.Id); };
            stack.Children.Add(combo); appearance.Children.Add(stack); return combo;
        }
        PreferenceChoice("bar-theme", "横条主题", [new("System", "跟随系统"), new("Dark", "深色"), new("Light", "浅色")], preferences.Theme.ToString(),
            value => ChangePreferences(preferences with { Theme = Enum.Parse<BarTheme>(value) }));
        PreferenceChoice("refresh-interval", "自动更新", [new("30", "每30秒"), new("60", "每60秒"), new("120", "每2分钟"), new("300", "每5分钟")], preferences.RefreshSeconds.ToString(),
            value => ChangePreferences(preferences with { RefreshSeconds = int.Parse(value) }));
        var widths = new[] { 180, 360, 540, 720, preferences.Width }.Distinct().Order().Select(w => new Choice(w.ToString(), w + "")).ToArray();
        PreferenceChoice("bar-width", "横条宽度", widths, preferences.Width.ToString(), value => ChangePreferences(preferences with { Width = int.Parse(value) }));
        panel.Children.Add(appearance);
        var reminders = new CheckBox { Content = "低额度提醒（20% / 10% / 5%，同周期不重复）", IsChecked = preferences.AlertsEnabled };
        reminders.Checked += (_, _) => ChangePreferences(preferences with { AlertsEnabled = true });
        reminders.Unchecked += (_, _) => ChangePreferences(preferences with { AlertsEnabled = false });
        panel.Children.Add(reminders);
        positionLock.IsChecked = preferences.PositionLocked;
        AutomationProperties.SetAutomationId(positionLock, "position-lock");
        positionLock.Checked += (_, _) => ChangePreferences(preferences with { PositionLocked = true });
        positionLock.Unchecked += (_, _) => ChangePreferences(preferences with { PositionLocked = false });
        panel.Children.Add(positionLock);
        panel.Children.Add(new TextBlock { Text = "按住横条可自由拖动；锁定后鼠标穿透。最小化后双击托盘图标恢复。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "设置自动保存。切换桌面账号或工作区后，请重新核对账号。", Margin = new Thickness(0, 16, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        panel.Children.Add(settingsNotice); panel.Children.Add(diagnostic);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        refresh.Click += async (_, _) => await RefreshAsync();
        groups.SelectionChanged += (_, _) => { if (updatingChoices) return; groupId = (groups.SelectedItem as Choice)?.Id; slot = null; UpdatePeriods(); SaveSelection(); Render(); };
        periods.SelectionChanged += (_, _) => { if (updatingChoices) return; slot = (periods.SelectedItem as Choice)?.Id; if (slot == "auto") slot = null; SaveSelection(); Render(); };
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) =>
        {
            if (closing) return;
            if (display && tracker is null) Attach();
            if (resumePending && !Usage.Refreshing) await RefreshPendingResumeAsync();
            else if (monitor is not null && !Usage.Refreshing && Usage.NextRefreshUtc <= DateTimeOffset.UtcNow) await RefreshAsync();
            Render();
        }, Dispatcher);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        Loaded += async (_, _) => { EnsureTray(); Attach(); await RefreshAsync(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) MinimizeToTray(); else if (!inTray) restoreWindowState = WindowState; };
        Closing += CloseAsync;
        Closed += (_, _) => { timer.Stop(); StopOverlay(); tray?.Dispose(); };
        Render();
    }
    private void EnsureTray()
    {
        tray ??= new TrayController(RestoreFromTray, ToggleBar, () => ChangePreferences(preferences with { PositionLocked = !preferences.PositionLocked }), ResetPosition, Close);
        tray.Update(display, preferences.PositionLocked, Presentation.PercentText);
    }
    internal void MinimizeToTray()
    {
        if (closing) return;
        EnsureTray(); inTray = true; ShowInTaskbar = false; Hide(); tracker?.Tick(); Render();
    }
    internal void RestoreFromTray()
    {
        if (closing) return;
        inTray = false; WindowState = restoreWindowState; ShowInTaskbar = true; Show(); Activate();
        tracker?.UpdateInteractionWindow(new WindowInteropHelper(this).Handle); tracker?.Tick(); Render();
    }
    private void ToggleBar()
    {
        if (closing) return;
        if (display) StopOverlay(); else { display = true; Attach(); }
        Render();
    }
    private void ResetPosition() => ChangePreferences(preferences with { OffsetX = 0, OffsetY = 44 });

    internal async Task RefreshAsync()
    {
        if (closing || monitor is null) { Render(); return; }
        var operation = monitor.RefreshAsync(); Render(); await operation;
        if (!closing) Render();
    }
    internal void ResumeFromSleep()
    {
        if (closing) return;
        resumePending = true;
        _ = Dispatcher.BeginInvoke(new Action(async () => await RefreshPendingResumeAsync()));
    }
    private async Task RefreshPendingResumeAsync()
    {
        if (closing || !resumePending || monitor is null || Usage.Refreshing) return;
        resumePending = false;
        await RefreshAsync();
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0218 && wParam.ToInt64() is 7 or 18) ResumeFromSleep();
        if (message == 0x001A) { systemTheme = ReadSystemTheme(); Render(); }
        return 0;
    }
    private static BarTheme ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0 ? BarTheme.Dark : BarTheme.Light;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return BarTheme.Dark; }
    }
    private void ChangePreferences(DisplayPreferences value)
    {
        if (closing) return;
        overlay?.EndPointerDrag();
        var normalized = value.Normalize();
        if (normalized == preferences) return;
        var placementChanged = normalized.Width != preferences.Width || normalized.OffsetY != preferences.OffsetY || normalized.OffsetX != preferences.OffsetX;
        if (normalized.RefreshSeconds != preferences.RefreshSeconds) monitor?.SetRefreshSeconds(normalized.RefreshSeconds);
        if (normalized.AlertsEnabled != preferences.AlertsEnabled) lastAlertAtRead = -1;
        preferences = normalized; SavePreferences();
        if (overlay is not null) overlay.PositionLocked = preferences.PositionLocked;
        positionLock.IsChecked = preferences.PositionLocked;
        if (placementChanged) tracker?.UpdatePlacement(preferences.Width, preferences.OffsetY, preferences.OffsetX);
        Render();
    }
    private void SaveSelection()
    {
        var updated = preferences with { GroupId = groupId, Slot = slot };
        if (updated == preferences) return;
        preferences = updated; SavePreferences();
    }
    private void SavePreferences()
        => settingsNotice.Text = preferenceStore.TrySave(new(preferences, alerts.Keys)) ? "" : "设置无法保存，本次使用仍可继续。";
    private void UpdatePeriods()
    {
        updatingChoices = true;
        var group = Usage.Snapshot?.Groups.FirstOrDefault(g => g.Id == groupId);
        var choices = new List<Choice> { new("auto", "同组优先提醒") };
        if (group is not null) choices.AddRange(group.Windows.Select(w => new Choice(w.Slot, w.PeriodLabel)));
        var selected = choices.FirstOrDefault(c => c.Id == (slot ?? "auto")) ?? choices[0];
        slot = selected.Id == "auto" ? null : selected.Id;
        periods.ItemsSource = choices; periods.SelectedItem = selected; updatingChoices = false;
    }
    private void UpdateChoices(UsageView view)
    {
        if (view.SuccessfulReads == choicesAtRead || view.Snapshot is null) return;
        choicesAtRead = view.SuccessfulReads; updatingChoices = true;
        var choices = view.Snapshot.Groups.Select(g => new Choice(g.Id, g.Id == "codex" ? "Codex" : g.Name)).ToArray();
        groupId = choices.FirstOrDefault(c => c.Id == groupId)?.Id ?? choices.FirstOrDefault(c => c.Id == "codex")?.Id ?? choices.FirstOrDefault()?.Id;
        groups.ItemsSource = choices; groups.SelectedItem = choices.FirstOrDefault(c => c.Id == groupId);
        updatingChoices = false; UpdatePeriods(); SaveSelection();
    }
    private void Attach()
    {
        if (!display || tracker is not null) return;
        var target = testTarget ?? NativeWindows.DiscoverCodex().OrderByDescending(w => (long)w.Bounds.Width * w.Bounds.Height).FirstOrDefault();
        if (target is null) return;
        floatingBar = new QuotaBarView(); UpdateBar(floatingBar);
        overlay = new OverlayWindow(floatingBar, preferences.PositionLocked);
        tracker = new(overlay, target.Executable, preferences.Width, preferences.OffsetY, testTarget is null ? 0 : (nint)target.Handle, new WindowInteropHelper(this).Handle, preferences.OffsetX);
        tracker.PositionUpdated += (position, committed) =>
        {
            preferences = preferences with { OffsetX = position.OffsetX, OffsetY = position.OffsetY };
            if (committed) { SavePreferences(); Render(); }
        };
    }
    private void StopOverlay()
    {
        display = false; tracker?.Dispose(); tracker = null; overlay?.Close(); overlay = null; floatingBar = null;
    }
    private void Shift(int delta) => ChangePreferences(preferences with { OffsetY = Math.Clamp(preferences.OffsetY + delta, 0, 100000) });
    private void UpdateBar(QuotaBarView bar)
        => bar.Update(Presentation, testData, preferences.Theme == BarTheme.System ? systemTheme : preferences.Theme, SystemParameters.HighContrast);
    private void Render()
    {
        if (closing) return;
        var view = Usage; UpdateChoices(view);
        Presentation = QuotaPresentation.Create(view, groupId, slot, DateTimeOffset.UtcNow);
        inlineBar.MaxWidth = preferences.Width; UpdateBar(inlineBar);
        if (floatingBar is not null) UpdateBar(floatingBar);
        resetText.Text = Presentation.ResetText;
        var fetched = view.LastSuccessUtc is { } date ? $"上次成功更新：{date.ToLocalTime():HH:mm:ss}" : "尚未成功读取";
        info.Text = $"{Presentation.Status}{(view.Refreshing ? " · 正在更新" : "")}\n{fetched}";
        refresh.IsEnabled = monitor is not null && !view.Refreshing;
        if (preferences.AlertsEnabled && view.State == UsageState.Current && view.SuccessfulReads != lastAlertAtRead)
        {
            lastAlertAtRead = view.SuccessfulReads;
            var notifications = alerts.Evaluate(view, true);
            if (notifications.Count > 0) SavePreferences();
            if (notifications.Count > 0)
            {
                NotificationCount += notifications.Count;
                var toast = new QuotaAlertWindow(notifications, testData);
                toasts.Add(toast); toast.Closed += (_, _) => toasts.Remove(toast); toast.Show();
            }
        }
        tray?.Update(display, preferences.PositionLocked, Presentation.PercentText);
        if (overlay?.IsDragging == true) return;
        var marker = $"{view.State}:{view.Attempts}:{view.SuccessfulReads}:{preferences}:{display}:{systemTheme}:{inTray}:{tray?.Visible}";
        if (marker == lastReport) return; lastReport = marker; WriteState(view);
    }
    private void WriteState(UsageView view)
    {
        try
        {
            var presentation = QuotaPresentation.Create(view, groupId, slot, DateTimeOffset.UtcNow);
            Program.WriteReport(reportPath, new
            {
                capturedAtUtc = DateTimeOffset.UtcNow,
                simulated = testData,
                state = view.State.ToString(),
                view.Attempts,
                view.SuccessfulReads,
                view.LastSuccessUtc,
                view.NextRefreshUtc,
                view.LastSourceExited,
                groupId,
                slot,
                remainingPercent = presentation.RemainingPercent,
                percentText = presentation.PercentText,
                period = presentation.Title,
                sourceAccountFingerprint = view.Snapshot?.UsageAccountFingerprint,
                sourceSnapshotUtc = view.Snapshot?.AtUtc,
                sourceGroups = view.Snapshot?.Groups.Select(g => new { g.Id, windows = g.Windows }),
                display,
                offset = preferences.OffsetY,
                offsetX = preferences.OffsetX,
                minimizedToTray = inTray,
                controlVisible = IsVisible,
                trayIconVisible = tray?.Visible ?? false,
                preferences,
                NotificationCount,
                overlay = tracker?.State,
                sourceStopped = SourceStopped
            });
            diagnostic.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { diagnostic.Text = "诊断文件无法保存，显示仍可继续使用。"; }
    }
    private async void CloseAsync(object? sender, CancelEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true; if (closing) return;
        closing = true; timer.Stop(); StopOverlay(); tray?.Dispose(); IsEnabled = false;
        foreach (var toast in toasts.ToArray()) toast.Close();
        try { if (monitor is not null) await monitor.DisposeAsync(); SourceStopped = true; }
        finally { WriteState(Usage); allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
}






















