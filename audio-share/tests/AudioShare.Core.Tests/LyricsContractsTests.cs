using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsContractsTests
{
    [Fact]
    public void LyricLine_PreservesReceivedTimingAndText()
    {
        var line = new LyricLine("真實歌詞", 1250, 4800);

        Assert.Equal("真實歌詞", line.Text);
        Assert.Equal(1250, line.StartTimeMilliseconds);
        Assert.Equal(4800, line.EndTimeMilliseconds);
    }

    [Fact]
    public void IslandMode_ContainsOnlyTheApprovedModes()
    {
        Assert.Equal(
            ["Idle", "Resolving", "Playing", "Paused", "NoLyrics", "Unavailable"],
            Enum.GetNames<IslandMode>());
    }

    [Fact]
    public void IslandSnapshot_PreservesRealLyricAndSource()
    {
        var line = new LyricLine("真實歌詞", 1250, 4800);
        var snapshot = new IslandSnapshot(IslandMode.Playing, "真實歌詞", line, LyricSource.LocalNetEase);

        Assert.Same(line, snapshot.LyricLine);
        Assert.Equal(LyricSource.LocalNetEase, snapshot.Source);
    }
}
