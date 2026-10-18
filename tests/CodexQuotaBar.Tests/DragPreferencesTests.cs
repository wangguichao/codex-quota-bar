using CodexQuotaBar.Core;
using Xunit;
namespace CodexQuotaBar.Tests;

public sealed class DragPreferencesTests
{
    [Fact]
    public void PositionAndLockRoundTripAndLegacySettingsStillLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuotaBar-drag-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new PreferenceStore(path);
            var preferences = new DisplayPreferences(OffsetY: 700, OffsetX: -260, PositionLocked: true);
            Assert.True(store.TrySave(new(preferences, [])));
            Assert.Equal(preferences, store.Load().Settings.Preferences);
            File.WriteAllText(path, "{\"Preferences\":{\"Width\":360,\"OffsetY\":92,\"RefreshSeconds\":120,\"Theme\":2,\"AlertsEnabled\":false},\"AlertKeys\":[]}");
            var legacy = store.Load();
            Assert.False(legacy.RecoveredFromError); Assert.Equal(92, legacy.Settings.Preferences.OffsetY);
            Assert.Equal(0, legacy.Settings.Preferences.OffsetX); Assert.False(legacy.Settings.Preferences.PositionLocked);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void LargeStoredOffsetsHaveSafeBounds()
    {
        var normalized = new DisplayPreferences(OffsetY: int.MaxValue, OffsetX: int.MinValue).Normalize();
        Assert.Equal(100000, normalized.OffsetY); Assert.Equal(-100000, normalized.OffsetX);
    }
}

