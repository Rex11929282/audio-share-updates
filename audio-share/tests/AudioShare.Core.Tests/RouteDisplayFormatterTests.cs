using AudioShare.App;

namespace AudioShare.Core.Tests;

public sealed class RouteDisplayFormatterTests
{
    [Fact]
    public void SummarizeCollapsesMultipleOutputDevices()
    {
        var summary = RouteDisplayFormatter.SummarizeDevice("Dummy Output (Voicemod), MAG 274F (NVIDIA), Voicemeeter AUX Input");

        Assert.Equal("多個播放設備", summary);
    }

    [Fact]
    public void SummarizeUsesWindowsDefaultWhenNoRouteIsKnown()
    {
        Assert.Equal("跟隨 Windows 默認播放設備", RouteDisplayFormatter.SummarizeDevice(null));
    }
}
