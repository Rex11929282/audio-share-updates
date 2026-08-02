using System.Text.Json;
using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsOverlayPresenterTests
{
    [Fact]
    public void ConnectedWithoutALine_UsesTheHonestWaitingCopy()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.SetConnectionState(ConnectionState.Connected);

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("connected", json.RootElement.GetProperty("connectionState").GetString());
        Assert.Equal("已連線，等待歌詞", json.RootElement.GetProperty("displayText").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
    }

    [Fact]
    public void Disconnect_ClearsAPreviouslyReceivedLine()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.SetConnectionState(ConnectionState.Connected);
        presenter.ShowLyricLine(new LyricLine("只可顯示收到的文字", 1000, null));

        presenter.SetConnectionState(ConnectionState.Disconnected);

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("與 FlowCast 的連線已中斷，正在重新連線", json.RootElement.GetProperty("displayText").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
    }

    [Fact]
    public void WhitespaceLine_ReturnsToWaitingCopy()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.SetConnectionState(ConnectionState.Connected);
        presenter.ShowLyricLine(new LyricLine("   ", 0, null));

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("已連線，等待歌詞", json.RootElement.GetProperty("displayText").GetString());
    }
}
