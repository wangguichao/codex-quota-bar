using System.Windows;
using System.Windows.Threading;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

internal sealed record TrackerState(long TargetHandle, long ForegroundHandle, bool Visible,
    bool FocusPreserved, OverlayPlacement? Placement, string? ErrorType,
    bool NativeVisible = false, bool LayeredClickThrough = false, long HitTestRoot = 0);

internal sealed class WindowTracker : IDisposable
{
    private readonly OverlayWindow overlay;
    private readonly string executable;
    private readonly nint pinnedWindow;
    private nint interactionWindow;
    private double width, offset, offsetX;
    private OverlayDragSession? drag;
    internal event Action<OverlayPosition, bool>? PositionUpdated;
    private readonly DispatcherTimer timer;
    private readonly NativeWindows.WinEventProc callback;
    private readonly List<nint> hooks = [];
    private nint target;
    private bool disposed;
    private bool queued;
    internal bool HooksAvailable => hooks.Count == 2;
    internal TrackerState State { get; private set; } = new(0, 0, false, true, null, null);

    internal WindowTracker(OverlayWindow overlay, string executable, double width, double offset, nint pinnedWindow = 0, nint interactionWindow = 0, double offsetX = 0)
    {
        this.overlay = overlay; this.executable = executable; this.width = width; this.offset = offset;
        this.pinnedWindow = pinnedWindow;
        this.interactionWindow = interactionWindow; this.offsetX = offsetX;
        overlay.DragStarted += BeginDrag; overlay.DragMoved += MoveDrag; overlay.DragFinished += FinishDrag;
        target = pinnedWindow != 0 ? pinnedWindow : (nint)(NativeWindows.DiscoverCodex().Where(w => string.Equals(w.Executable, executable, StringComparison.OrdinalIgnoreCase) && w.Bounds.Width >= 180 && w.Bounds.Height >= 100).OrderByDescending(w => (long)w.Bounds.Width * w.Bounds.Height).FirstOrDefault()?.Handle ?? 0);
        callback = (_, eventType, window, objectId, _, _, _) =>
        {
            if (disposed || window == overlay.Handle || (eventType != 3 && (window != target || objectId != 0)) || queued)
                return;
            queued = true;
            overlay.Dispatcher.BeginInvoke(() => { queued = false; if (!disposed) Tick(); });
        };
        foreach (var eventType in new uint[] { 3, 0x800B })
        {
            var hook = NativeWindows.SetWinEventHook(eventType, eventType, 0, callback, 0, 0, 0);
            if (hook != 0) hooks.Add(hook);
        }
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Tick(), overlay.Dispatcher);
        overlay.Closed += (_, _) => Dispose();
        Tick();
    }

    internal void UpdateInteractionWindow(nint window) => interactionWindow = window;
    internal void UpdatePlacement(double preferredWidth, double offsetY, double horizontalOffset)
    {
        overlay.EndPointerDrag(); width = preferredWidth; offset = offsetY; offsetX = horizontalOffset; Tick();
    }
    private void BeginDrag(int x, int y)
    {
        if (disposed) return;
        var identity = NativeWindows.Read(target);
        if (identity is null || OverlayPolicy.Place(identity.Bounds, identity.Dpi, width, offset, offsetX) is null) { overlay.EndPointerDrag(); return; }
        drag = new(identity.Bounds, identity.Dpi, width, (int)offsetX, (int)offset, x, y);
    }
    private void MoveDrag(int x, int y)
    {
        if (disposed || drag is null) return;
        var position = drag.MoveTo(x, y);
        offsetX = position.OffsetX; offset = position.OffsetY; Tick();
        PositionUpdated?.Invoke(position, false);
    }
    private void FinishDrag()
    {
        if (drag is null) return;
        drag = null; PositionUpdated?.Invoke(new((int)offsetX, (int)offset), true);
    }

    internal void Tick()
    {
        if (disposed) return;
        var foreground = NativeWindows.GetForegroundWindow();
        try
        {
            var active = foreground == overlay.Handle ? null : NativeWindows.Read(foreground);
            if (active is not null && (pinnedWindow != 0 ? foreground == pinnedWindow : NativeWindows.IsCodexSurface(active)) &&
                string.Equals(active.Executable, executable, StringComparison.OrdinalIgnoreCase))
                target = foreground;
            var identity = NativeWindows.Read(target);
            var visible = identity is not null && string.Equals(identity.Executable, executable, StringComparison.OrdinalIgnoreCase) &&
                OverlayPolicy.ShouldShow(identity.Visible && !identity.Cloaked, identity.Minimized, foreground == target, interactionWindow != 0 && foreground == interactionWindow);
            var placement = visible ? OverlayPolicy.Place(identity!.Bounds, identity.Dpi, width, offset, offsetX) : null;
            if (placement is { } position)
            {
                if (!overlay.IsVisible) overlay.Show();
                NativeWindows.Position(overlay.Handle, position);
            }
            else if (overlay.IsVisible) overlay.Hide();
            State = new((long)target, (long)foreground, overlay.IsVisible,
                NativeWindows.GetForegroundWindow() == foreground, placement, null,
                NativeWindows.IsWindowVisible(overlay.Handle),
                (NativeWindows.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x80020) == 0x80020,
                placement is { } hitPosition ? NativeWindows.RootWindowAt(hitPosition.Left + hitPosition.Width / 2, hitPosition.Top + hitPosition.Height / 2) : 0);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            overlay.Hide();
            State = new((long)target, (long)foreground, false, NativeWindows.GetForegroundWindow() == foreground, null, error.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        overlay.EndPointerDrag(); disposed = true; timer.Stop();
        overlay.DragStarted -= BeginDrag; overlay.DragMoved -= MoveDrag; overlay.DragFinished -= FinishDrag;
        foreach (var hook in hooks) NativeWindows.UnhookWinEvent(hook);
        hooks.Clear();
        GC.KeepAlive(callback);
    }
}












