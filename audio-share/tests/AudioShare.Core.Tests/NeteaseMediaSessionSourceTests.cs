using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class NeteaseMediaSessionSourceTests
{
    [Theory]
    [InlineData("cloudmusic.exe", true)]
    [InlineData("NetEase.CloudMusic", true)]
    [InlineData("CLOUDMUSIC", true)]
    [InlineData("chrome.exe", false)]
    [InlineData("", false)]
    public void IsNeteaseSource_MatchesOnlyNetEase(string source, bool expected)
    {
        Assert.Equal(expected, NeteaseMediaSessionSource.IsNeteaseSource(source));
    }

    [Fact]
    public void PositionAt_AdvancesOnlyWhilePlaying()
    {
        var captured = DateTimeOffset.Parse("2026-08-02T08:00:00Z");
        var playing = new MediaPlaybackSnapshot("歌", ["歌手"], 180000, 5000, captured, true);
        var paused = playing with { IsPlaying = false };

        Assert.Equal(15000, playing.PositionAt(captured.AddSeconds(10)));
        Assert.Equal(5000, paused.PositionAt(captured.AddSeconds(10)));
    }

    [Fact]
    public void PositionAt_ClampsToTheTrackBounds()
    {
        var captured = DateTimeOffset.Parse("2026-08-02T08:00:00Z");
        var snapshot = new MediaPlaybackSnapshot("歌", ["歌手"], 10000, 5000, captured, true);

        Assert.Equal(0, snapshot.PositionAt(captured.AddSeconds(-10)));
        Assert.Equal(10000, snapshot.PositionAt(captured.AddSeconds(20)));
    }

    [Fact]
    public void ParseArtists_SplitsAndTrimsCommonNetEaseSeparators()
    {
        Assert.Equal(
            ["Daoko", "米津玄師", "第三位", "第四位", "第五位"],
            NeteaseMediaSessionSource.ParseArtists(" Daoko / 米津玄師、第三位；第四位, 第五位 "));
    }
}
