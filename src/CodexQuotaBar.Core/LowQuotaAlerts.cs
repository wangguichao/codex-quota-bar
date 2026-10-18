namespace CodexQuotaBar.Core;

public sealed record QuotaAlert(string GroupId, string Slot, int Threshold, double RemainingPercent, string PeriodLabel, string Message);

public sealed class LowQuotaAlerts(IEnumerable<string> existingKeys)
{
    private static readonly int[] Thresholds = [20, 10, 5];
    private readonly HashSet<string> fired = new(existingKeys.Where(IsValidKey), StringComparer.Ordinal);
    public IReadOnlyList<string> Keys => fired.Order(StringComparer.Ordinal).ToArray();

    public IReadOnlyList<QuotaAlert> Evaluate(UsageView view, bool enabled)
    {
        if (!enabled || view.State != UsageState.Current || view.Snapshot is not { } snapshot) return [];
        var alerts = new List<QuotaAlert>();
        foreach (var group in snapshot.Groups)
            foreach (var window in group.Windows)
            {
                if (window.RemainingPercent is not { } remaining || window.ResetsAtUtc is not { } reset) continue;
                var period = IdentityBinding.Fingerprint(snapshot.UsageAccountFingerprint + ":" + group.Id + ":" + window.Slot + ":" + reset.ToUnixTimeSeconds());
                var crossed = Thresholds.Where(t => remaining <= t && !fired.Contains(period + ":" + t)).ToArray();
                if (crossed.Length == 0) continue;
                foreach (var threshold in crossed) fired.Add(period + ":" + threshold);
                var lowest = crossed.Min();
                alerts.Add(new(group.Id, window.Slot, lowest, remaining, window.PeriodLabel,
                    (group.Id == "codex" ? "Codex" : group.Name) + " · " + window.PeriodLabel + " 剩余 " + window.PercentText));
            }
        return alerts.AsReadOnly();
    }
    internal static bool IsValidKey(string? value)
    {
        if (value is null) return false;
        var parts = value.Split(':');
        return parts.Length == 2 && parts[0].Length == 16 && parts[0].All(char.IsAsciiHexDigit) && parts[1] is "20" or "10" or "5";
    }
}

