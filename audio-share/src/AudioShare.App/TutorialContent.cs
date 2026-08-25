namespace AudioShare.App;

public sealed record TutorialCallout(
    string Number,
    string Label,
    double X,
    double Y,
    double TargetX,
    double TargetY);

public sealed record TutorialScreenshot(
    string ResourceUri,
    IReadOnlyList<TutorialCallout> Callouts);

public sealed record TutorialStep(
    string Title,
    string Body,
    IReadOnlyList<string> DiagramNodes,
    IReadOnlyList<string> KeyRules,
    TutorialScreenshot? Screenshot = null);

public static class TutorialContent
{
    public static IReadOnlyList<string> Rules { get; } =
    [
        "一次只分享一個程序。勾選程序後會先出現確認窗口；確認後朋友才會聽到它。選擇播放路徑本身不會開始分享。",
        "Voicemeeter Input 是分享路徑：打開 A1 和 B1。Voicemeeter AUX Input 是本機路徑：只打開 A1，B1 必須關閉。",
        "Discord 的麥克風請選擇 Voicemeeter Out B1；Discord 自己不要加入分享，否則朋友可能聽到自己的回聲。",
        "停止分享、程序關閉或 FlowCast 關閉時，FlowCast 會恢復開始分享前的播放路徑。暫時恢復失敗時，下次打開 FlowCast 會先自動重試。",
        "最小化到系統托盤後，FlowCast 仍會繼續保護分享狀態。",
    ];

    public static IReadOnlyList<TutorialStep> Steps { get; } =
    new TutorialStep[]
    {
        new(
            "首次使用：第 1 步，確認 Banana",
            "打開 FlowCast 會自動嘗試打開並最小化 Voicemeeter Banana。若狀態顯示未安裝，請下載並安裝 Banana，再重啟電腦。安裝普通版 Voicemeeter 不夠。",
            ["打開 FlowCast", "確認 Banana", "開始設置"],
            ["必須同時檢測到 Banana、Input 和 AUX，才能開始分享。"]),
        new(
            "首次使用：第 2 步，設置自己聽到的設備",
            "打開 Voicemeeter Banana，在右上角 A1 選擇你的耳機或喇叭。A1 是你自己聽聲音的出口。",
            ["A1", "耳機或喇叭", "自己聽到"],
            ["聽不到電腦聲音時，先檢查 A1 是否仍是你的耳機或喇叭。"]),
        new(
            "首次使用：第 3 步，設置 Discord",
            "在 Discord 的語音和視頻中，把麥克風設為 Voicemeeter Out B1，把揚聲器設為 Voicemeeter AUX Input。",
            ["Discord", "麥克風：B1", "揚聲器：AUX"],
            ["這樣朋友的聲音只回到你的耳機，不會被重新送回朋友。"],
            new TutorialScreenshot(
                "/Assets/tutorial-discord-voice-video.png",
                [
                    new("1", "麥克風：Voicemeeter Out B1", 0.23, 0.48, 0.43, 0.33),
                    new("2", "揚聲器：Voicemeeter AUX Input", 0.60, 0.48, 0.72, 0.33),
                ])),
        new(
            "選擇程序和播放路徑",
            "程序暫時沒有聲音時也可以在 FlowCast 的下拉菜單選擇播放路徑。菜單會顯示 Windows 默認設備和可用耳機或喇叭，不會顯示 Voicemeeter Input 或 AUX，因為它們由 FlowCast 在分享時自動管理。選擇後暫停再重新播放該程序即可生效。",
            ["選擇程序", "選擇播放路徑", "重新播放"],
            ["選擇播放路徑不會自動開始分享。", "勾選確認後，FlowCast 才會把選中的程序切到分享路徑。", "完全沒有音頻工作階段的新程序，Windows 暫時無法讓 FlowCast 自動識別。"]),
        new(
            "開始分享",
            "想分享時，直接勾選程序即可。即使它暫時沒有聲音也可以勾選；確認窗口會說明朋友會聽到這個程序。確認後 FlowCast 才會啟用 B1，並把其他程序保持在僅自己聽的路徑。",
            ["勾選程序", "確認", "啟用 B1", "朋友聽到"],
            ["已開始分享的程序會鎖定，不能取消勾選或修改路徑。", "想換程序時，勾選另一個程序並確認；FlowCast 會先恢復原程序。"]),
        new(
            "停止分享和異常恢復",
            "點擊停止分享後，FlowCast 會取消勾選、關閉 B1，並恢復開始分享前的播放路徑。被選中的程序、Banana 或音頻設備突然關閉時，也會優先停止分享並恢復原路徑。",
            ["停止分享", "恢復原路徑", "只自己聽"],
            ["關閉窗口時可選擇最小化或徹底關閉。", "最小化後會停止非必要動畫，分享狀態仍會繼續監控。", "要停止分享時，請點擊停止分享並等待恢復原播放路徑。"]),
        new(
            "Banana 設置圖",
            "對照此圖確認 A1、Input 和 AUX 的位置。",
            ["A1", "Input", "AUX"],
            ["Input 打開 B1，AUX 關閉 B1。"],
            new TutorialScreenshot(
                "/Assets/tutorial-voicemeeter-banana.png",
                [
                    new("1", "A1：選擇你的耳機或喇叭", 0.54, 0.20, 0.72, 0.11),
                    new("2", "Voicemeeter Input：打開 A1 和 B1", 0.05, 0.32, 0.55, 0.66),
                    new("3", "Voicemeeter AUX Input：只打開 A1", 0.68, 0.52, 0.66, 0.59),
                    new("4", "Voicemeeter AUX Input：關閉 B1", 0.68, 0.72, 0.66, 0.74),
                ])),
    }.Select(step => step with
    {
        Body = $"{step.Body}\n\n怎麼做：按本步驟的順序設置。\n為什麼：確保只有你選擇的聲音會送給朋友。\n成功後：狀態會顯示為已就緒或正在分享。\n出問題時：點擊刷新，確認設備仍已啟用並暫停後重新播放。",
    }).ToArray();
}
