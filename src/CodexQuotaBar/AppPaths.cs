using System.IO;
namespace CodexQuotaBar;
internal static class AppPaths
{
    internal static bool IsShareEdition => File.Exists(Path.Combine(AppContext.BaseDirectory, "share-mode.json"));
    internal static string DataDirectory => IsShareEdition
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaBar")
        : AppContext.BaseDirectory;
    internal static string Configuration => Path.Combine(DataDirectory, "live.local.json");
    internal static string Report => Path.Combine(DataDirectory, "live-state.json");
    internal static string BundledCodex => Path.Combine(AppContext.BaseDirectory, "runtime", "codex.exe");
}

