namespace CodexQuotaBar.Core;

public enum PreviewStatus { Stopped, WaitingForTarget, WaitingForForeground, Rendering, Showing }

public static class PreviewStatusPolicy
{
    public static PreviewStatus Evaluate(bool running, bool targetFound, bool nativeVisible, bool rendered)
    {
        if (!running) return PreviewStatus.Stopped;
        if (!targetFound) return PreviewStatus.WaitingForTarget;
        if (!nativeVisible) return PreviewStatus.WaitingForForeground;
        return rendered ? PreviewStatus.Showing : PreviewStatus.Rendering;
    }
}

