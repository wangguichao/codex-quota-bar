using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
namespace CodexQuotaBar;

/// <summary>Separate-process fixture for actual mouse hit-testing.</summary>
internal static class ClickTarget
{
    internal static int Run(Application app, string reportPath)
    {
        var window = new Window { Title = "QuotaBar Click Target", Width = 1000, Height = 500, Left = 80, Top = 100 };
        var canvas = new Canvas();
        var button = new Button { Content = "点击穿透验证 · 0", Width = 850, Height = 65, FontSize = 18 };
        Canvas.SetTop(button, 0); Canvas.SetLeft(button, 40); canvas.Children.Add(button);
        var text = new TextBlock { Text = "独立进程测试窗口，按钮应能穿过模拟浮层接收鼠标点击。", FontSize = 16 };
        Canvas.SetLeft(text, 40); Canvas.SetTop(text, 110); canvas.Children.Add(text);
        window.Content = canvas;
        var clicks = 0;
        void Save() => Program.WriteReport(reportPath, new
        {
            processId = Environment.ProcessId,
            handle = (long)new WindowInteropHelper(window).Handle,
            clicks
        });
        window.Loaded += (_, _) => Save();
        button.Click += (_, _) => { button.Content = $"点击穿透验证 · {++clicks}"; Save(); };
        var end = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        end.Tick += (_, _) => window.Close();
        window.Closed += (_, _) => { end.Stop(); Save(); app.Shutdown(); };
        end.Start();
        return app.Run(window);
    }
}
