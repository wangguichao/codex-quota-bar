using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CodexQuotaBar.Core;
using Forms = System.Windows.Forms;

namespace CodexQuotaBar;

internal static class DragTraySmokeTest
{
    private sealed class Provider : IUsageProvider
    {
        internal int Reads;
        public async Task<UsageSnapshot> ReadAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Reads); await Task.Delay(30, token);
            return new(DateTimeOffset.UtcNow, "EMAIL", "QUOTA", true, [new("codex", "Codex", [new("primary", 7, 10080, DateTimeOffset.UtcNow.AddDays(1))])]);
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
    private static async Task CloseAsync(LiveWindow window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(); window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static async Task<(bool Moved, bool Released, bool Focus)> CheckBackgroundPointerAsync(string reportPath)
    {
        var childPath = Path.ChangeExtension(reportPath, ".child.json"); File.Delete(childPath);
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException())
        { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("--click-target"); info.ArgumentList.Add(childPath);
        using var child = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException();
        OverlayWindow? overlay = null; WindowTracker? tracker = null;
        try
        {
            for (var i = 0; i < 40 && !File.Exists(childPath); i++) await Task.Delay(50);
            using var childReport = System.Text.Json.JsonDocument.Parse(File.ReadAllText(childPath));
            var handle = (nint)childReport.RootElement.GetProperty("handle").GetInt64();
            var target = NativeWindows.Read(handle) ?? throw new InvalidOperationException();
            for (var i = 0; i < 20 && NativeWindows.GetForegroundWindow() != handle; i++) await Task.Delay(50);
            overlay = new OverlayWindow(OverlayWindow.CreateBarContent(), false);
            tracker = new(overlay, target.Executable, 540, 44, handle);
            var start = tracker.State.Placement ?? throw new InvalidOperationException();
            var sample = new PointerSample(true, start.Left + 20, start.Top + 10);
            overlay.PointerReader = () => sample;
            overlay.BeginPointerDrag(sample.X, sample.Y);
            sample = sample with { X = sample.X + 260, Y = sample.Y + 160 };
            await Task.Delay(100);
            var actual = tracker.State.Placement!.Value;
            var moved = overlay.IsPointerPolling && Math.Abs(actual.Left - start.Left - 260) <= 1 && Math.Abs(actual.Top - start.Top - 160) <= 1;
            sample = sample with { Pressed = false };
            await Task.Delay(80);
            return (moved, !overlay.IsDragging && !overlay.IsPointerPolling, NativeWindows.GetForegroundWindow() == handle && target.ProcessId != Environment.ProcessId);
        }
        finally
        {
            tracker?.Dispose(); overlay?.Close();
            if (!child.HasExited) { child.CloseMainWindow(); if (!child.WaitForExit(3000)) { child.Kill(true); child.WaitForExit(); } }
        }
    }

    internal static async Task RunAsync(Application app, string reportPath)
    {
        var results = new List<object>(); var success = true; LiveWindow? window = null; var closed = false;
        var fixture = new Window { Title = "QuotaBar 拖动与托盘测试（测试数据）", Width = 1100, Height = 800, Left = 100, Top = 80 };
        var settings = Path.ChangeExtension(reportPath, ".settings.json"); File.Delete(settings);
        void Check(string name, bool passed) { success &= passed; results.Add(new { name, passed }); }
        try
        {
            fixture.Show(); var identity = NativeWindows.Read(new WindowInteropHelper(fixture).Handle) ?? throw new InvalidOperationException();
            var source = new Provider();
            LiveWindow Create() => new(new("", new("EMAIL", "QUOTA", "personal")), Path.ChangeExtension(reportPath, ".state.json"), source, identity, settings);
            window = Create(); window.Show(); window.Activate();
            for (var i = 0; i < 40 && window.Usage.State != UsageState.Current; i++) await Task.Delay(100);
            var overlay = window.FloatingOverlay ?? throw new InvalidOperationException();
            Check("tray_icon_created", window.Tray?.Visible == true);
            Check("bar_starts_unlocked", !overlay.PositionLocked && !window.Preferences.PositionLocked);
            var initial = window.Tracking!.Placement!.Value; var foreground = NativeWindows.GetForegroundWindow();
            var began = overlay.BeginPointerDrag(initial.Left + 30, initial.Top + 14, false);
            overlay.MovePointerDrag(initial.Left + 230, initial.Top + 164); overlay.EndPointerDrag();
            var moved = window.Tracking!.Placement!.Value;
            Check("drag_moves_horizontally_and_vertically", began && Math.Abs(moved.Left - initial.Left - 200) <= 1 && Math.Abs(moved.Top - initial.Top - 150) <= 1);
            Check("drag_preserves_foreground_focus", NativeWindows.GetForegroundWindow() == foreground && window.Tracking.FocusPreserved);
            Check("drag_position_saved_after_release", new PreferenceStore(settings).Load().Settings.Preferences == window.Preferences);
            var lockBox = Descendants<CheckBox>(window).Single(c => AutomationProperties.GetAutomationId(c) == "position-lock");
            lockBox.IsChecked = true;
            var beforeLock = window.Preferences;
            Check("locked_bar_has_native_click_through", window.Preferences.PositionLocked && (NativeWindows.GetWindowLongPtr(overlay.Handle, -20).ToInt64() & 0x80020) == 0x80020 &&
                NativeWindows.RootWindowAt(moved.Left + moved.Width / 2, moved.Top + moved.Height / 2) != (long)overlay.Handle);
            Check("locked_bar_ignores_drag", !overlay.BeginPointerDrag(100, 100, false) && window.Preferences == beforeLock);
            lockBox.IsChecked = false;
            Check("unlock_restores_pointer_hit_target", !overlay.PositionLocked && NativeWindows.RootWindowAt(moved.Left + moved.Width / 2, moved.Top + moved.Height / 2) == (long)overlay.Handle);
            overlay.BeginPointerDrag(moved.Left + 10, moved.Top + 10, false); overlay.MovePointerDrag(int.MinValue, int.MinValue); overlay.EndPointerDrag();
            var clamped = window.Tracking!.Placement!.Value;
            Check("drag_cannot_lose_bar_outside_target", clamped.Left >= identity.Bounds.Left && clamped.Top >= identity.Bounds.Top &&
                clamped.Left + clamped.Width <= identity.Bounds.Right && clamped.Top + clamped.Height <= identity.Bounds.Bottom);
            ((Forms.ToolStripMenuItem)window.Tray!.Menu.Items["reset"]!).PerformClick();
            Check("tray_reset_restores_default_position", window.Preferences.OffsetX == 0 && window.Preferences.OffsetY == 44);
            var reset = window.Tracking!.Placement!.Value;
            overlay.BeginPointerDrag(reset.Left + 20, reset.Top + 12, false); overlay.MovePointerDrag(reset.Left + 140, reset.Top + 112);
            overlay.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });
            Check("lost_capture_commits_drag_and_releases_state", !overlay.IsDragging && new PreferenceStore(settings).Load().Settings.Preferences == window.Preferences);
            var beforeMove = window.Tracking!.Placement!.Value;
            fixture.Left += 40; fixture.Top += 30; await Task.Delay(400);
            var afterMove = window.Tracking!.Placement!.Value;
            Check("dragged_bar_continues_following_target", Math.Abs(afterMove.Left - beforeMove.Left - 50) <= 1 && Math.Abs(afterMove.Top - beforeMove.Top - 38) <= 1);
            window.WindowState = WindowState.Maximized; await Task.Delay(80); window.WindowState = WindowState.Minimized; await Task.Delay(100);
            Check("minimize_hides_window_and_taskbar_keeps_tray", window.IsInTray && !window.IsVisible && !window.ShowInTaskbar && window.Tray.Visible);
            fixture.Activate(); await Task.Delay(350);
            Check("bar_remains_usable_with_settings_in_tray", window.Tracking?.NativeVisible == true);
            var reads = source.Reads; await window.RefreshAsync();
            Check("quota_read_continues_while_in_tray", source.Reads > reads && window.Usage.State == UsageState.Current && !window.SourceStopped);
            ((Forms.ToolStripMenuItem)window.Tray.Menu.Items["restore"]!).PerformClick(); await Task.Delay(100);
            Check("tray_menu_restores_previous_maximized_state", !window.IsInTray && window.IsVisible && window.ShowInTaskbar && window.WindowState == WindowState.Maximized);
            window.MinimizeToTray();
            var signalName = "Local\\QuotaBar-test-" + Guid.NewGuid().ToString("N");
            using (var signal = new InstanceRestoreSignal(signalName, () => app.Dispatcher.BeginInvoke(new Action(window.RestoreFromTray))))
            {
                InstanceRestoreSignal.Send(signalName);
                for (var i = 0; i < 30 && window.IsInTray; i++) await Task.Delay(50);
                Check("duplicate_launch_signal_restores_tray_window", window.IsVisible && !window.IsInTray);
            }
            var earlyName = "Local\\QuotaBar-early-" + Guid.NewGuid().ToString("N");
            using (var early = InstanceRestoreSignal.Prepare(earlyName))
            {
                early.Set();
                var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var listener = new InstanceRestoreSignal(early, () => received.TrySetResult());
                await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Check("restore_request_before_listener_is_retained", received.Task.IsCompletedSuccessfully);
            }
            var tray = window.Tray;
            for (var i = 0; i < 3; i++) { window.MinimizeToTray(); window.RestoreFromTray(); }
            Check("repeated_restore_reuses_one_tray_icon", ReferenceEquals(tray, window.Tray) && tray.Visible);
            window.WindowState = WindowState.Normal; window.MinimizeToTray(); fixture.Activate(); await Task.Delay(300);
            var last = window.Tracking!.Placement!.Value;
            overlay.BeginPointerDrag(last.Left + 10, last.Top + 10, false); overlay.MovePointerDrag(last.Left + 90, last.Top + 60);
            var saved = window.Preferences;
            await CloseAsync(window); closed = true;
            Check("exit_while_in_tray_stops_query_and_disposes_icon", window.SourceStopped && window.Tray.IsDisposed && !NativeWindows.IsWindow(overlay.Handle));
            window = Create(); closed = false;
            Check("dragged_position_survives_restart", window.Preferences.OffsetX == saved.OffsetX && window.Preferences.OffsetY == saved.OffsetY);
            window.Show(); await Task.Delay(150);
            await CloseAsync(window); closed = true;
            Check("restarted_window_cleans_up_tray", window.Tray?.IsDisposed == true);
            fixture.Activate(); await Task.Delay(50);
            var background = await CheckBackgroundPointerAsync(reportPath);
            Check("background_polling_follows_pointer_outside_bar", background.Moved);
            Check("background_polling_detects_release_without_mouse_up", background.Released);
            Check("background_polling_keeps_separate_process_in_foreground", background.Focus);
            Program.WriteReport(reportPath, new { success, simulated = true, pointerInput = "Own-window drag handlers exercised with screen coordinates; no injected physical mouse input.", results });
        }
        catch (Exception error) { success = false; Program.WriteReport(reportPath, new { success, errorType = error.GetType().Name, results }); }
        finally
        {
            if (window is not null && !closed) await CloseAsync(window);
            fixture.Close(); app.Shutdown(success ? 0 : 1);
        }
    }
}




