namespace AudioShare.Core;

public enum HealthState
{
    Ready,
    Attention,
    Unknown,
}

public sealed record HealthItem(string Key, HealthState State, string Message);

public sealed record HealthSummary(IReadOnlyList<HealthItem> Items)
{
    public static HealthSummary Create(
        bool bananaRunning,
        bool hasDefaultPlayback,
        bool hasInput,
        bool hasAux,
        bool discordRunning) =>
        new(
        [
            new("Banana", bananaRunning ? HealthState.Ready : HealthState.Attention,
                bananaRunning ? "Voicemeeter Banana 正在运行。" : "未检测到正在运行的 Voicemeeter Banana。"),
            new("A1", hasDefaultPlayback ? HealthState.Ready : HealthState.Attention,
                hasDefaultPlayback ? "已检测到 Windows 默认播放设备。" : "未检测到 Windows 默认播放设备。"),
            new("Input", hasInput ? HealthState.Ready : HealthState.Attention,
                hasInput ? "Voicemeeter Input 已就绪。" : "未检测到 Voicemeeter Input。"),
            new("AUX", hasAux ? HealthState.Ready : HealthState.Attention,
                hasAux ? "Voicemeeter AUX Input 已就绪。" : "未检测到 Voicemeeter AUX Input。"),
            new("Discord", discordRunning ? HealthState.Ready : HealthState.Attention,
                discordRunning
                    ? "Discord 正在运行。请在语音和视频中手动确认 B1 麦克风。"
                    : "Discord 未运行。启动后请手动确认 B1 麦克风。"),
        ]);

    public bool IsReady(string key) =>
        Items.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal) && item.State == HealthState.Ready);
}
