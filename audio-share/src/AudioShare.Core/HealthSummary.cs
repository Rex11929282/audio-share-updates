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
        bool routingHelperReady = true) =>
        new(
        [
            new("Banana", bananaRunning ? HealthState.Ready : HealthState.Attention,
                bananaRunning ? "Voicemeeter Banana 正在運行。" : "未檢測到正在運行的 Voicemeeter Banana。"),
            new("A1", hasDefaultPlayback ? HealthState.Ready : HealthState.Attention,
                hasDefaultPlayback ? "已檢測到 Windows 默認播放設備。" : "未檢測到 Windows 默認播放設備。"),
            new("Input", hasInput ? HealthState.Ready : HealthState.Attention,
                hasInput ? "Voicemeeter Input 已就緒。" : EndpointMessage("Voicemeeter Input", routingHelperReady)),
            new("AUX", hasAux ? HealthState.Ready : HealthState.Attention,
                hasAux ? "Voicemeeter AUX Input 已就緒。" : EndpointMessage("Voicemeeter AUX Input", routingHelperReady)),
        ]);

    // When the bundled routing helper cannot run, endpoint detection is impossible, so the
    // chip must not claim the device is missing (which sends users hunting the wrong problem).
    private static string EndpointMessage(string endpoint, bool routingHelperReady) =>
        routingHelperReady
            ? $"未檢測到 {endpoint}。"
            : $"FlowCast 路由組件尚未就緒，暫時無法檢測 {endpoint}（裝置可能其實存在）。";

    public bool IsReady(string key) =>
        Items.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal) && item.State == HealthState.Ready);
}
