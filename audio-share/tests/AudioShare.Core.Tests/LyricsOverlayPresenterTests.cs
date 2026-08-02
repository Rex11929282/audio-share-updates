using System.Text.Json;
using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsOverlayPresenterTests
{
    [Fact]
    public void StartsInTheHonestIdleState()
    {
        var presenter = new LyricsOverlayPresenter();

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("idle", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("等待播放", json.RootElement.GetProperty("displayText").GetString());
        Assert.Equal("none", json.RootElement.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
    }

    [Fact]
    public void Show_PausedLyric_SerializesTheIslandSnapshot()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.Show(new IslandSnapshot(
            IslandMode.Paused,
            "只可顯示收到的文字",
            new LyricLine("只可顯示收到的文字", 1000, 2400),
            LyricSource.RemoteRadmin));

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("paused", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("remoteRadmin", json.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            "只可顯示收到的文字",
            json.RootElement.GetProperty("lyricLine").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData(IslandMode.Idle, "等待播放")]
    [InlineData(IslandMode.Resolving, "正在取得歌詞")]
    [InlineData(IslandMode.NoLyrics, "這首歌沒有歌詞")]
    [InlineData(IslandMode.Unavailable, "暫時無法取得歌詞")]
    public void Show_StatusMode_DoesNotExposeALyric(IslandMode mode, string text)
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.Show(new IslandSnapshot(
            mode,
            $"  {text}  ",
            new LyricLine("不可顯示", 0, null),
            LyricSource.LocalNetEase));

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal(text, json.RootElement.GetProperty("displayText").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
    }
}
