using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareConfirmationTests
{
    [Fact]
    public void Create_SeparatesSelectedInputAndUnselectedAuxSessions()
    {
        var chrome = new AudioSession(1, 1, "chrome.exe", "Chrome", true);
        var cloudMusic = new AudioSession(2, 2, "cloudmusic.exe", "NetEase Cloud Music", true);

        var summary = ShareConfirmation.Create([chrome, cloudMusic], [cloudMusic]);

        Assert.Equal([cloudMusic], summary.InputSessions);
        Assert.Equal([chrome], summary.AuxSessions);
    }

    [Fact]
    public void Create_UsesProcessAndStartTimeIdentityAndDoesNotDuplicateSelectedSessions()
    {
        var chromeFirst = new AudioSession(1, 10, "chrome.exe", "Chrome", true);
        var chromeSecond = new AudioSession(1, 11, "chrome.exe", "Chrome", true);
        var cloudMusic = new AudioSession(2, 20, "cloudmusic.exe", "NetEase Cloud Music", true);

        var summary = ShareConfirmation.Create(
            [chromeFirst, chromeFirst, chromeSecond, cloudMusic],
            [chromeSecond, chromeSecond]);

        Assert.Equal([chromeSecond], summary.InputSessions);
        Assert.Equal([chromeFirst, cloudMusic], summary.AuxSessions);
    }
}
