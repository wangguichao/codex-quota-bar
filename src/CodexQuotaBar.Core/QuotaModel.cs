using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexQuotaBar.Core;

public sealed record QuotaWindow(string Slot, double? UsedPercent, int? DurationMinutes, DateTimeOffset? ResetsAtUtc)
{
    public double? RemainingPercent => UsedPercent is >= 0 and <= 100 && double.IsFinite(UsedPercent.Value) ? 100 - UsedPercent.Value : null;
    public string PercentText => RemainingPercent is not { } value ? "暂无数据" : value is > 0 and < 1 ? "小于1%" : $"{Math.Floor(value).ToString(CultureInfo.InvariantCulture)}%";
    public string PeriodLabel => DurationMinutes switch
    {
        10080 => "周额度",
        > 0 when DurationMinutes % 1440 == 0 => $"{DurationMinutes / 1440}天额度",
        > 0 when DurationMinutes % 60 == 0 => $"{DurationMinutes / 60}小时额度",
        > 0 => $"{DurationMinutes}分钟额度",
        _ => "周期额度"
    };
    public string ResetText(DateTimeOffset now)
    {
        if (ResetsAtUtc is not { } reset) return "重置时间未知";
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return "等待重置确认";
        return remaining.TotalDays >= 1 ? $"{(int)remaining.TotalDays}天{remaining.Hours}小时后重置"
            : remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}小时{remaining.Minutes}分后重置"
            : $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}分钟后重置";
    }
}
public sealed record QuotaGroup(string Id, string Name, IReadOnlyList<QuotaWindow> Windows);
public sealed record UsageSnapshot(DateTimeOffset AtUtc, string? AccountFingerprint, string? UsageAccountFingerprint,
    bool IdentityConsistent, IReadOnlyList<QuotaGroup> Groups)
{
    public bool SourceProcessExited { get; init; }
}
public sealed record IdentityBinding(string AccountFingerprint, string UsageAccountFingerprint, string Space)
{
    public bool Matches(UsageSnapshot snapshot) => Space == "personal" && snapshot.IdentityConsistent
        && !string.IsNullOrEmpty(AccountFingerprint) && !string.IsNullOrEmpty(UsageAccountFingerprint)
        && string.Equals(AccountFingerprint, snapshot.AccountFingerprint, StringComparison.Ordinal)
        && string.Equals(UsageAccountFingerprint, snapshot.UsageAccountFingerprint, StringComparison.Ordinal);
    public static string? Fingerprint(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())))[..16];
}
public static class QuotaSelection
{
    public static QuotaWindow? Select(UsageSnapshot snapshot, string groupId, string? slot) =>
        snapshot.Groups.FirstOrDefault(g => g.Id == groupId)?.Windows
            .Where(w => w.RemainingPercent is not null && (slot is null || w.Slot == slot))
            .OrderBy(w => w.RemainingPercent).FirstOrDefault();
}
public static class QuotaParser
{
    public static UsageSnapshot Parse(JsonElement before, JsonElement usage, JsonElement after, DateTimeOffset now)
    {
        var beforeAccount = Property(before, "account"); var afterAccount = Property(after, "account");
        var first = IdentityBinding.Fingerprint(Text(beforeAccount, "email"));
        var last = IdentityBinding.Fingerprint(Text(afterAccount, "email"));
        var consistent = Text(beforeAccount, "type") == "chatgpt" && Text(afterAccount, "type") == "chatgpt" && first is not null && first == last;
        var groups = new List<QuotaGroup>();
        var map = Property(usage, "rateLimitsByLimitId");
        if (map.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in map.EnumerateObject())
                if (SafeId(item.Name) && Group(item.Name, item.Value) is { } group) groups.Add(group);
        }
        else if (Property(usage, "rateLimits") is { ValueKind: JsonValueKind.Object } legacy)
        {
            var id = Text(legacy, "limitId");
            if (Group(id is not null && SafeId(id) ? id : "codex", legacy) is { } group) groups.Add(group);
        }
        return new(now, first, IdentityBinding.Fingerprint(Text(usage, "accountId")), consistent, groups.AsReadOnly());
    }
    private static QuotaGroup? Group(string id, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var windows = new List<QuotaWindow>();
        foreach (var slot in new[] { "primary", "secondary" })
        {
            var raw = Property(value, slot); if (raw.ValueKind != JsonValueKind.Object) continue;
            var used = Property(raw, "usedPercent");
            double? percent = used.ValueKind == JsonValueKind.Number && used.TryGetDouble(out var number) && double.IsFinite(number) && number is >= 0 and <= 100 ? number : null;
            var duration = Property(raw, "windowDurationMins");
            int? minutes = duration.ValueKind == JsonValueKind.Number && duration.TryGetInt32(out var count) && count > 0 ? count : null;
            DateTimeOffset? reset = null; var rawReset = Property(raw, "resetsAt");
            if (rawReset.ValueKind == JsonValueKind.Number && rawReset.TryGetInt64(out var seconds) && seconds is >= -62135596800 and <= 253402300799)
                reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
            windows.Add(new(slot, percent, minutes, reset));
        }
        var name = Text(value, "limitName");
        return new(id, string.IsNullOrWhiteSpace(name) ? id : name[..Math.Min(name.Length, 64)], windows.AsReadOnly());
    }
    private static bool SafeId(string id) => id.Length is > 0 and <= 80 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    internal static JsonElement Property(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : default;
    internal static string? Text(JsonElement value, string key) => Property(value, key) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
}

