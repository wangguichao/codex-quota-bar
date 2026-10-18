using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class PreferencesTests
{
    [Fact]
    public void PreferencesRoundTripAndRecoverFromDamagedFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "QuotaBar-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var store = new PreferenceStore(path);
            Assert.False(store.Load().RecoveredFromError);
            var preferences = new DisplayPreferences(360, 92, 120, BarTheme.Light, false, "codex", "secondary");
            Assert.True(store.TrySave(new(preferences, ["0123456789ABCDEF:20"])));
            var loaded = store.Load();
            Assert.False(loaded.RecoveredFromError); Assert.Equal(preferences, loaded.Settings.Preferences);
            Assert.Single(loaded.Settings.AlertKeys);
            File.WriteAllText(path, "{broken");
            var damaged = store.Load();
            Assert.True(damaged.RecoveredFromError); Assert.Equal(new DisplayPreferences(), damaged.Settings.Preferences);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void InvalidPreferencesAreClampedAndWriteFailureIsExplicit()
    {
        var normalized = new DisplayPreferences(999, -50, 1, (BarTheme)99, true, "", "invalid").Normalize();
        Assert.Equal(720, normalized.Width); Assert.Equal(0, normalized.OffsetY);
        Assert.Equal(60, normalized.RefreshSeconds); Assert.Equal(BarTheme.System, normalized.Theme);
        Assert.Null(normalized.GroupId); Assert.Null(normalized.Slot);
        var directory = Path.Combine(Path.GetTempPath(), "QuotaBar-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { Assert.False(new PreferenceStore(directory).TrySave(new(new(), []))); }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void OversizedSettingsRecoverWithoutLoadingPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuotaBar-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, new string('x', 262145));
            Assert.True(new PreferenceStore(path).Load().RecoveredFromError);
        }
        finally { File.Delete(path); }
    }
}

