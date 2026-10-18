namespace CodexQuotaBar.Core;

public enum UsageState { Connecting, Current, Stale, ReadError, Timeout, NeedsLogin, IdentityUnverified, Incompatible, NoData, Stopped }
public sealed class UsageFailureException(UsageState state) : Exception("Usage source unavailable.")
{ public UsageState State { get; } = state; }
public interface IUsageProvider { Task<UsageSnapshot> ReadAsync(CancellationToken token); }
public sealed record UsageView(UsageState State, UsageSnapshot? Snapshot, DateTimeOffset? LastSuccessUtc,
    DateTimeOffset NextRefreshUtc, bool Refreshing, int Attempts, int SuccessfulReads, bool LastSourceExited);

public sealed class UsageMonitor : IAsyncDisposable
{
    private readonly IUsageProvider provider;
    private readonly IdentityBinding binding;
    private readonly TimeProvider clock;
    private int refreshSeconds;
    private readonly TimeSpan timeout;
    private readonly object stateLock = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopped = new();
    private UsageState state = UsageState.Connecting;
    private UsageSnapshot? snapshot;
    private DateTimeOffset? lastSuccess;
    private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;
    private bool refreshing, disposed;
    private int failures, attempts, successes;
    private bool sourceExited;

    public UsageMonitor(IUsageProvider provider, IdentityBinding binding, TimeProvider? clock = null, int refreshSeconds = 60, TimeSpan? timeout = null)
    {
        this.provider = provider; this.binding = binding; this.clock = clock ?? TimeProvider.System;
        if (refreshSeconds is not (30 or 60 or 120 or 300)) throw new ArgumentOutOfRangeException(nameof(refreshSeconds));
        this.refreshSeconds = refreshSeconds; this.timeout = timeout ?? TimeSpan.FromSeconds(10);
    }
    public void SetRefreshSeconds(int seconds)
    {
        if (seconds is not (30 or 60 or 120 or 300)) throw new ArgumentOutOfRangeException(nameof(seconds));
        lock (stateLock)
        {
            if (disposed) return;
            refreshSeconds = seconds;
            nextRefresh = NextSuccessfulRead(clock.GetUtcNow(), snapshot);
        }
    }
    private DateTimeOffset NextSuccessfulRead(DateTimeOffset now, UsageSnapshot? current)
    {
        var next = now.AddSeconds(refreshSeconds);
        if (current is null) return next;
        var reset = current.Groups.SelectMany(g => g.Windows).Where(w => w.ResetsAtUtc > now && w.ResetsAtUtc < next)
            .Select(w => w.ResetsAtUtc!.Value.AddSeconds(1)).DefaultIfEmpty(next).Min();
        return reset < next ? reset : next;
    }
    public UsageView View
    {
        get
        {
            lock (stateLock)
            {
                var effective = state == UsageState.Current && snapshot is not null && clock.GetUtcNow() - snapshot.AtUtc > TimeSpan.FromSeconds(180) ? UsageState.Stale : state;
                return new(effective, effective == UsageState.Current ? snapshot : null, lastSuccess, nextRefresh, refreshing, attempts, successes, sourceExited);
            }
        }
    }
    public async Task RefreshAsync()
    {
        if (!await gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            lock (stateLock) { if (disposed) return; refreshing = true; attempts++; }
            using var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
            timeoutToken.CancelAfter(timeout);
            var result = await provider.ReadAsync(timeoutToken.Token).ConfigureAwait(false);
            lock (stateLock)
            {
                if (disposed) return;
                sourceExited = result.SourceProcessExited;
                if (!binding.Matches(result))
                { state = UsageState.IdentityUnverified; snapshot = null; failures++; nextRefresh = clock.GetUtcNow().AddMinutes(2); return; }
                snapshot = result; lastSuccess = result.AtUtc; failures = 0; successes++;
                state = result.Groups.Any(g => g.Windows.Any(w => w.RemainingPercent is not null)) ? UsageState.Current : UsageState.NoData;
                nextRefresh = NextSuccessfulRead(clock.GetUtcNow(), result);
            }
        }
        catch (Exception error) when (error is UsageFailureException or OperationCanceledException or IOException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            lock (stateLock)
            {
                if (disposed) return;
                state = error is UsageFailureException failure ? failure.State : error is OperationCanceledException ? UsageState.Timeout : UsageState.ReadError;
                if (state is UsageState.NeedsLogin or UsageState.IdentityUnverified) snapshot = null;
                failures++;
                nextRefresh = clock.GetUtcNow().AddMinutes(failures == 1 ? 2 : failures == 2 ? 5 : 10);
            }
        }
        finally { lock (stateLock) refreshing = false; gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lock (stateLock) { if (disposed) return; disposed = true; state = UsageState.Stopped; snapshot = null; }
        stopped.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        gate.Release();
        stopped.Dispose();
    }
}







