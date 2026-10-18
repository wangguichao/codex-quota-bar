using System.Text.Json;
using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class QuotaModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1792210000);
    private static JsonDocument Account(string email = "sample@example.com") => JsonDocument.Parse(JsonSerializer.Serialize(new { account = new { type = "chatgpt", email } }));
    private static UsageSnapshot Parse(string json)
    {
        using var account = Account(); using var usage = JsonDocument.Parse(json);
        return QuotaParser.Parse(account.RootElement, usage.RootElement, account.RootElement, Now);
    }
    [Theory]
    [InlineData(0, 100, "100%")]
    [InlineData(7, 93, "93%")]
    [InlineData(99.5, 0.5, "小于1%")]
    [InlineData(100, 0, "0%")]
    [InlineData(20.1, 79.9, "79%")]
    public void ComputesRemainingWithoutRoundingUp(double used, double expected, string text)
    {
        var window = new QuotaWindow("primary", used, 10080, Now.AddDays(1));
        Assert.Equal(expected, window.RemainingPercent!.Value, 6);
        Assert.Equal(text, window.PercentText);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidPercentIsUnavailable(double used)
        => Assert.Null(new QuotaWindow("primary", used, null, null).RemainingPercent);
    [Fact]
    public void MultiBucketWinsOverLegacyAndSelectionNeverMixesPools()
    {
        var snapshot = Parse("""{"accountId":"a","rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":32,"windowDurationMins":300},"secondary":{"usedPercent":65,"windowDurationMins":10080}},"other":{"primary":{"usedPercent":97}}}}""");
        Assert.Equal(2, snapshot.Groups.Count);
        Assert.Equal(35, QuotaSelection.Select(snapshot, "codex", null)!.RemainingPercent);
        Assert.Equal(68, QuotaSelection.Select(snapshot, "codex", "primary")!.RemainingPercent);
        Assert.Null(QuotaSelection.Select(snapshot, "missing", null));
        Assert.Null(QuotaSelection.Select(snapshot, "codex", "missing"));
    }
    [Fact]
    public void MissingFieldsStayMissingAndEmptyMapDoesNotResurrectLegacy()
    {
        var snapshot = Parse("""{"rateLimits":{"primary":{}}}""");
        Assert.Null(snapshot.Groups[0].Windows[0].RemainingPercent);
        Assert.Null(snapshot.Groups[0].Windows[0].ResetsAtUtc);
        Assert.Null(QuotaSelection.Select(snapshot, "codex", null));
        Assert.Empty(Parse("""{"rateLimitsByLimitId":{},"rateLimits":{"primary":{"usedPercent":0}}}""").Groups);
    }
    [Fact]
    public void UnknownTimeAndMalformedNumbersDoNotBecomeAValidQuota()
    {
        var snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":"bad","windowDurationMins":-1,"resetsAt":9223372036854775807}}}""");
        var window = snapshot.Groups[0].Windows[0];
        Assert.Null(window.RemainingPercent); Assert.Null(window.DurationMinutes); Assert.Null(window.ResetsAtUtc);
    }
    [Fact]
    public void AccountChangeDuringReadIsNotConsistent()
    {
        using var before = Account(); using var after = Account("other@example.com"); using var usage = JsonDocument.Parse("{}");
        Assert.False(QuotaParser.Parse(before.RootElement, usage.RootElement, after.RootElement, Now).IdentityConsistent);
    }
    [Theory]
    [InlineData(10080, "周额度")]
    [InlineData(300, "5小时额度")]
    [InlineData(15, "15分钟额度")]
    [InlineData(2880, "2天额度")]
    [InlineData(null, "周期额度")]
    public void PeriodLabelsFollowActualDuration(int? minutes, string expected)
        => Assert.Equal(expected, new QuotaWindow("primary", 10, minutes, null).PeriodLabel);
    [Fact]
    public void ReachingResetNeverInventsOneHundredPercent()
    {
        var window = new QuotaWindow("primary", 80, 60, Now.AddSeconds(-1));
        Assert.Equal("20%", window.PercentText);
        Assert.Equal("等待重置确认", window.ResetText(Now));
    }
}

