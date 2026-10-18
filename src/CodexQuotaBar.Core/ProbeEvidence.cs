using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexQuotaBar.Core;

public sealed record ProbeEvidence(
    DateTimeOffset CapturedAtUtc,
    string? AuthType,
    string? AccountFingerprint,
    string? UsageAccountFingerprint,
    bool DesktopIdentityVerified,
    Dictionary<string, object?>? RateLimits,
    Dictionary<string, Dictionary<string, object?>>? RateLimitsByLimitId)
{
    public static ProbeEvidence Sanitize(JsonElement accountResult, JsonElement usageResult)
    {
        var account = Property(accountResult, "account");
        var auth = Text(account, "type");
        if (auth is not ("apiKey" or "chatgpt" or "amazonBedrock"))
            auth = null;
        var groups = Property(usageResult, "rateLimitsByLimitId");
        Dictionary<string, Dictionary<string, object?>>? safeGroups = null;
        if (groups.ValueKind == JsonValueKind.Object)
        {
            safeGroups = [];
            foreach (var group in groups.EnumerateObject())
                if (SafeIdentifier(group.Name) && Bucket(group.Value) is { } bucket)
                    safeGroups[group.Name] = bucket;
        }
        return new(DateTimeOffset.UtcNow, auth, Fingerprint(Text(account, "email")),
            Fingerprint(Text(usageResult, "accountId")), false,
            Bucket(Property(usageResult, "rateLimits")), safeGroups);
    }

    private static Dictionary<string, object?>? Bucket(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return null;
        var result = new Dictionary<string, object?>();
        var id = Text(input, "limitId");
        if (id is not null && SafeIdentifier(id))
            result["limitId"] = id;
        foreach (var slot in new[] { "primary", "secondary" })
        {
            var window = Property(input, slot);
            if (window.ValueKind != JsonValueKind.Object)
                continue;
            var safe = new Dictionary<string, object?>();
            foreach (var name in new[] { "usedPercent", "windowDurationMins", "resetsAt" })
            {
                var value = Property(window, name);
                safe[name] = value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                    ? number : null;
            }
            result[slot] = safe;
        }
        return result;
    }

    private static JsonElement Property(JsonElement value, string key)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) ? child : default;
    private static string? Text(JsonElement value, string key)
    {
        var property = Property(value, key);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
    private static string? Fingerprint(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant())))[..16];
    private static bool SafeIdentifier(string value) => value.Length is > 0 and <= 80 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}

