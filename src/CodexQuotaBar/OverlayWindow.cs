using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexQuotaBar;

internal sealed class OverlayWindow : Window
{
    internal nint Handle { get; }
    internal bool HasRendered { get; private set; }

    internal bool IsDragging { get; private set; }
    private bool positionLocked = true, pollPointer;
    private readonly DispatcherTimer dragTimer;
    private int lastPointerX, lastPointerY;
    internal Func<PointerSample> PointerReader { get; set; } = NativeWindows.ReadPrimaryPointer;
    internal bool IsPointerPolling => pollPointer && dragTimer.IsEnabled;
    internal event Action<int, int>? DragStarted, DragMoved;
    internal event Action? DragFinished;
    internal bool PositionLocked
    {
        get => positionLocked;
        set
        {
            EndPointerDrag(); positionLocked = value;
            var style = NativeWindows.GetWindowLongPtr(Handle, -20).ToInt64();
            NativeWindows.SetWindowLongPtr(Handle, -20, (nint)(value ? style | 0x20 : style & ~0x20L));
            NativeWindows.SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0037);
            Cursor = value ? Cursors.Arrow : Cursors.SizeAll;
        }
    }
    internal bool BeginPointerDrag(int x, int y, bool capturePointer = true)
    {
        if (PositionLocked || IsDragging) return false;
        IsDragging = true; pollPointer = capturePointer; lastPointerX = x; lastPointerY = y;
        DragStarted?.Invoke(x, y);
        if (IsDragging && pollPointer) { _ = CaptureMouse(); if (IsDragging) dragTimer.Start(); }
        return IsDragging;
    }
    internal void MovePointerDrag(int x, int y) { if (IsDragging) DragMoved?.Invoke(x, y); }
    private void PollPointer()
    {
        if (!IsDragging) { dragTimer.Stop(); return; }
        var sample = PointerReader();
        if (sample.Valid && (sample.X != lastPointerX || sample.Y != lastPointerY))
        {
            lastPointerX = sample.X; lastPointerY = sample.Y; MovePointerDrag(sample.X, sample.Y);
        }
        if (!sample.Pressed || !sample.Valid) EndPointerDrag();
    }
    internal void EndPointerDrag()
    {
        dragTimer.Stop(); pollPointer = false;
        if (!IsDragging) return;
        IsDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        DragFinished?.Invoke();
    }

    internal OverlayWindow(FrameworkElement? liveContent = null, bool positionLocked = true)
    {
        dragTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        dragTimer.Tick += (_, _) => PollPointer();
        Title = liveContent is null ? "Codex额度条 · 模拟数据" : "Codex额度条 · 真实额度";
        Width = 540; Height = 28;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Content = liveContent ?? CreateBarContent();
        ContentRendered += (_, _) => HasRendered = true;
        Handle = new WindowInteropHelper(this).EnsureHandle();
        var style = NativeWindows.GetWindowLongPtr(Handle, -20).ToInt64();
        NativeWindows.SetWindowLongPtr(Handle, -20, (nint)(style | 0x08000000 | 0x00000080 | 0x00000020));
        HwndSource.FromHwnd(Handle)!.AddHook(HandleMessage);
        PositionLocked = positionLocked;
        PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (NativeWindows.GetCursorPos(out var point) && BeginPointerDrag(point.X, point.Y)) args.Handled = true;
        };
        MouseMove += (_, args) =>
        {
            if (!IsDragging || pollPointer) return;
            if (args.LeftButton != MouseButtonState.Pressed) { EndPointerDrag(); return; }
            if (NativeWindows.GetCursorPos(out var point)) MovePointerDrag(point.X, point.Y);
            args.Handled = true;
        };
        PreviewMouseLeftButtonUp += (_, args) => { if (IsDragging) { if (pollPointer) PollPointer(); else EndPointerDrag(); args.Handled = true; } };
        LostMouseCapture += (_, _) => { if (!pollPointer || !PointerReader().Pressed) EndPointerDrag(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) EndPointerDrag(); };
        Closed += (_, _) => EndPointerDrag();
    }

    internal static FrameworkElement CreateBarContent()
    {
        var grid = new Grid { Margin = new Thickness(9, 0, 5, 0) };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = "模拟数据",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 207, 125)),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Margin = new Thickness(0, 0, 10, 0)
        };
        grid.Children.Add(label);
        var meter = new Grid { Height = 18, VerticalAlignment = VerticalAlignment.Center };
        meter.ColumnDefinitions.Add(new() { Width = new GridLength(68, GridUnitType.Star) });
        meter.ColumnDefinitions.Add(new() { Width = new GridLength(32, GridUnitType.Star) });
        meter.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(38, 108, 89)), CornerRadius = new CornerRadius(4) });
        var percentage = new TextBlock
        {
            Text = "Codex · 演示周期   剩余 68%",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontSize = 11
        };
        Grid.SetColumnSpan(percentage, 2); meter.Children.Add(percentage);
        Grid.SetColumn(meter, 1); grid.Children.Add(meter);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(27, 31, 39)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(67, 74, 87)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private nint HandleMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; }
        return 0;
    }
}












