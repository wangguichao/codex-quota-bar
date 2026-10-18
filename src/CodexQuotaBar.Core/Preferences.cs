using System.Text;
using System.Text.Json;

namespace CodexQuotaBar.Core;

public enum BarTheme { System, Dark, Light }

public sealed record DisplayPreferences(int Width = 540, int OffsetY = 44, int RefreshSeconds = 60,
    BarTheme Theme = BarTheme.System, bool AlertsEnabled = true, string? GroupId = null, string? Slot = null, int OffsetX = 0, bool PositionLocked = false)
{
    public DisplayPreferences Normalize() => this with
    {
        Width = Math.Clamp(Width, 180, 720),
        OffsetY = Math.Clamp(OffsetY, 0, 100000),
        OffsetX = Math.Clamp(OffsetX, -100000, 100000),
        RefreshSeconds = RefreshSeconds is 30 or 60 or 120 or 300 ? RefreshSeconds : 60,
        Theme = Enum.IsDefined(Theme) ? Theme : BarTheme.System,
        GroupId = GroupId is { Length: > 0 and <= 80 } && GroupId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? GroupId : null,
        Slot = Slot is "primary" or "secondary" ? Slot : null
    };
}

public sealed record UserSettings(DisplayPreferences Preferences, IReadOnlyList<string> AlertKeys);
public sealed record SettingsLoadResult(UserSettings Settings, bool RecoveredFromError);

/// <summary>Only display preferences and alert fingerprints are persisted. Credentials never enter this store.</summary>
public sealed class PreferenceStore(string path)
{
    private const int MaximumBytes = 262144;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public SettingsLoadResult Load(DisplayPreferences? defaults = null)
    {
        var fallback = new UserSettings(defaults ?? new(), []);
        try
        {
            if (!File.Exists(path)) return new(fallback, false);
            if (new FileInfo(path).Length > MaximumBytes) return new(fallback, true);
            var parsed = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path));
            if (parsed?.Preferences is null || parsed.AlertKeys is null) return new(fallback, true);
            return new(Normalize(parsed), false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(fallback, true); }
    }
    public bool TrySave(UserSettings settings)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(Normalize(settings), Options);
            if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    private static UserSettings Normalize(UserSettings settings)
        => new(settings.Preferences.Normalize(), settings.AlertKeys.Where(LowQuotaAlerts.IsValidKey).Distinct(StringComparer.Ordinal).ToArray());
}



