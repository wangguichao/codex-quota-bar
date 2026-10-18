using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class PresentationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1792210000);
    [Theory]
    [InlineData(UsageState.Connecting)]
    [InlineData(UsageState.Stale)]
    [InlineData(UsageState.ReadError)]
    [InlineData(UsageState.Timeout)]
    [InlineData(UsageState.NeedsLogin)]
    [InlineData(UsageState.IdentityUnverified)]
    [InlineData(UsageState.Incompatible)]
    [InlineData(UsageState.NoData)]
    [InlineData(UsageState.Stopped)]
    public void EveryUnavailableStateHidesTheOldNumericPercentage(UsageState state)
    {
        var snapshot = new UsageSnapshot(Now, "E", "U", true, [new("codex", "Codex", [new("primary", 7, 10080, Now.AddDays(1))])]);
        var result = QuotaPresentation.Create(new(state, snapshot, Now, Now, false, 1, 1, true), "codex", null, Now);
        Assert.Null(result.RemainingPercent); Assert.DoesNotContain("93%", result.PercentText);
    }
    [Theory]
    [InlineData(49, QuotaTone.Green)]
    [InlineData(50, QuotaTone.Amber)]
    [InlineData(80, QuotaTone.Amber)]
    [InlineData(81, QuotaTone.Red)]
    public void ThresholdsKeepTextAndFillConsistent(double used, QuotaTone tone)
    {
        var snapshot = new UsageSnapshot(Now, "E", "U", true, [new("codex", "Codex", [new("primary", used, 10080, Now.AddDays(1))])]);
        var result = QuotaPresentation.Create(new(UsageState.Current, snapshot, Now, Now, false, 1, 1, true), null, null, Now);
        Assert.Equal(tone, result.Tone); Assert.Contains("周额度", result.Title);
    }
}

