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
        "operation_busy" => new("正在處理上一個操作", "當前設置沒有改變。", "稍後再試", null),
        "already_sharing" => new("這個程序已經在分享", "請先停止分享，或選擇另一個程序。", "知道了", null),
        "not_sharing" => new("目前沒有分享", "先選擇一個程序並確認分享。", "知道了", null),
        "route_start_failed" => new("沒有連接到分享路徑", "朋友暫時聽不到這個程序，原本播放路徑會保持安全。", "重新連接", FlowCastRepairAction.ReconnectRoute),
        "route_not_confirmed" => new("Windows 還沒有采用新路徑", "分享沒有開始，原本播放路徑正在恢復。", "重試", FlowCastRepairAction.ReconnectRoute),
        "banana_unavailable" => new("Voicemeeter 暫時沒有回應", "分享已經停止或靜音。", "重新打開", FlowCastRepairAction.RestartBanana),
        "restore_pending" => new("原本播放路徑正在等待恢復", "朋友已經聽不到該程序，FlowCast 會在下次啟動繼續恢復。", "刷新", FlowCastRepairAction.Refresh),
        _ => new("這次操作沒有完成", "FlowCast 已保持在安全狀態。", "重試", FlowCastRepairAction.Refresh),
    };
}
