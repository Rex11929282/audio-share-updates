using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class FlowCastPreferencesTests
{
    [Fact]
    public void NormalizesExtensionsBeforePersistingExclusions()
    {
        var preferences = FlowCastPreferences.Empty.Exclude("Wallpaper64.exe");

        Assert.True(preferences.IsExcluded("wallpaper64"));
    }

    [Fact]
    public void RestoreRemovesOnlyTheRequestedProcess()
    {
        var preferences = FlowCastPreferences.Empty
            .Exclude("chrome")
            .Exclude("cloudmusic")
            .Restore("chrome.exe");

        Assert.False(preferences.IsExcluded("chrome"));
        Assert.True(preferences.IsExcluded("cloudmusic"));
    }
}
