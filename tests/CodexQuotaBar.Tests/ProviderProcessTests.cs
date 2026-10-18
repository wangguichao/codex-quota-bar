using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class ProviderProcessTests
{
    private static CodexUsageProvider Provider(string mode)
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? throw new InvalidOperationException("DOTNET_ROOT required for process tests");
        return new(Path.Combine(root, "dotnet.exe"), [Path.Combine(AppContext.BaseDirectory, "TestServer", "CodexQuotaBar.FakeServer.dll"), mode]);
    }
    [Fact]
    public async Task RealStdioHandshakeReturnsSnapshotAndClosesOwnedProcess()
    {
        var result = await Provider("ok").ReadAsync(CancellationToken.None);
        Assert.True(result.IdentityConsistent); Assert.True(result.SourceProcessExited);
        Assert.Equal(93, result.Groups[0].Windows[0].RemainingPercent);
    }
    [Theory]
    [InlineData("login", UsageState.NeedsLogin)]
    [InlineData("apikey", UsageState.Incompatible)]
    [InlineData("rpc", UsageState.ReadError)]
    [InlineData("incompatible", UsageState.Incompatible)]
    [InlineData("malformed", UsageState.Incompatible)]
    [InlineData("invalid-json", UsageState.Incompatible)]
    [InlineData("missing-result", UsageState.Incompatible)]
    public async Task FailureTypesDoNotExposeRawServerErrors(string mode, UsageState state)
    {
        var error = await Assert.ThrowsAsync<UsageFailureException>(() => Provider(mode).ReadAsync(CancellationToken.None));
        Assert.Equal(state, error.State); Assert.DoesNotContain("PRIVATE_ERROR_TOKEN", error.ToString());
    }
    [Fact]
    public async Task AccountSwitchInsideProcessIsDetected()
        => Assert.False((await Provider("switch").ReadAsync(CancellationToken.None)).IdentityConsistent);
    [Fact]
    public async Task CancellationStopsHungSource()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider("slow").ReadAsync(cancellation.Token));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5));
    }
    [Fact]
    public async Task MissingExecutableIsIncompatible()
    {
        var error = await Assert.ThrowsAsync<UsageFailureException>(() => new CodexUsageProvider("missing.exe").ReadAsync(CancellationToken.None));
        Assert.Equal(UsageState.Incompatible, error.State);
    }
}



