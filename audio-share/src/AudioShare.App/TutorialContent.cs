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
        "只有勾选后再点开始分享的程序，才会走 B1 给朋友听；没勾选的程序会保留在 AUX，只自己听。",
        "Discord 的麦克风选 B1，Discord 本身不要勾选分享，否则朋友可能听到自己的回声。",
        "点击停止分享会让声音回到只自己听；关闭窗口只会最小化到系统托盘，不会中断分享。",
    ];

    public static IReadOnlyList<TutorialStep> Steps { get; } =
    [
        new(
            "1. 安装 Voicemeeter Banana",
            "怎么做：在首页点“前往官方下载”，安装 Voicemeeter Banana 后重启电脑。\n为什么：FlowCast 要用 Banana 的 Input、AUX 和 B1 通道。\n成功后：打开 FlowCast 时，不会再显示未安装提示。\n出问题时：确认装的是 Banana，不是普通版 Voicemeeter；装完一定要重启电脑。",
            ["安装 Banana", "重启电脑", "打开 FlowCast"],
            ["没有 Banana，FlowCast 不能开始分享。"]),
        new(
            "2. 先让自己听得到声音",
            "怎么做：打开 Voicemeeter Banana，在右上角的硬件输出 A1 选择你的耳机或音箱。\n为什么：A1 是你自己听声音的出口。\n成功后：你能听到电脑声音和好友说话。\n出问题时：听不到声音时，先检查 A1 还是不是你的耳机。",
            ["A1", "你的耳机", "自己听到"],
            ["A1 必须选你的耳机或音箱。"]),
        new(
            "3. 分清“分享”和“只自己听”",
            "怎么做：在 Voicemeeter Input 这一栏打开 A1 和 B1；在 Voicemeeter AUX Input 这一栏只打开 A1，B1 必须关闭。\n为什么：Input 的声音会给你和朋友听；AUX 的声音只给你自己听。B1 是给 Discord 麦克风使用的输出。\n成功后：朋友只会听到走 Input 的声音。\n出问题时：朋友听到不该听的声音时，检查 AUX 的 B1 有没有关掉。",
            ["Input：A1 + B1", "Discord 麦克风：B1", "朋友听到"],
            ["只让 Input 开 B1；AUX 的 B1 必须关闭。"],
            new TutorialScreenshot(
                "/Assets/tutorial-voicemeeter-banana.png",
                [
                    new("1", "硬件输出 A1：选择你的耳机", 0.54, 0.20, 0.72, 0.11),
                    new("2", "Voicemeeter Input：A1 和 B1 都打开", 0.05, 0.32, 0.55, 0.66),
                    new("3", "Voicemeeter AUX Input：A1 打开", 0.68, 0.52, 0.66, 0.59),
                    new("4", "Voicemeeter AUX Input：B1 关闭", 0.68, 0.72, 0.66, 0.74),
                ])),
        new(
            "4. 设置 Discord",
            "怎么做：Discord 的“语音和视频”里，麦克风选 Voicemeeter Out B1，扬声器选 Voicemeeter AUX Input。\n为什么：Discord 会把 B1 送给朋友；好友说话会只回到你的耳机。\n成功后：朋友听得到你分享的音乐，不会听到自己的回声。\n出问题时：朋友听不到音乐，检查麦克风是不是 Voicemeeter Out B1。",
            ["Discord", "麦克风：B1", "扬声器：AUX"],
            ["Discord：麦克风选 B1，扬声器选 AUX。"],
            new TutorialScreenshot(
                "/Assets/tutorial-discord-voice-video.png",
                [
                    new("1", "麦克风：Voicemeeter Out B1", 0.23, 0.48, 0.43, 0.33),
                    new("2", "扬声器：Voicemeeter AUX Input", 0.60, 0.48, 0.72, 0.33),
                ])),
        new(
            "5. 选择要分享的程序",
            "怎么做：先让要分享的程序开始播放声音，再回到 FlowCast 点“刷新”。每个程序都会显示当前播放装置；若显示喇叭、耳机等其他路径，可点“改为仅自己听”。勾选要分享的程序，点击“开始分享”，再确认即可。\n为什么：FlowCast 会检查所有正在使用的播放装置。点“改为仅自己听”会把该程序设为 Voicemeeter AUX Input，不会开始分享；开始分享时才会把勾选程序送到 Input。\n成功后：朋友只会听到你勾选的程序，例如 Chrome 或网易云音乐。\n出问题时：修改路径后，请暂停再重新播放该程序；程序没有出现时，让它先播放声音，再点刷新。",
            ["勾选程序", "开始分享", "朋友听到"],
            ["没有勾选任何程序时，开始分享不能按。"]),
        new(
            "6. 静音、最小化和停止分享",
            "怎么做：分享中可点“静音分享”临时不让朋友听音乐；再点“恢复分享”即可继续。点窗口右上角 X 后，可选择最小化到系统托盘或结束 FlowCast。要结束分享时也可以点“停止分享”。\n为什么：静音不会影响你自己的播放；系统托盘让分享继续而不占桌面。\n成功后：停止分享后，你仍然听得到声音，Discord 朋友听不到这些程序。\n出问题时：如果朋友还听得到，重新打开 FlowCast，点停止分享后再确认一次。",
            ["静音分享", "系统托盘", "停止分享"],
            ["点 X 时可选择最小化或结束；只有停止分享或结束 FlowCast 才会结束分享。"]),
        new(
            "7. 定时、设置和更新",
            "怎么做：需要定时停止时，先开始分享，再点“定时”选择分钟数。设置里可改是否在停止时恢复本机播放、以及结束提示音。看到更新提示时，点更新后 FlowCast 会关闭旧版本、安装新版本并自动重新打开。\n为什么：定时停止可避免忘记结束分享；设置让你决定需要的提醒方式。\n成功后：定时到点会停止分享并取消勾选；更新完成后自动打开的就是新版本。\n出问题时：如果状态显示“需要处理”，不要继续分享，点停止分享或从系统托盘选择结束 FlowCast，让程序先回到只自己听。",
            ["定时停止", "设置", "自动更新"],
            ["只有主动点击定时后，才会开始计时。", "更新下载完成后，FlowCast 会自动安装并重新打开。"]),
    ];
}
