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
        "选择播放路径不会开始分享。只有勾选正在播放的程序，再点击开始分享并确认，朋友才会听到。",
        "Voicemeeter Input 是分享路径：打开 A1 和 B1。Voicemeeter AUX Input 是本机路径：只打开 A1，B1 必须关闭。",
        "Discord 的麦克风请选择 Voicemeeter Out B1；Discord 自己不要加入分享，否则朋友可能听到自己的回声。",
        "停止分享、程序关闭或 FlowCast 关闭时，FlowCast 会恢复开始分享前的播放路径；找不到旧设备时才交回 Windows 默认播放设备。",
        "静音分享只会暂时停止朋友听到音乐；最小化到系统托盘后，FlowCast 仍会继续保护分享状态。",
    ];

    public static IReadOnlyList<TutorialStep> Steps { get; } =
    new TutorialStep[]
    {
        new(
            "首次使用：第 1 步，确认 Banana",
            "打开 FlowCast 后先看系统状态。若显示未安装，请下载并安装 Voicemeeter Banana，再重启电脑。安装普通版 Voicemeeter 不够。",
            ["安装 Banana", "重启电脑", "打开 FlowCast"],
            ["必须同时检测到 Banana、Input 和 AUX，才能开始分享。"]),
        new(
            "首次使用：第 2 步，设置自己听到的设备",
            "打开 Voicemeeter Banana，在右上角 A1 选择你的耳机或喇叭。A1 是你自己听声音的出口。",
            ["A1", "耳机或喇叭", "自己听到"],
            ["听不到电脑声音时，先检查 A1 是否仍是你的耳机或喇叭。"]),
        new(
            "首次使用：第 3 步，设置 Discord",
            "在 Discord 的语音和视频中，把麦克风设为 Voicemeeter Out B1，把扬声器设为 Voicemeeter AUX Input。",
            ["Discord", "麦克风：B1", "扬声器：AUX"],
            ["这样朋友的声音只回到你的耳机，不会被重新送回朋友。"],
            new TutorialScreenshot(
                "/Assets/tutorial-discord-voice-video.png",
                [
                    new("1", "麦克风：Voicemeeter Out B1", 0.23, 0.48, 0.43, 0.33),
                    new("2", "扬声器：Voicemeeter AUX Input", 0.60, 0.48, 0.72, 0.33),
                ])),
        new(
            "选择程序和播放路径",
            "程序没有播放声音时也可以在 FlowCast 的下拉菜单选择播放路径。可选 Windows 默认设备、Voicemeeter Input、Voicemeeter AUX Input，以及已启用的耳机或喇叭。选择后暂停再重新播放该程序即可生效。",
            ["选择程序", "选择播放路径", "重新播放"],
            ["选择 Input 只是让程序走分享路径，不会自动开始分享。", "选择 AUX 后，声音只在本机播放。", "完全没有音频工作阶段的新程序，Windows 暂时无法让 FlowCast 自动识别。"]),
        new(
            "开始分享",
            "先让要分享的程序实际播放声音。出现勾选框后勾选它，再点击开始分享。确认窗口会列出朋友会听到的程序，以及只留给你自己的程序。确认后才会启用 B1。",
            ["勾选正在播放的程序", "开始分享", "确认", "朋友听到"],
            ["没有勾选正在播放的程序时，开始分享不能按。", "已开始分享时，开始分享按钮会锁定，避免重复操作。"]),
        new(
            "停止分享和异常恢复",
            "点击停止分享后，FlowCast 会取消勾选、关闭 B1，并恢复开始分享前的播放路径。被选中的程序、Banana 或音频设备突然关闭时，也会优先停止分享并恢复原路径。",
            ["停止分享", "恢复原路径", "只自己听"],
            ["关闭窗口时可选择最小化或彻底关闭。", "最小化后会停止非必要动画，分享状态仍会继续监控。"]),
        new(
            "Banana 设置图",
            "对照此图确认 A1、Input 和 AUX 的位置。",
            ["A1", "Input", "AUX"],
            ["Input 打开 B1，AUX 关闭 B1。"],
            new TutorialScreenshot(
                "/Assets/tutorial-voicemeeter-banana.png",
                [
                    new("1", "A1：选择你的耳机或喇叭", 0.54, 0.20, 0.72, 0.11),
                    new("2", "Voicemeeter Input：打开 A1 和 B1", 0.05, 0.32, 0.55, 0.66),
                    new("3", "Voicemeeter AUX Input：只打开 A1", 0.68, 0.52, 0.66, 0.59),
                    new("4", "Voicemeeter AUX Input：关闭 B1", 0.68, 0.72, 0.66, 0.74),
                ])),
    }.Select(step => step with
    {
        Body = $"{step.Body}\n\n怎么做：按本步骤的顺序设置。\n为什么：确保只有你选择的声音会送给朋友。\n成功后：状态会显示为已就绪或正在分享。\n出问题时：点击刷新，确认设备仍已启用并暂停后重新播放。",
    }).ToArray();
}
