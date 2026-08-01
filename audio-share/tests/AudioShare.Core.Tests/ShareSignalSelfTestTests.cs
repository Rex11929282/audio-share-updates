using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareSignalSelfTestTests
{
    [Fact]
    public void GetMessage_WhenNotSharing_DoesNotClaimSignal()
    {
        var message = ShareSignalSelfTest.GetMessage(false, false, 1f);

        Assert.Equal("B1 自测：开始分享后会在这里确认音频信号。", message);
    }

    [Fact]
    public void GetMessage_WhenB1HasSignal_ReportsOnlyTheMeasuredSignal()
    {
        var message = ShareSignalSelfTest.GetMessage(true, true, 0.02f);

        Assert.Equal("B1 自测：已收到音乐信号。请确认 Discord 麦克风选择 Voicemeeter Out B1。", message);
    }

    [Fact]
    public void GetMessage_WhenB1HasNoSignal_AsksUserToPlayTheSelectedProgram()
    {
        var message = ShareSignalSelfTest.GetMessage(true, true, 0.019f);

        Assert.Equal("B1 自测：正在分享，但还没收到所选程序的音频。请播放音乐后再确认。", message);
    }
}
