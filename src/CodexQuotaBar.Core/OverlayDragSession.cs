namespace CodexQuotaBar.Core;

public readonly record struct OverlayPosition(int OffsetX, int OffsetY);

/// <summary>Converts screen-pixel pointer motion to a saved, target-relative logical position.</summary>
public sealed class OverlayDragSession
{
    private readonly PixelRect target;
    private readonly uint dpi;
    private readonly double preferredWidth;
    private readonly OverlayPlacement start, centered;
    private readonly int pointerX, pointerY;
    public OverlayDragSession(PixelRect target, uint dpi, double preferredWidth, int offsetX, int offsetY, int pointerX, int pointerY)
    {
        this.target = target; this.dpi = dpi; this.preferredWidth = preferredWidth;
        start = OverlayPolicy.Place(target, dpi, preferredWidth, offsetY, offsetX) ?? throw new ArgumentException("Target cannot contain the overlay.");
        centered = OverlayPolicy.Place(target, dpi, preferredWidth, 0)!.Value;
        this.pointerX = pointerX; this.pointerY = pointerY;
    }
    public OverlayPosition MoveTo(int x, int y)
    {
        var scale = dpi / 96d;
        var offsetX = (int)Math.Round(Math.Clamp((start.Left - centered.Left + (long)x - pointerX) / scale, -100000, 100000));
        var offsetY = (int)Math.Round(Math.Clamp((start.Top - target.Top + (long)y - pointerY) / scale, 0, 100000));
        var actual = OverlayPolicy.Place(target, dpi, preferredWidth, offsetY, offsetX)!.Value;
        return new((int)Math.Round((actual.Left - centered.Left) / scale), (int)Math.Round((actual.Top - target.Top) / scale));
    }
}

