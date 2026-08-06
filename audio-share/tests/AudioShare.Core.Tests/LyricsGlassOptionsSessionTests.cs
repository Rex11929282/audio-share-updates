namespace AudioShare.Core.Tests;

public sealed class LyricsGlassOptionsSessionTests
{
    [Fact]
    public void Complete_ReturnsCurrentSettingsWithoutMutatingTheInitialSettings()
    {
        var initial = new global::AudioShare.Lyrics.LyricsGlassSettings(0.2, 3, 0.4, 0.5, true);
        var current = initial with { BlurRadiusDp = 12, ChromaticAberration = false };
        var session = new global::AudioShare.Lyrics.LyricsGlassOptionsSession(initial)
        {
            Current = current
        };

        var completed = session.Complete();

        Assert.Equal(current, completed);
        Assert.Equal(new global::AudioShare.Lyrics.LyricsGlassSettings(0.2, 3, 0.4, 0.5, true), initial);
    }

    [Fact]
    public void Cancel_RestoresTheInitialSettings()
    {
        var initial = new global::AudioShare.Lyrics.LyricsGlassSettings(0.2, 3, 0.4, 0.5, true);
        var session = new global::AudioShare.Lyrics.LyricsGlassOptionsSession(initial)
        {
            Current = global::AudioShare.Lyrics.LyricsGlassSettings.Defaults
        };

        session.Cancel();

        Assert.Equal(initial, session.Current);
    }

    [Fact]
    public void Reset_RestoresDefaultSettings()
    {
        var initial = new global::AudioShare.Lyrics.LyricsGlassSettings(0.2, 3, 0.4, 0.5, true);
        var session = new global::AudioShare.Lyrics.LyricsGlassOptionsSession(initial);

        session.Reset();

        Assert.Equal(global::AudioShare.Lyrics.LyricsGlassSettings.Defaults, session.Current);
    }
}
