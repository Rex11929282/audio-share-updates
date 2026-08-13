namespace AudioShare.App;

public enum FlowCastRepairAction
{
    Refresh,
    ReconnectRoute,
    RestartBanana,
}

public sealed record FlowCastErrorMessage(
    string Title,
    string Description,
    string ActionText,
    FlowCastRepairAction? Action);

public static class FlowCastErrorPresenter
{
    public static FlowCastErrorMessage FromCode(string? code) => code switch
    {
        "operation_busy" => new("正在处理上一个操作", "当前设置没有改变。", "稍后再试", null),
        "already_sharing" => new("这个程序已经在分享", "请先停止分享，或选择另一个程序。", "知道了", null),
        "not_sharing" => new("目前没有分享", "先选择一个程序并确认分享。", "知道了", null),
        "route_start_failed" => new("没有连接到分享路径", "朋友暂时听不到这个程序，原本播放路径会保持安全。", "重新连接", FlowCastRepairAction.ReconnectRoute),
        "route_not_confirmed" => new("Windows 还没有采用新路径", "分享没有开始，原本播放路径正在恢复。", "重试", FlowCastRepairAction.ReconnectRoute),
        "banana_unavailable" => new("Voicemeeter 暂时没有回应", "分享已经停止或静音。", "重新打开", FlowCastRepairAction.RestartBanana),
        "restore_pending" => new("原本播放路径正在等待恢复", "朋友已经听不到该程序，FlowCast 会在下次启动继续恢复。", "刷新", FlowCastRepairAction.Refresh),
        _ => new("这次操作没有完成", "FlowCast 已保持在安全状态。", "重试", FlowCastRepairAction.Refresh),
    };
}
