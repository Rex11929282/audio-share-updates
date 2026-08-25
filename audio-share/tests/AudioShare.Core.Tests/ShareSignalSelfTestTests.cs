using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareSignalSelfTestTests
{
    [Fact]
    public void GetMessage_WhenNotSharing_DoesNotClaimSignal()
    {
        var message = ShareSignalSelfTest.GetMessage(false, false, 1f, 1f);

        Assert.Equal("尚未開始分享。勾選應用程式後，朋友才會聽到。", message);
    }

    [Fact]
    public void GetMessage_WhenB1HasSignal_ReportsOnlyTheMeasuredSignal()
    {
        var message = ShareSignalSelfTest.GetMessage(true, true, 0.02f, 0.02f);

        Assert.Equal("正在分享，朋友應該能聽到目前的聲音。", message);
    }

    [Fact]
    public void GetMessage_WhenB1HasNoSignal_AsksUserToPlayTheSelectedProgram()
    {
        var message = ShareSignalSelfTest.GetMessage(true, true, 0.019f, 0.019f);

        Assert.Equal("正在分享，但 FlowCast 尚未收到聲音。請確認該程式正在播放；若它指定了其他輸出，請改為跟隨系統預設。", message);
    }

    [Fact]
    public void GetMessage_WhenInputHasSignalButB1DoesNot_ExplainsTheBananaIssue()
    {
        var message = ShareSignalSelfTest.GetMessage(true, true, 0.02f, 0f);

        Assert.Equal("聲音已進入 FlowCast，但尚未送到朋友。請檢查 Voicemeeter Banana 的 B1 是否開啟。", message);
    }
}
