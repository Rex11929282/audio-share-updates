using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsSourceCoordinatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-02T08:00:00Z");

    [Fact]
    public void SnapshotAt_SelectsTheLineAtTheCurrentPlaybackPosition()
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetLocal(CreateLocal(
            positionMilliseconds: 2500,
            isPlaying: true,
            lines: [new("第一句", 0, 2000), new("第二句", 2000, null)]));

        var snapshot = coordinator.SnapshotAt(Now);

        Assert.Equal(IslandMode.Playing, snapshot.Mode);
        Assert.Equal("第二句", snapshot.DisplayText);
        Assert.Equal(LyricSource.LocalNetEase, snapshot.Source);
    }

    [Fact]
    public void SnapshotAt_FreezesTheCurrentLineWhilePaused()
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetLocal(CreateLocal(
            positionMilliseconds: 1000,
            isPlaying: false,
            lines: [new("暫停這句", 0, 2000), new("不可前進", 2000, null)]));

        var snapshot = coordinator.SnapshotAt(Now.AddSeconds(30));

        Assert.Equal(IslandMode.Paused, snapshot.Mode);
        Assert.Equal("暫停這句", snapshot.DisplayText);
    }

    [Fact]
    public void SetLocal_ClearsThePreviousTrackWhileTheNextTrackResolves()
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetLocal(CreateLocal(1000, true, [new("上一首", 0, null)]));

        coordinator.SetLocal(CreateLocal(0, true, [], trackId: "next"));

        var snapshot = coordinator.SnapshotAt(Now);
        Assert.Equal(IslandMode.Resolving, snapshot.Mode);
        Assert.Equal("正在取得歌詞", snapshot.DisplayText);
        Assert.Null(snapshot.LyricLine);
    }

    [Theory]
    [InlineData(NeteaseLyricsStatus.NoLyrics, IslandMode.NoLyrics, "這首歌沒有歌詞")]
    [InlineData(NeteaseLyricsStatus.Unavailable, IslandMode.Unavailable, "暫時無法取得歌詞")]
    public void SnapshotAt_MapsLocalLyricFailuresHonestly(
        NeteaseLyricsStatus status,
        IslandMode expectedMode,
        string expectedText)
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetLocal(CreateLocal(0, true, [], lyricsStatus: status));

        var snapshot = coordinator.SnapshotAt(Now);

        Assert.Equal(expectedMode, snapshot.Mode);
        Assert.Equal(expectedText, snapshot.DisplayText);
        Assert.Null(snapshot.LyricLine);
    }

    [Fact]
    public void SnapshotAt_RemoteWinsThenExpiresBackToLocal()
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetLocal(CreateLocal(1000, true, [new("本機", 0, null)]));
        coordinator.SetRemote(new RemoteLyricsPlayback(
            "remote",
            1,
            IslandMode.Playing,
            new LyricLine("遠端", 0, null),
            Now));

        Assert.Equal("遠端", coordinator.SnapshotAt(Now).DisplayText);
        Assert.Equal(LyricSource.RemoteRadmin, coordinator.SnapshotAt(Now).Source);
        Assert.Equal("本機", coordinator.SnapshotAt(Now.AddSeconds(6)).DisplayText);
        Assert.Equal(LyricSource.LocalNetEase, coordinator.SnapshotAt(Now.AddSeconds(6)).Source);
    }

    [Fact]
    public void SetRemote_IgnoresOlderSequencesAndOtherHostsUntilCleared()
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetRemote(CreateRemote("owner-a", 2, "最新"));
        coordinator.SetRemote(CreateRemote("owner-a", 1, "過期"));
        coordinator.SetRemote(CreateRemote("owner-b", 3, "另一台"));

        Assert.Equal("最新", coordinator.SnapshotAt(Now).DisplayText);

        coordinator.ClearRemote();
        coordinator.SetRemote(CreateRemote("owner-b", 1, "另一台"));
        Assert.Equal("另一台", coordinator.SnapshotAt(Now).DisplayText);
    }

    [Fact]
    public void SnapshotAt_DoesNotInventARemoteLyricWhenTheFrameHasNoLine()
    {
        var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        coordinator.SetRemote(new RemoteLyricsPlayback("remote", 1, IslandMode.Playing, null, Now));

        var snapshot = coordinator.SnapshotAt(Now);

        Assert.Equal(IslandMode.Resolving, snapshot.Mode);
        Assert.Equal("正在取得歌詞", snapshot.DisplayText);
        Assert.Null(snapshot.LyricLine);
    }

    private static LocalLyricsPlayback CreateLocal(
        long positionMilliseconds,
        bool isPlaying,
        IReadOnlyList<LyricLine> lines,
        string trackId = "local",
        NeteaseLyricsStatus lyricsStatus = NeteaseLyricsStatus.Available) =>
        new(
            trackId,
            new MediaPlaybackSnapshot("歌", ["歌手"], 180000, positionMilliseconds, Now, isPlaying),
            lyricsStatus,
            lines);

    private static RemoteLyricsPlayback CreateRemote(string sessionId, long sequence, string text) =>
        new(sessionId, sequence, IslandMode.Playing, new LyricLine(text, 0, null), Now);
}
