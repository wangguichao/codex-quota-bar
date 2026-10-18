using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

internal sealed class QuotaAlertWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(8) };
    internal QuotaAlertWindow(IReadOnlyList<QuotaAlert> alerts, bool testData)
    {
        Title = testData ? "Codex额度条 · 测试提醒" : "Codex额度条 · 低额度提醒";
        Width = 370; Height = alerts.Count > 1 ? 220 : 145; ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.ToolWindow;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 250));
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = testData ? "测试数据 · 低额度提醒" : "Codex 低额度提醒", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = string.Join("\n", alerts.Select(a => a.Message)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }
        });
        Content = panel;
        Loaded += (_, _) => { var area = SystemParameters.WorkArea; Left = area.Right - Width - 20; Top = area.Bottom - Height - 20; timer.Start(); };
        timer.Tick += (_, _) => Close();
        Closed += (_, _) => timer.Stop();
    }
}

