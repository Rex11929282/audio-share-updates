namespace AudioShare.Core;

public static class ShareSignalSelfTest
{
    private const float SignalThreshold = 0.02f;

    public static string GetMessage(bool isSharing, bool isMainInputShared, float b1Level)
    {
        if (!isSharing || !isMainInputShared)
        {
            return "B1 自测：开始分享后会在这里确认音频信号。";
        }

        return b1Level >= SignalThreshold
            ? "B1 自测：已收到音乐信号。请确认 Discord 麦克风选择 Voicemeeter Out B1。"
            : "B1 自测：正在分享，但还没收到所选程序的音频。请播放音乐后再确认。";
    }
}
