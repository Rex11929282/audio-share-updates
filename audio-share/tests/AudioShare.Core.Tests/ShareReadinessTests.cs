using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareReadinessTests
{
    [Fact]
    public void OrdersFavoriteProgramsBeforeOtherPrograms()
    {
        var favorites = new FavoritePrograms(["cloudmusic"]);
        var chrome = new AudioSession(1, 1, "chrome", "Chrome", true);
        var cloudMusic = new AudioSession(2, 2, "cloudmusic", "NetEase Cloud Music", true);

        var ordered = favorites.Order([chrome, cloudMusic]);

        Assert.Equal([cloudMusic, chrome], ordered);
    }

    [Fact]
    public void ProvidesTheApprovedTimerPresets()
    {
        Assert.Equal([5, 15, 30, 60], ShareTimerPresets.Minutes);
    }

    [Fact]
    public void RequiresTwoClearPeakSamplesBeforeAProgramIsConsideredAudible()
    {
        Assert.False(AudioActivityPolicy.HasConfirmedOutput(0.03f, 0.01f));
        Assert.False(AudioActivityPolicy.HasConfirmedOutput(0.01f, 0.03f));
        Assert.True(AudioActivityPolicy.HasConfirmedOutput(0.03f, 0.04f));
        Assert.True(AudioActivityPolicy.HasConfirmedOutput(0.01f, 0.03f, 0.04f));
        Assert.False(AudioActivityPolicy.HasConfirmedOutput(0.03f, 0.01f, 0.01f));
    }

    [Fact]
    public void AllowsSharingWhenAnAppIsSelectedBeforeItProducesAudio()
    {
        Assert.True(ShareStartPolicy.CanStart(hasSelection: true, routeAvailable: true, isRoutingOperation: false));
        Assert.False(ShareStartPolicy.CanStart(hasSelection: false, routeAvailable: true, isRoutingOperation: false));
    }
}
