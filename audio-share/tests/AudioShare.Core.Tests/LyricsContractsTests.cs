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
    public void ConnectionState_ContainsOnlyTheApprovedStates()
    {
        Assert.Equal(
            ["Searching", "Unavailable", "Connected", "Disconnected"],
            Enum.GetNames<ConnectionState>());
    }
}
