using System.Text.Json.Serialization;
namespace CodexQuotaBar.Core;

/// <summary>Email is displayed only during setup and is excluded from diagnostic serialization.</summary>
public sealed record AccountInspection(UsageSnapshot Snapshot, [property: JsonIgnore] string Email);

public static class SetupBinding
{
    public static IdentityBinding Confirm(UsageSnapshot snapshot, string desktopEmail, bool personalSpaceConfirmed)
    {
        if (!personalSpaceConfirmed || !snapshot.IdentityConsistent ||
            string.IsNullOrEmpty(snapshot.AccountFingerprint) || string.IsNullOrEmpty(snapshot.UsageAccountFingerprint) ||
            IdentityBinding.Fingerprint(desktopEmail) != snapshot.AccountFingerprint)
            throw new InvalidOperationException("请核对桌面账号及个人空间后再连接。");
        return new(snapshot.AccountFingerprint, snapshot.UsageAccountFingerprint, "personal");
    }
    public static bool SameIdentity(UsageSnapshot before, UsageSnapshot after)
        => before.IdentityConsistent && after.IdentityConsistent &&
            !string.IsNullOrEmpty(before.AccountFingerprint) && !string.IsNullOrEmpty(before.UsageAccountFingerprint) &&
            before.AccountFingerprint == after.AccountFingerprint && before.UsageAccountFingerprint == after.UsageAccountFingerprint;
}

