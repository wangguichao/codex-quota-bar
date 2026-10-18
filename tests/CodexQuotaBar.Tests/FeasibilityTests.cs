using System.Text.Json;
using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class FeasibilityTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void PlacementScalesAndRemainsWithinTarget(uint dpi)
    {
        var target = new PixelRect(-1600, 20, 0, 1020);
        var result = OverlayPolicy.Place(target, dpi, 540, 44);
        Assert.NotNull(result);
        Assert.True(result.Value.Left >= target.Left);
        Assert.True(result.Value.Left + result.Value.Width <= target.Right - 12 * dpi / 96);
        Assert.Equal((int)Math.Round(28 * dpi / 96d), result.Value.Height);
        Assert.Equal(target.Top + (int)Math.Round(44 * dpi / 96d), result.Value.Top);
    }
    [Fact]
    public void InvalidOrTooSmallWindowsHaveNoOverlay()
    {
        Assert.Null(OverlayPolicy.Place(new PixelRect(0, 0, 120, 200), 96, 540, 44));
        Assert.Null(OverlayPolicy.Place(new PixelRect(0, 0, 800, 40), 96, 540, 44));
        Assert.Null(OverlayPolicy.Place(new PixelRect(0, 0, 800, 600), 0, 540, 44));
    }
    [Fact]
    public void ExtremePreferencesAreClamped()
    {
        var result = OverlayPolicy.Place(new PixelRect(0, 0, 800, 600), 96, 5000, -100);
        Assert.NotNull(result);
        Assert.True(result.Value.Top >= 0);
        Assert.True(result.Value.Left + result.Value.Width <= 788);
    }
    [Theory]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, false, true, true)]
    public void VisibilityRespectsForegroundAndMinimization(bool visible, bool minimized, bool foreground, bool ownInteraction, bool expected)
        => Assert.Equal(expected, OverlayPolicy.ShouldShow(visible, minimized, foreground, ownInteraction));
    [Fact]
    public void EvidenceNeverPersistsRawIdentityOrSecrets()
    {
        using var account = JsonDocument.Parse("""{"account":{"type":"chatgpt","email":"secret@example.com","planType":"plus","access_token":"SECRET_TOKEN"},"cookie":"PRIVATE_COOKIE"}""");
        using var usage = JsonDocument.Parse("""{"accountId":"PRIVATE_ACCOUNT","rateLimits":{"limitId":"codex","primary":{"usedPercent":33,"windowDurationMins":300,"resetsAt":1900000000,"secret":"PRIVATE"}},"token":"SECRET_TOKEN"}""");
        var evidence = ProbeEvidence.Sanitize(account.RootElement, usage.RootElement);
        var json = JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("secret@example.com", json);
        Assert.DoesNotContain("PRIVATE_ACCOUNT", json);
        Assert.DoesNotContain("SECRET_TOKEN", json);
        Assert.DoesNotContain("PRIVATE_COOKIE", json);
        Assert.Contains("usedPercent", json);
        Assert.Contains("33", json);
        Assert.False(evidence.DesktopIdentityVerified);
        Assert.Equal("chatgpt", evidence.AuthType);
        Assert.NotNull(evidence.AccountFingerprint);
    }
    [Fact]
    public void MissingDataRemainsUnavailable()
    {
        using var empty = JsonDocument.Parse("{}");
        var evidence = ProbeEvidence.Sanitize(empty.RootElement, empty.RootElement);
        Assert.Null(evidence.AuthType);
        Assert.Null(evidence.AccountFingerprint);
        Assert.Null(evidence.RateLimits);
        Assert.Null(evidence.RateLimitsByLimitId);
        Assert.False(evidence.DesktopIdentityVerified);
    }
    [Fact]
    public void MultiplePoolsStaySeparateAndExcludeUnknownFields()
    {
        using var empty = JsonDocument.Parse("{}");
        using var usage = JsonDocument.Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":12},"credits":{"balance":"PRIVATE"}},"other":{"secondary":{"usedPercent":70}}}}""");
        var evidence = ProbeEvidence.Sanitize(empty.RootElement, usage.RootElement);
        Assert.Equal(2, evidence.RateLimitsByLimitId!.Count);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(evidence));
    }
}
