namespace AudioShare.Core;

public static class ShareSignalSelfTest
{
    private const float SignalThreshold = 0.02f;

    public static string GetMessage(bool isSharing, bool isMainInputShared, float inputLevel, float b1Level)
    {
        if (!isSharing || !isMainInputShared)
        {
            return "尚未開始分享。勾選應用程式後，朋友才會聽到。";
        }

        if (b1Level >= SignalThreshold)
        {
            return "正在分享，朋友應該能聽到目前的聲音。";
        }

        return inputLevel >= SignalThreshold
            ? "聲音已進入 FlowCast，但尚未送到朋友。請檢查 Voicemeeter Banana 的 B1 是否開啟。"
            : "正在分享，但 FlowCast 尚未收到聲音。請確認該程式正在播放；若它指定了其他輸出，請改為跟隨系統預設。";
    }
}
