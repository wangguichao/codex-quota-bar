namespace CodexQuotaBar.Core;

public static class WindowIdentityPolicy
{
    public static bool IsCodexSurface(string package, string windowClass, int width, int height)
        => package.StartsWith("OpenAI.Codex_", StringComparison.Ordinal)
            && windowClass.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)
            && width >= 180 && height >= 100;
}

