using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace CodexQuotaBar;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Length == 2 && args[0] == "--inspect-windows")
        {
            WriteReport(args[1], new { capturedAtUtc = DateTimeOffset.UtcNow, windows = NativeWindows.DiscoverCodex(), displays = NativeWindows.Displays() });
            return 0;
        }
        if (args.Length == 2 && args[0] == "--click-target") return ClickTarget.Run(app, args[1]);
        if (args.Length == 2 && args[0] == "--preview-self-test")
        {
            app.Startup += async (_, _) => await PreviewSmokeTest.RunAsync(app, args[1]);
            return app.Run();
        }
        if (args.Length == 2 && args[0] == "--self-test")
        {
            app.Startup += async (_, _) => await DesktopSelfTest.RunAsync(app, args[1]);
            return app.Run();
        }
        if (args.Length == 2 && args[0] == "--setup-self-test")
        {
            app.Startup += async (_, _) => await SetupSmokeTest.RunAsync(app, args[1]); return app.Run();
        }
        if (args.Length == 2 && args[0] == "--interaction-self-test")
        {
            app.Startup += async (_, _) => await DragTraySmokeTest.RunAsync(app, args[1]); return app.Run();
        }
        if (args.Length == 2 && args[0] == "--live-self-test")
        {
            app.Startup += async (_, _) => await LiveSmokeTest.RunAsync(app, args[1]);
            return app.Run();
        }
        if (args.Contains("--demo"))
        {
            try
            {
                var target = Option(args, "--target-exe") ??
                    NativeWindows.DiscoverCodex().Select(w => w.Executable).Distinct(StringComparer.OrdinalIgnoreCase).SingleOrDefault();
                if (target is null || !File.Exists(target)) throw new ArgumentException("无法确定唯一目标，请使用 --target-exe 指定完整路径。");
                var seconds = Number(args, "--seconds", 30, 5, 600);
                var overlay = new OverlayWindow();
                using var tracker = new WindowTracker(overlay, target, Number(args, "--width", 540, 180, 720),
                    Number(args, "--offset-y", 44, 0, 500));
                var samples = new List<TrackerState>();
                var sampleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
                    (_, _) => samples.Add(tracker.State), app.Dispatcher);
                var endTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
                overlay.Closed += (_, _) => { endTimer.Stop(); sampleTimer.Stop(); app.Shutdown(); };
                endTimer.Tick += (_, _) =>
                {
                    endTimer.Stop(); sampleTimer.Stop(); var hooksAvailable = tracker.HooksAvailable; tracker.Dispose();
                    if (Option(args, "--report") is { } path)
                        WriteReport(path, new { simulated = true, hooksAvailable, samples });
                    overlay.Close();
                };
                endTimer.Start();
                return app.Run();
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                MessageBox.Show(error.Message, "Codex额度条 · 参数错误");
                return 2;
            }
        }
        if (!args.Contains("--preview"))
        {
            var instanceName = "Local\\CodexQuotaBar.Live." + CodexQuotaBar.Core.IdentityBinding.Fingerprint(Environment.UserName);
            using var request = InstanceRestoreSignal.Prepare(instanceName + ".Restore");
            using var instance = new Mutex(true, instanceName, out var firstInstance);
            if (!firstInstance) { request.Set(); return 0; }
            var configPath = Option(args, "--config") ?? AppPaths.Configuration;
            var reportPath = Option(args, "--report") ?? AppPaths.Report;
            Window? activeWindow = null;
            void ShowSetup()
            {
                var setup = new SetupWindow(configPath);
                activeWindow = setup;
                var configured = false;
                setup.Configured += config => { configured = true; setup.Close(); ShowLive(config); };
                setup.Closed += (_, _) => { if (!configured) app.Shutdown(); };
                setup.Show();
            }
            void ShowLive(LiveConfiguration config)
            {
                var live = new LiveWindow(config, reportPath, settingsPath: Option(args, "--settings") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, "display.local.json"));
                activeWindow = live;
                var reconfigure = false;
                live.ReconfigureRequested += () => { reconfigure = true; live.Close(); };
                live.Closed += (_, _) => { if (reconfigure) ShowSetup(); else app.Shutdown(); };
                if (args.Contains("--tray")) live.Loaded += (_, _) => live.MinimizeToTray();
                live.Show();
            }
            using var restore = new InstanceRestoreSignal(request, () =>
            {
                if (!app.Dispatcher.HasShutdownStarted) _ = app.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                {
                    if (activeWindow is LiveWindow live) live.RestoreFromTray();
                    else if (activeWindow is { } window) { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); }
                }));
            });
            app.Startup += (_, _) =>
            {
                var config = LiveConfiguration.Load(configPath);
                if (config is null || args.Contains("--setup")) ShowSetup(); else ShowLive(config);
            };
            return app.Run();
        }
        var preview = new PreviewWindow(Option(args, "--report"));
        preview.Closed += (_, _) => app.Shutdown();
        return app.Run(preview);
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private static double Number(string[] args, string name, double fallback, double minimum, double maximum)
    {
        var value = Option(args, name);
        var number = value is null ? fallback : double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        if (!double.IsFinite(number) || number < minimum || number > maximum) throw new ArgumentException(name + " 超出允许范围");
        return number;
    }
    internal static void WriteReport(string path, object report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions));
    }
}
















