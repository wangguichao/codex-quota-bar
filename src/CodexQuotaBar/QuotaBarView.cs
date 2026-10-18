using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CodexQuotaBar.Core;

namespace CodexQuotaBar;

internal sealed class QuotaBarView : Border
{
    private readonly ColumnDefinition filled = new();
    private readonly ColumnDefinition empty = new();
    private readonly Border fill = new() { CornerRadius = new CornerRadius(4) };
    private readonly TextBlock label = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
    private readonly TextBlock title = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock percentage = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    internal TextBlock PercentageElement => percentage;

    internal QuotaBarView()
    {
        Height = 28; CornerRadius = new CornerRadius(6); BorderThickness = new Thickness(1);
        var grid = new Grid { Margin = new Thickness(9, 0, 9, 0) };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(label);
        var meter = new Grid { Height = 18, VerticalAlignment = VerticalAlignment.Center };
        meter.ColumnDefinitions.Add(filled); meter.ColumnDefinitions.Add(empty); meter.Children.Add(fill);
        var text = new Grid { Margin = new Thickness(6, 0, 6, 0) };
        text.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        text.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        text.Children.Add(title); Grid.SetColumn(percentage, 1); text.Children.Add(percentage);
        Grid.SetColumnSpan(text, 2); meter.Children.Add(text);
        Grid.SetColumn(meter, 1); grid.Children.Add(meter); Child = grid;
        SizeChanged += (_, _) => { label.Visibility = ActualWidth < 320 ? Visibility.Collapsed : Visibility.Visible; };
    }
    internal void Update(QuotaPresentation presentation, bool testData = false, BarTheme theme = BarTheme.Dark, bool highContrast = false)
    {
        label.Text = testData ? "测试数据" : "个人空间";
        title.Text = presentation.Title; percentage.Text = presentation.PercentText;
        AutomationProperties.SetName(this, presentation.Title + " " + presentation.PercentText);
        ToolTip = presentation.Status + " · " + presentation.ResetText;
        var remaining = presentation.RemainingPercent ?? 0;
        filled.Width = new GridLength(remaining, GridUnitType.Star);
        empty.Width = new GridLength(100 - remaining, GridUnitType.Star);
        var light = theme == BarTheme.Light;
        Background = highContrast ? SystemColors.WindowBrush : Brush(light ? 0xF5F8FA : 0x1B1F27);
        BorderBrush = highContrast ? SystemColors.WindowTextBrush : Brush(light ? 0xABB6C3 : 0x434A57);
        var foreground = highContrast ? SystemColors.WindowTextBrush : light ? Brushes.Black : Brushes.White;
        title.Foreground = percentage.Foreground = label.Foreground = foreground;
        fill.Background = highContrast ? Brushes.Transparent : presentation.Tone switch
        {
            QuotaTone.Green => Brush(light ? 0xCCE9DC : 0x266C59),
            QuotaTone.Amber => Brush(light ? 0xF3DDAA : 0x89631D),
            QuotaTone.Red => Brush(light ? 0xF1C9CD : 0x9B393E),
            _ => Brushes.Transparent
        };
    }
    private static SolidColorBrush Brush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}


