using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class LowQuotaAlertsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1792210000);
    private static UsageView View(double used, DateTimeOffset? reset = null, UsageState state = UsageState.Current, string account = "QUOTA")
        => new(state, new(Now, "EMAIL", account, true, [new("codex", "Codex", [new("primary", used, 10080, reset ?? Now.AddDays(1))])]), Now, Now.AddMinutes(1), false, 1, 1, true);
    [Fact]
    public void ThresholdsFireOncePerPeriodAndPersistAcrossRestart()
    {
        var alerts = new LowQuotaAlerts([]);
        Assert.Empty(alerts.Evaluate(View(79), true));
        Assert.Equal(20, Assert.Single(alerts.Evaluate(View(80), true)).Threshold);
        Assert.Empty(alerts.Evaluate(View(82), true));
        Assert.Equal(10, Assert.Single(alerts.Evaluate(View(92), true)).Threshold);
        Assert.Equal(5, Assert.Single(alerts.Evaluate(View(98), true)).Threshold);
        var restarted = new LowQuotaAlerts(alerts.Keys);
        Assert.Empty(restarted.Evaluate(View(99), true));
        Assert.Equal(5, Assert.Single(restarted.Evaluate(View(99, Now.AddDays(8)), true)).Threshold);
    }
    [Fact]
    public void LargeDropMakesOneAlertAndMarksAllCrossedThresholds()
    {
        var alerts = new LowQuotaAlerts([]);
        Assert.Equal(5, Assert.Single(alerts.Evaluate(View(97), true)).Threshold);
        Assert.Empty(alerts.Evaluate(View(85), true));
        Assert.Single(alerts.Evaluate(View(97, account: "ANOTHER"), true));
    }
    [Theory]
    [InlineData(UsageState.Stale)]
    [InlineData(UsageState.ReadError)]
    [InlineData(UsageState.IdentityUnverified)]
    public void UnverifiedOrOldDataNeverNotifies(UsageState state)
        => Assert.Empty(new LowQuotaAlerts([]).Evaluate(View(95, state: state), true));
    [Fact]
    public void DisabledOrUnknownPeriodDoesNotConsumeThreshold()
    {
        var alerts = new LowQuotaAlerts([]);
        Assert.Empty(alerts.Evaluate(View(95), false));
        var unknown = View(95) with { Snapshot = new(Now, "EMAIL", "QUOTA", true, [new("codex", "Codex", [new("primary", 95, 10080, null)])]) };
        Assert.Empty(alerts.Evaluate(unknown, true));
        Assert.Single(alerts.Evaluate(View(95), true));
    }
}

