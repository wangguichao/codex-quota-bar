using System.IO;
using System.Text.Json;
using CodexQuotaBar.Core;
namespace CodexQuotaBar;

internal sealed record LiveConfiguration(string CodexExecutable, IdentityBinding Identity, int RefreshSeconds = 60)
{
    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this, Program.JsonOptions)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static LiveConfiguration? Load(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 16384) return null;
            var value = JsonSerializer.Deserialize<LiveConfiguration>(File.ReadAllText(path));
            static bool Fingerprint(string? value) => value?.Length == 16 && value.All(char.IsAsciiHexDigit);
            return value is not null && Path.IsPathFullyQualified(value.CodexExecutable) && File.Exists(value.CodexExecutable)
                && value.Identity is not null && value.Identity.Space == "personal"
                && Fingerprint(value.Identity.AccountFingerprint) && Fingerprint(value.Identity.UsageAccountFingerprint)
                && value.RefreshSeconds is 30 or 60 or 120 or 300 ? value : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
}


