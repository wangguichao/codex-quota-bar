using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

internal static class LiveSmokeTest
{
    private sealed class Provider : IUsageProvider
    {
        internal bool Mismatch, WithSecondary, Slow;
        internal double UsedPercent = 7, SecondaryUsedPercent = 50;
        internal TaskCompletionSource? BlockNext;
        internal DateTimeOffset Reset = DateTimeOffset.UtcNow.AddDays(1);
        public async Task<UsageSnapshot> ReadAsync(CancellationToken token)
        {
            if (BlockNext is { } block) { BlockNext = null; await block.Task.WaitAsync(token); throw new UsageFailureException(UsageState.Timeout); }
            await Task.Delay(Slow ? Timeout.Infinite : 30, token);
            var windows = new List<QuotaWindow> { new("primary", UsedPercent, 10080, Reset) };
            if (WithSecondary) windows.Add(new("secondary", SecondaryUsedPercent, 300, Reset));
            return new(DateTimeOffset.UtcNow, "EMAIL", Mismatch ? "OTHER" : "QUOTA", true, [new("codex", "Codex", windows.AsReadOnly())]);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static ComboBox Selector(LiveWindow window, string id)
        => Descendants<ComboBox>(window).Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static async Task<bool> CloseAndWaitAsync(LiveWindow window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(); window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        return window.SourceStopped;
    }
    private static async Task WaitCurrentAsync(LiveWindow window)
    {
        for (var i = 0; i < 40 && window.Usage.State != UsageState.Current; i++) await Task.Delay(100);
    }
    private static bool RenderThemes(string path)
    {
        var panel = new StackPanel { Width = 540, Background = Brushes.White };
        var fits = true;
        foreach (var theme in new[] { BarTheme.Dark, BarTheme.Light })
            foreach (var width in new[] { 180, 540 })
            {
                var bar = new QuotaBarView { Width = width, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
                bar.Update(new(new string('W', 64), "剩余 93%", 93, "明天重置", "测试数据", QuotaTone.Green), true, theme);
                panel.Children.Add(bar);
            }
        panel.Measure(new Size(540, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, 540, panel.DesiredSize.Height)); panel.UpdateLayout();
        foreach (var bar in panel.Children.OfType<QuotaBarView>())
        {
            var text = bar.PercentageElement;
            var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1.25);
            fits &= text.ActualWidth + 0.5 >= formatted.WidthIncludingTrailingWhitespace;
        }
        var bitmap = new RenderTargetBitmap(675, (int)Math.Ceiling(panel.ActualHeight * 1.25), 120, 120, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream); return fits;
    }
    internal static async Task RunAsync(Application app, string reportPath)
    {
        var source = new Provider(); var results = new List<object>(); var success = true;
        var fixture = new Window { Title = "QuotaBar 真实显示验证目标（测试数据）", Width = 1000, Height = 650 };
        var settingsPath = Path.ChangeExtension(reportPath, ".settings.json");
        File.Delete(settingsPath);
        LiveWindow? window = null;
        void Check(string name, bool passed) { success &= passed; results.Add(new { name, passed }); }
        try
        {
            fixture.Show();
            var target = NativeWindows.Read(new WindowInteropHelper(fixture).Handle) ?? throw new InvalidOperationException();
            LiveWindow Create(LiveConfiguration? config = null) => new(config ?? new("", new("EMAIL", "QUOTA", "personal")),
                Path.ChangeExtension(reportPath, ".state.json"), source, target, settingsPath);
            window = Create(); window.Show(); window.Activate(); await WaitCurrentAsync(window);
            var bothBars = Descendants<QuotaBarView>(window).All(b => b.PercentageElement.Text == "剩余 93%")
                && app.Windows.OfType<OverlayWindow>().All(o => (o.Content as QuotaBarView)?.PercentageElement.Text == "剩余 93%");
            Check("verified_read_updates_both_bars", window.Usage.State == UsageState.Current && window.Presentation.RemainingPercent == 93 && bothBars);
            source.WithSecondary = true; await window.RefreshAsync();
            var period = Selector(window, "quota-period"); period.SelectedIndex = 2;
            var selected = window.Presentation.RemainingPercent == 50;
            source.WithSecondary = false; await window.RefreshAsync();
            Check("missing_fixed_period_returns_to_auto", selected && period.SelectedIndex == 0 && window.Presentation.RemainingPercent == 93);
            source.Mismatch = true; await window.RefreshAsync();
            Check("account_mismatch_hides_percentage", window.Usage.State == UsageState.IdentityUnverified && window.Presentation.RemainingPercent is null);
            source.Mismatch = false; await window.RefreshAsync();
            Check("verified_read_recovers", window.Usage.State == UsageState.Current && window.Presentation.RemainingPercent == 93);
            Selector(window, "bar-theme").SelectedIndex = 2;
            Selector(window, "refresh-interval").SelectedIndex = 2;
            Selector(window, "bar-width").SelectedIndex = 1;
            Descendants<Button>(window).Single(b => b.Content as string == "下移").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("preference_controls_update_model", window.Preferences is { Width: 360, OffsetY: 52, RefreshSeconds: 120, Theme: BarTheme.Light });
            var beforeResume = window.Usage.SuccessfulReads;
            window.ResumeFromSleep();
            for (var i = 0; i < 40 && window.Usage.SuccessfulReads == beforeResume; i++) await Task.Delay(50);
            Check("resume_handler_requests_fresh_data", window.Usage.SuccessfulReads > beforeResume);
            var block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.BlockNext = block; var beforeBlocked = window.Usage.SuccessfulReads;
            var blockedRead = window.RefreshAsync(); window.ResumeFromSleep(); await Task.Delay(50);
            block.SetResult(); await blockedRead;
            for (var i = 0; i < 60 && window.Usage.SuccessfulReads == beforeBlocked; i++) await Task.Delay(50);
            Check("resume_during_inflight_timeout_still_reads_again", window.Usage.SuccessfulReads > beforeBlocked);
            Check("idle_close_stops_monitor_and_closes_window", await CloseAndWaitAsync(window));
            window = Create();
            Check("preferences_survive_window_restart", window.Preferences is { Width: 360, OffsetY: 52, RefreshSeconds: 120, Theme: BarTheme.Light });
            window.Show(); window.Activate(); await WaitCurrentAsync(window);
            source.UsedPercent = 85; source.WithSecondary = true; source.SecondaryUsedPercent = 90; var foreground = NativeWindows.GetForegroundWindow(); await window.RefreshAsync();
            Check("low_quota_toast_does_not_steal_focus", window.NotificationCount == 2 && NativeWindows.GetForegroundWindow() == foreground);
            var activeToasts = app.Windows.OfType<QuotaAlertWindow>().ToArray();
            Check("multiple_period_alerts_share_one_visible_toast", activeToasts.Length == 1 &&
                Descendants<TextBlock>(activeToasts[0]).Any(t => t.Text.Contains("周额度") && t.Text.Contains("5小时")));
            await window.RefreshAsync(); Check("same_threshold_does_not_repeat", window.NotificationCount == 2);
            await CloseAndWaitAsync(window); window = Create(); window.Show(); await WaitCurrentAsync(window);
            Check("alert_dedup_survives_restart", window.NotificationCount == 0);
            source.Reset = source.Reset.AddDays(7); await window.RefreshAsync();
            Check("new_period_can_notify_again", window.NotificationCount == 2);
            await CloseAndWaitAsync(window);
            window = new(null, Path.ChangeExtension(reportPath, ".unconfigured.json"), source, target, settingsPath); window.Show();
            Check("unconfigured_window_closes_without_reentrancy", await CloseAndWaitAsync(window));
            source.Slow = true; window = Create(); window.Show();
            for (var i = 0; i < 20 && !window.Usage.Refreshing; i++) await Task.Delay(50);
            var reading = window.Usage.Refreshing;
            Check("close_cancels_inflight_read", await CloseAndWaitAsync(window) && reading); window = null;
            Check("dark_light_and_narrow_percentage_fits", RenderThemes(Path.ChangeExtension(reportPath, ".png")));
            Program.WriteReport(reportPath, new { success, simulated = true, results });
        }
        catch (Exception error) { success = false; Program.WriteReport(reportPath, new { success, errorType = error.GetType().Name, results }); }
        finally
        {
            if (window?.IsVisible == true) await CloseAndWaitAsync(window);
            fixture.Close(); app.Shutdown(success ? 0 : 1);
        }
    }
}








