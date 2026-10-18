namespace CodexQuotaBar.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
public readonly record struct OverlayPlacement(int Left, int Top, int Width, int Height);

public static class OverlayPolicy
{
    public static bool ShouldShow(bool targetVisible, bool minimized, bool targetForeground, bool ownInteraction)
        => targetVisible && !minimized && (targetForeground || ownInteraction);

    public static OverlayPlacement? Place(PixelRect target, uint dpi, double preferredWidth, double offsetY, double offsetX = 0)
    {
        if (dpi is < 48 or > 768 || !double.IsFinite(preferredWidth) || !double.IsFinite(offsetY) || !double.IsFinite(offsetX))
            return null;
        var scale = dpi / 96d;
        var margin = (int)Math.Round(12 * scale);
        var height = (int)Math.Round(28 * scale);
        var minimum = (int)Math.Round(180 * scale);
        var available = target.Width - 2 * margin;
        if (available < minimum || target.Height < height + 2 * margin)
            return null;
        var width = Math.Min(available, (int)Math.Round(Math.Clamp(preferredWidth, 180, 720) * scale));
        var top = target.Top + (int)Math.Round(Math.Clamp(offsetY * scale, 0, target.Height - height));
        var centered = target.Left + (target.Width - width) / 2;
        var left = (int)Math.Round(Math.Clamp(centered + offsetX * scale, target.Left + margin, target.Right - margin - width));
        return new(left, top, width, height);
    }
}




