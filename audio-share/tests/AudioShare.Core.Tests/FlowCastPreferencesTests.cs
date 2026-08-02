using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class FlowCastPreferencesTests
{
    [Fact]
    public void CompleteQuickStart_PreservesExistingPreferences()
    {
        var preferences = FlowCastPreferences.Empty
            .Exclude("cloudmusic.exe") with { ReduceMotion = true };

        var updated = preferences.CompleteQuickStart();

        Assert.True(updated.QuickStartCompleted);
        Assert.True(updated.ReduceMotion);
        Assert.True(updated.IsExcluded("cloudmusic"));
    }

    [Fact]
    public void Empty_DisablesReduceMotionByDefault()
    {
        Assert.False(FlowCastPreferences.Empty.ReduceMotion);
    }

    [Fact]
    public void Empty_EnablesDisconnectNotificationsByDefault()
    {
        Assert.True(FlowCastPreferences.Empty.DisconnectNotificationsEnabled);
    }

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

    [Fact]
    public void ChangingReduceMotionKeepsExcludedPrograms()
    {
        var preferences = FlowCastPreferences.Empty.Exclude("wallpaper64") with { ReduceMotion = true };

        Assert.True(preferences.ReduceMotion);
        Assert.True(preferences.IsExcluded("wallpaper64.exe"));
    }
}
