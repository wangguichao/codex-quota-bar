using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class UsageMonitorTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1792210000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Provider(Func<CancellationToken, Task<UsageSnapshot>> read) : IUsageProvider
    { public Task<UsageSnapshot> ReadAsync(CancellationToken token) => read(token); }
    private static UsageSnapshot Snapshot(DateTimeOffset now, string usageId = "QUOTA") =>
        new(now, "EMAIL", usageId, true, [new("codex", "Codex", [new("primary", 7, 10080, now.AddDays(1))])]);
    private static readonly IdentityBinding Binding = new("EMAIL", "QUOTA", "personal");
    [Fact]
    public async Task SuccessExpiresAndNeverShowsOldPercentAsCurrent()
    {
        var clock = new Clock(); await using var monitor = new UsageMonitor(new Provider(_ => Task.FromResult(Snapshot(clock.Now))), Binding, clock);
        await monitor.RefreshAsync();
        Assert.Equal(UsageState.Current, monitor.View.State);
        Assert.NotNull(monitor.View.Snapshot);
        clock.Now += TimeSpan.FromSeconds(181);
        Assert.Equal(UsageState.Stale, monitor.View.State);
        Assert.Null(monitor.View.Snapshot);
    }
    [Fact]
    public async Task MismatchClearsPreviouslyValidDisplay()
    {
        var clock = new Clock(); var id = "QUOTA";
        await using var monitor = new UsageMonitor(new Provider(_ => Task.FromResult(Snapshot(clock.Now, id))), Binding, clock);
        await monitor.RefreshAsync(); id = "OTHER"; await monitor.RefreshAsync();
        Assert.Equal(UsageState.IdentityUnverified, monitor.View.State);
        Assert.Null(monitor.View.Snapshot);
    }
    [Fact]
    public async Task FailureBackoffAndRecoveryAreExplicit()
    {
        var clock = new Clock(); var fail = true;
        await using var monitor = new UsageMonitor(new Provider(_ => fail ? Task.FromException<UsageSnapshot>(new UsageFailureException(UsageState.ReadError)) : Task.FromResult(Snapshot(clock.Now))), Binding, clock);
        foreach (var minutes in new[] { 2, 5, 10, 10 })
        {
            await monitor.RefreshAsync();
            Assert.Equal(clock.Now.AddMinutes(minutes), monitor.View.NextRefreshUtc);
            Assert.Null(monitor.View.Snapshot);
        }
        fail = false; await monitor.RefreshAsync();
        Assert.Equal(UsageState.Current, monitor.View.State);
        Assert.Equal(clock.Now.AddSeconds(60), monitor.View.NextRefreshUtc);
    }
    [Fact]
    public async Task OverlappingRefreshesDoNotStartAnotherQueryAndDisposeCancels()
    {
        var calls = 0; var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new UsageMonitor(new Provider(async token => { Interlocked.Increment(ref calls); entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Snapshot(DateTimeOffset.UtcNow); }), Binding);
        var first = monitor.RefreshAsync(); await entered.Task; await monitor.RefreshAsync();
        Assert.Equal(1, calls);
        await monitor.DisposeAsync(); await first;
        Assert.Equal(UsageState.Stopped, monitor.View.State);
        await monitor.RefreshAsync(); Assert.Equal(1, calls);
    }
    [Fact]
    public async Task LoginAndEmptyQuotaHaveDistinctStates()
    {
        var clock = new Clock(); var login = true;
        await using var monitor = new UsageMonitor(new Provider(_ => login ? Task.FromException<UsageSnapshot>(new UsageFailureException(UsageState.NeedsLogin)) : Task.FromResult(Snapshot(clock.Now) with { Groups = [] })), Binding, clock);
        await monitor.RefreshAsync(); Assert.Equal(UsageState.NeedsLogin, monitor.View.State);
        login = false; await monitor.RefreshAsync(); Assert.Equal(UsageState.NoData, monitor.View.State);
    }
    [Fact]
    public async Task ChangingIntervalPreservesUpcomingResetConfirmation()
    {
        var clock = new Clock();
        var snapshot = Snapshot(clock.Now) with { Groups = [new("codex", "Codex", [new("primary", 80, 60, clock.Now.AddSeconds(10))])] };
        await using var monitor = new UsageMonitor(new Provider(_ => Task.FromResult(snapshot)), Binding, clock);
        await monitor.RefreshAsync(); monitor.SetRefreshSeconds(300);
        Assert.Equal(clock.Now.AddSeconds(11), monitor.View.NextRefreshUtc);
    }
    [Fact]
    public async Task ChangingRefreshIntervalReschedulesNextRead()
    {
        var clock = new Clock();
        await using var monitor = new UsageMonitor(new Provider(_ => Task.FromResult(Snapshot(clock.Now))), Binding, clock);
        await monitor.RefreshAsync(); monitor.SetRefreshSeconds(120);
        Assert.Equal(clock.Now.AddSeconds(120), monitor.View.NextRefreshUtc);
        await monitor.RefreshAsync(); Assert.Equal(clock.Now.AddSeconds(120), monitor.View.NextRefreshUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.SetRefreshSeconds(1));
    }
    [Fact]
    public async Task MaximumResetDateUsesNormalRefreshWithoutOverflow()
    {
        var clock = new Clock();
        var snapshot = Snapshot(clock.Now) with { Groups = [new("codex", "Codex", [new("primary", 7, 10080, DateTimeOffset.FromUnixTimeSeconds(253402300799))])] };
        await using var monitor = new UsageMonitor(new Provider(_ => Task.FromResult(snapshot)), Binding, clock);
        await monitor.RefreshAsync();
        Assert.Equal(UsageState.Current, monitor.View.State);
        Assert.Equal(clock.Now.AddSeconds(60), monitor.View.NextRefreshUtc);
    }
    [Fact]
    public async Task UpcomingResetSchedulesAReadAndDoesNotRefillLocally()
    {
        var clock = new Clock();
        var snapshot = Snapshot(clock.Now) with { Groups = [new("codex", "Codex", [new("primary", 80, 60, clock.Now.AddSeconds(10))])] };
        await using var monitor = new UsageMonitor(new Provider(_ => Task.FromResult(snapshot)), Binding, clock);
        await monitor.RefreshAsync();
        Assert.Equal(clock.Now.AddSeconds(11), monitor.View.NextRefreshUtc);
        clock.Now += TimeSpan.FromSeconds(12);
        Assert.Equal(20, QuotaSelection.Select(monitor.View.Snapshot!, "codex", null)!.RemainingPercent);
    }
}




