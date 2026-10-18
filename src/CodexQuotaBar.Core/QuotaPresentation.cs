namespace CodexQuotaBar.Core;

public enum QuotaTone { Neutral, Green, Amber, Red }
public sealed record QuotaPresentation(string Title, string PercentText, double? RemainingPercent, string ResetText, string Status, QuotaTone Tone)
{
    public static QuotaPresentation Create(UsageView view, string? groupId, string? slot, DateTimeOffset now)
    {
        var message = view.State switch
        {
            UsageState.Connecting => "正在连接",
            UsageState.Stale => "数据已过期",
            UsageState.ReadError => "读取失败",
            UsageState.Timeout => "查询超时",
            UsageState.NeedsLogin => "需要在 Codex 登录",
            UsageState.IdentityUnverified => "账号待核对",
            UsageState.Incompatible => "数据源不兼容",
            UsageState.NoData => "暂无额度数据",
            UsageState.Stopped => "已停止",
            _ => "已核对个人账号"
        };
        if (view.State != UsageState.Current || view.Snapshot is null)
            return new("Codex", message, null, "", message, QuotaTone.Neutral);
        var group = view.Snapshot.Groups.FirstOrDefault(g => g.Id == groupId) ?? (groupId is null ? view.Snapshot.Groups.FirstOrDefault(g => g.Id == "codex") ?? view.Snapshot.Groups.FirstOrDefault() : null);
        var window = group is null ? null : QuotaSelection.Select(view.Snapshot, group.Id, slot);
        if (window?.RemainingPercent is not { } remaining) return new("Codex", "暂无额度数据", null, "", "暂无额度数据", QuotaTone.Neutral);
        var name = group!.Id == "codex" ? "Codex" : group.Name;
        return new($"{name} · {window.PeriodLabel}", $"剩余 {window.PercentText}", remaining, window.ResetText(now),
            message, remaining > 50 ? QuotaTone.Green : remaining >= 20 ? QuotaTone.Amber : QuotaTone.Red);
    }
}

