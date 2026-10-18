using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

/// <summary>User-controlled feasibility preview; its lifetime has no automatic expiry.</summary>
internal sealed class PreviewWindow : Window
{
    private readonly string reportPath;
    private readonly WindowIdentity? testTarget;
    private readonly TextBlock statusText = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock positionText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
    private readonly TextBlock diagnosticText = new() { Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer statusTimer;
    private OverlayWindow? overlay;
    private WindowTracker? tracker;
    private bool requestedRunning;
    private string? lastSavedStatus;
    private double offset = 44;

    internal bool HasDiagnosticError { get; private set; }
    internal PreviewStatus Status { get; private set; } = PreviewStatus.Stopped;
    internal TrackerState? TrackerState => tracker?.State;
    internal bool IsOverlayRendered => overlay?.HasRendered == true;

    internal PreviewWindow(string? reportPath = null, WindowIdentity? testTarget = null)
    {
        this.reportPath = reportPath ?? Path.Combine(AppContext.BaseDirectory, "preview-state.json");
        this.testTarget = testTarget;
        Title = "Codex额度条 · 模拟预览";
        Width = 650; Height = 360; MinWidth = 630; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 250));
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = "额度横条预览", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "这是模拟数据预览。窗口保持运行，直到你点击停止或关闭此窗口。",
            Margin = new Thickness(0, 10, 0, 18),
            TextWrapping = TextWrapping.Wrap
        });
        var sample = OverlayWindow.CreateBarContent();
        sample.Width = 540; sample.Height = 28; sample.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(sample);
        panel.Children.Add(new TextBlock
        {
            Text = "上方是样式预览。独立横条显示在 Codex 窗口顶部。\n切换到其他应用时会隐藏，回到 Codex 或此窗口时恢复。",
            Margin = new Thickness(0, 10, 0, 14),
            TextWrapping = TextWrapping.Wrap
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        Button Action(string title, Action clicked)
        {
            var button = new Button { Content = title, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 10, 0) };
            button.Click += (_, _) => clicked();
            return button;
        }
        actions.Children.Add(Action("显示模拟横条", StartPreview));
        actions.Children.Add(Action("停止显示", StopPreview));
        actions.Children.Add(Action("上移", () => Shift(-8)));
        actions.Children.Add(Action("下移", () => Shift(8)));
        actions.Children.Add(positionText);
        panel.Children.Add(actions);
        statusText.Margin = new Thickness(0, 18, 0, 0);
        panel.Children.Add(statusText);
        panel.Children.Add(diagnosticText);
        Content = panel;
        statusTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => { if (requestedRunning && tracker is null) Attach(); UpdateStatus(); }, Dispatcher);
        Loaded += (_, _) => StartPreview();
        Closed += (_, _) => { statusTimer.Stop(); StopPreview(); };
        UpdateStatus();
    }

    internal void StartPreview()
    {
        requestedRunning = true;
        if (tracker is null) Attach();
        tracker?.Tick();
        UpdateStatus();
    }

    internal void StopPreview()
    {
        requestedRunning = false;
        tracker?.Dispose(); tracker = null;
        overlay?.Close(); overlay = null;
        UpdateStatus();
    }

    private void Attach()
    {
        var target = testTarget ?? NativeWindows.DiscoverCodex()
            .Where(w => w.Bounds.Width >= 180 && w.Bounds.Height >= 100)
            .OrderByDescending(w => (long)w.Bounds.Width * w.Bounds.Height).FirstOrDefault();
        if (target is null) return;
        overlay = new OverlayWindow();
        tracker = new WindowTracker(overlay, target.Executable, 540, offset,
            testTarget is null ? 0 : (nint)target.Handle, new WindowInteropHelper(this).Handle);
    }

    private void Shift(double delta)
    {
        offset = Math.Clamp(offset + delta, 0, 500);
        var restart = requestedRunning;
        StopPreview();
        if (restart) StartPreview();
        else UpdateStatus();
    }

    private void UpdateStatus()
    {
        var targetFound = tracker is not null && NativeWindows.IsWindow((nint)tracker.State.TargetHandle);
        Status = PreviewStatusPolicy.Evaluate(requestedRunning, targetFound,
            tracker?.State.NativeVisible == true, overlay?.HasRendered == true);
        statusText.Text = Status switch
        {
            PreviewStatus.Stopped => "已停止。点击“显示模拟横条”可重新开始。",
            PreviewStatus.WaitingForTarget => "未找到 Codex 窗口。请打开或还原 Codex。",
            PreviewStatus.WaitingForForeground => "正在等待 Codex。请还原窗口并切回 Codex 或此预览窗口。",
            PreviewStatus.Rendering => "正在绘制模拟横条……",
            _ => "模拟横条正在显示。不会自动退出。"
        };
        positionText.Text = $"距顶部 {offset:0}";
        var marker = $"{Status}:{offset}:{tracker?.State.TargetHandle}";
        if (marker == lastSavedStatus) return;
        lastSavedStatus = marker;
        try
        {
            Program.WriteReport(reportPath, new
            {
                capturedAtUtc = DateTimeOffset.UtcNow,
                simulated = true,
                autoExpires = false,
                running = requestedRunning,
                status = Status.ToString(),
                offset,
                rendered = overlay?.HasRendered == true,
                state = tracker?.State
            });
            HasDiagnosticError = false;
            diagnosticText.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            HasDiagnosticError = true;
            diagnosticText.Text = "诊断文件无法保存，预览仍可继续使用。";
        }
    }
}


