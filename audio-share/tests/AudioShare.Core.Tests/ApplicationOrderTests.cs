using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ApplicationOrderTests
{
    [Fact]
    public void MoveBefore_ReordersSelectedProgramsForRouting()
    {
        var chrome = new AudioSession(1, 1, "chrome.exe", "Chrome", true);
        var cloudMusic = new AudioSession(2, 2, "cloudmusic.exe", "CloudMusic", true);
        var order = new ApplicationOrder(["chrome", "cloudmusic"]);

        Assert.True(order.MoveBefore(cloudMusic, chrome));
        Assert.Equal([cloudMusic, chrome], order.Order([chrome, cloudMusic]));
    }

    [Fact]
    public void Synchronize_PreservesSavedProgramsThatAreNotRunning()
    {
        var chrome = new AudioSession(1, 1, "chrome.exe", "Chrome", true);
        var order = new ApplicationOrder(["cloudmusic"]);

        order.Synchronize([chrome]);

        Assert.Equal(["CLOUDMUSIC", "CHROME"], order.ProcessNames);
    }
}
