using System.Windows;
using System.Windows.Interop;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

internal static class PreviewSmokeTest
{
    internal static async Task RunAsync(Application app, string reportPath)
    {
        var results = new List<object>();
        var success = true;
        var fixture = new Window { Title = "QuotaBar 预览验证目标", Width = 1000, Height = 650 };
        PreviewWindow? preview = null;
        try
        {
            fixture.Show();
            var identity = NativeWindows.Read(new WindowInteropHelper(fixture).Handle) ?? throw new InvalidOperationException();
            preview = new PreviewWindow(System.IO.Path.ChangeExtension(reportPath, ".state.json"), identity);
            preview.Show(); preview.Activate();
            async Task Check(string name, Action action, PreviewStatus expected)
            {
                action();
                for (var attempt = 0; attempt < 30 && preview.Status != expected; attempt++) await Task.Delay(100);
                var passed = preview.Status == expected;
                if (expected == PreviewStatus.Showing)
                    passed &= preview.IsOverlayRendered && preview.TrackerState?.NativeVisible == true
                        && preview.TrackerState.FocusPreserved && preview.TrackerState.ForegroundHandle != identity.Handle;
                success &= passed;
                results.Add(new { name, passed, status = preview.Status.ToString(), preview.IsOverlayRendered, state = preview.TrackerState });
            }
            await Check("controller_foreground_keeps_rendered_overlay", () => { }, PreviewStatus.Showing);
            await Check("stop_hides_overlay", preview.StopPreview, PreviewStatus.Stopped);
            await Check("restart_shows_overlay", preview.StartPreview, PreviewStatus.Showing);
            var blockedPath = System.IO.Path.ChangeExtension(reportPath, ".blocked");
            System.IO.Directory.CreateDirectory(blockedPath);
            PreviewWindow? blocked = null;
            try
            {
                blocked = new PreviewWindow(blockedPath, identity);
                blocked.Show(); blocked.Activate();
                for (var attempt = 0; attempt < 30 && blocked.Status != PreviewStatus.Showing; attempt++) await Task.Delay(100);
                var passed = blocked.IsVisible && blocked.Status == PreviewStatus.Showing && blocked.HasDiagnosticError;
                success &= passed;
                results.Add(new { name = "diagnostic_failure_keeps_preview_usable", passed });
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
                success = false;
                results.Add(new { name = "diagnostic_failure_keeps_preview_usable", passed = false, errorType = error.GetType().Name });
            }
            finally { blocked?.Close(); }
            // The preview does not contain a lifetime timer; close remains the explicit cleanup path.
            Program.WriteReport(reportPath, new { success, autoExpires = false, results });
        }
        catch (Exception error)
        {
            success = false;
            Program.WriteReport(reportPath, new { success, errorType = error.GetType().Name, results });
        }
        finally { preview?.Close(); fixture.Close(); app.Shutdown(success ? 0 : 1); }
    }
}



