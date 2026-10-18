using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CodexQuotaBar;

/// <summary>Exercises only windows owned by this prototype; never moves the user's apps.</summary>
internal static class DesktopSelfTest
{
    internal static async Task RunAsync(Application app, string reportPath)
    {
        var results = new List<object>();
        var success = true;
        var fixture = new Window
        {
            Title = "QuotaBar 测试窗口 · 模拟数据",
            Width = 1000,
            Height = 650,
            Left = 80,
            Top = 100,
            Content = new TextBox { Text = "窗口跟随测试：此窗口仅用于原型验收。", Margin = new Thickness(20, 80, 20, 20), AcceptsReturn = true }
        };
        OverlayWindow? overlay = null;
        WindowTracker? tracker = null;
        Window? other = null;
        try
        {
            fixture.Show();
            fixture.Activate();
            var handle = new WindowInteropHelper(fixture).Handle;
            var identity = NativeWindows.Read(handle) ?? throw new InvalidOperationException("Test window missing");
            overlay = new OverlayWindow();
            tracker = new WindowTracker(overlay, identity.Executable, 540, 44, handle);
            async Task Check(string name, Action action, Func<bool> expected)
            {
                action();
                await Task.Delay(450);
                tracker.Tick();
                var actual = NativeWindows.Read(overlay.Handle);
                var position = tracker.State.Placement;
                var geometryMatches = position is null || (actual is not null && actual.Bounds.Left == position.Value.Left && actual.Bounds.Top == position.Value.Top && actual.Bounds.Width == position.Value.Width && actual.Bounds.Height == position.Value.Height);
                var passed = expected() && tracker.State.FocusPreserved && geometryMatches && tracker.State.NativeVisible == tracker.State.Visible && tracker.State.LayeredClickThrough;
                success &= passed;
                results.Add(new { name, passed, geometryMatches, state = tracker.State });
            }
            await Check("foreground_shows_without_focus_theft", () => fixture.Activate(), () => tracker.State.Visible);
            overlay.UpdateLayout();
            var pixels = new RenderTargetBitmap(675, 35, 120, 120, PixelFormats.Pbgra32);
            pixels.Render(overlay);
            var rawPixels = new byte[675 * 35 * 4]; pixels.CopyPixels(rawPixels, 675 * 4, 0);
            var opaquePixels = Enumerable.Range(0, 675 * 35).Count(i => rawPixels[i * 4 + 3] > 128);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(pixels));
            using (var preview = File.Create(Path.ChangeExtension(reportPath, ".png"))) encoder.Save(preview);
            results.Add(new { name = "rendered_pixels", passed = opaquePixels > 1000, opaquePixels, overlay.ActualWidth, overlay.ActualHeight, overlay.IsLoaded, overlay.Opacity });
            success &= opaquePixels > 1000;
            var oldLeft = tracker.State.Placement?.Left;
            await Check("move_follows", () => fixture.Left += 120, () => tracker.State.Visible && tracker.State.Placement?.Left != oldLeft);
            await Check("resize_follows", () => fixture.Width = 760, () => tracker.State.Visible && tracker.State.Placement?.Width <= 736);
            await Check("maximize_follows", () => fixture.WindowState = WindowState.Maximized, () => tracker.State.Visible);
            await Check("minimize_hides", () => fixture.WindowState = WindowState.Minimized, () => !tracker.State.Visible);
            await Check("restore_shows", () => { fixture.WindowState = WindowState.Normal; fixture.Activate(); }, () => tracker.State.Visible);
            other = new Window { Title = "QuotaBar 其他测试窗口", Width = 360, Height = 220 };
            await Check("other_foreground_hides", () => { other.Show(); other.Activate(); }, () => !tracker.State.Visible);
            await Check("return_to_target_shows", () => fixture.Activate(), () => tracker.State.Visible);
            await Check("closed_target_hides", fixture.Close, () => !tracker.State.Visible);
            Program.WriteReport(reportPath, new
            {
                capturedAtUtc = DateTimeOffset.UtcNow,
                success,
                simulated = true,
                target = identity,
                hooksAvailable = tracker.HooksAvailable,
                results
            });
        }
        catch (Exception error)
        {
            success = false;
            Program.WriteReport(reportPath, new { success, errorType = error.GetType().Name, results });
        }
        finally
        {
            tracker?.Dispose(); overlay?.Close(); other?.Close(); fixture.Close();
            app.Shutdown(success ? 0 : 1);
        }
    }
}




