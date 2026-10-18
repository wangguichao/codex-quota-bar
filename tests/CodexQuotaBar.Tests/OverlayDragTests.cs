using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class OverlayDragTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void PixelDragUsesTargetDpiAndKeepsRelativePosition(uint dpi)
    {
        var target = new PixelRect(10, 20, 3010, 2220);
        var drag = new OverlayDragSession(target, dpi, 540, 0, 44, 1000, 500);
        var scale = dpi / 96d;
        var position = drag.MoveTo(1000 + (int)(200 * scale), 500 + (int)(100 * scale));
        Assert.Equal(200, position.OffsetX); Assert.Equal(144, position.OffsetY);
        var original = OverlayPolicy.Place(target, dpi, 540, position.OffsetY, position.OffsetX)!.Value;
        var moved = OverlayPolicy.Place(new(60, 50, 3060, 2250), dpi, 540, position.OffsetY, position.OffsetX)!.Value;
        Assert.Equal(original.Left + 50, moved.Left); Assert.Equal(original.Top + 30, moved.Top);
    }
    [Fact]
    public void OffsetsAreClampedInsideNegativeCoordinateTarget()
    {
        var target = new PixelRect(-1600, -200, 0, 700);
        var drag = new OverlayDragSession(target, 120, 540, 0, 44, 100, 100);
        var topLeft = drag.MoveTo(int.MinValue, int.MinValue);
        var first = OverlayPolicy.Place(target, 120, 540, topLeft.OffsetY, topLeft.OffsetX)!.Value;
        Assert.InRange(first.Left, target.Left, target.Left + 16); Assert.Equal(target.Top, first.Top);
        var bottomRight = drag.MoveTo(int.MaxValue, int.MaxValue);
        var last = OverlayPolicy.Place(target, 120, 540, bottomRight.OffsetY, bottomRight.OffsetX)!.Value;
        Assert.True(last.Left + last.Width <= target.Right); Assert.True(last.Top + last.Height <= target.Bottom);
        Assert.InRange(target.Right - (last.Left + last.Width), 14, 16);
    }
    [Fact]
    public void StartingFromClampedSavedOffsetsDoesNotJump()
    {
        var target = new PixelRect(100, 200, 1100, 1000);
        var before = OverlayPolicy.Place(target, 120, 540, 100000, 100000)!.Value;
        var drag = new OverlayDragSession(target, 120, 540, 100000, 100000, 700, 400);
        var position = drag.MoveTo(700, 400);
        var after = OverlayPolicy.Place(target, 120, 540, position.OffsetY, position.OffsetX)!.Value;
        Assert.InRange(Math.Abs(before.Left - after.Left), 0, 1); Assert.InRange(Math.Abs(before.Top - after.Top), 0, 1);
    }
    [Fact]
    public void InvalidGeometryCannotStartDrag()
        => Assert.Throws<ArgumentException>(() => new OverlayDragSession(new(0, 0, 10, 10), 120, 540, 0, 44, 0, 0));
}

