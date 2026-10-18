using System.Text.Json;
using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class SetupBindingTests
{
    private static UsageSnapshot Snapshot(string email = "friend@example.com", string quota = "friend-quota", bool consistent = true)
        => new(DateTimeOffset.UtcNow, IdentityBinding.Fingerprint(email), IdentityBinding.Fingerprint(quota), consistent,
            [new("codex", "Codex", [new("primary", 20, 10080, DateTimeOffset.UtcNow.AddDays(1))])]);
    [Fact]
    public void RequiresDesktopAccountAndPersonalSpaceConfirmation()
    {
        var snapshot = Snapshot();
        Assert.Throws<InvalidOperationException>(() => SetupBinding.Confirm(snapshot, "friend@example.com", false));
        Assert.Throws<InvalidOperationException>(() => SetupBinding.Confirm(snapshot, "someone@example.com", true));
        var binding = SetupBinding.Confirm(snapshot, " FRIEND@example.com ", true);
        Assert.True(binding.Matches(snapshot)); Assert.Equal("personal", binding.Space);
    }
    [Fact]
    public void CannotBindAccountThatChangedDuringReadOrMissingIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => SetupBinding.Confirm(Snapshot(consistent: false), "friend@example.com", true));
        Assert.Throws<InvalidOperationException>(() => SetupBinding.Confirm(Snapshot() with { UsageAccountFingerprint = null }, "friend@example.com", true));
    }
    [Fact]
    public void RecheckRequiresSameSourceIdentity()
    {
        var original = Snapshot();
        Assert.True(SetupBinding.SameIdentity(original, Snapshot()));
        Assert.False(SetupBinding.SameIdentity(original, Snapshot(quota: "other-workspace")));
        Assert.False(SetupBinding.SameIdentity(original, Snapshot(email: "someone@example.com")));
        Assert.False(SetupBinding.SameIdentity(original, Snapshot(consistent: false)));
    }
    [Fact]
    public async Task InspectionProvidesEmailOnlyForOnScreenConfirmation()
    {
        var sdk = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? throw new InvalidOperationException("DOTNET_ROOT required");
        var provider = new CodexUsageProvider(Path.Combine(sdk, "dotnet.exe"),
            [Path.Combine(AppContext.BaseDirectory, "TestServer", "CodexQuotaBar.FakeServer.dll"), "ok"]);
        var result = await provider.InspectAsync(CancellationToken.None);
        Assert.Equal("sample@example.com", result.Email);
        Assert.True(result.Snapshot.SourceProcessExited);
        Assert.DoesNotContain("sample@example.com", JsonSerializer.Serialize(result));
    }
}

