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
        "勾选的程序会走 B1 分享；没有勾选的程序会走 AUX，只自己听。",
        "Discord 的麦克风选 B1，Discord 本身不要勾选分享，否则朋友可能听到自己的回声。",
        "点击停止分享或关闭 FlowCast 后，当前程序都会改回只自己听。",
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
            "怎么做：先让要分享的程序开始播放声音，再回到 FlowCast 点“重新检测”。勾选要分享的程序，然后点“应用所选音频路由”。\n为什么：勾选的程序会走 Input；没有勾选的程序会走 AUX。\n成功后：朋友只会听到你勾选的程序，例如 Chrome 或网易云音乐。\n出问题时：程序没有出现时，让它先播放声音，再重新检测。",
            ["勾选程序", "应用路由", "朋友听到"],
            ["没有勾选任何程序时，应用按钮不能按。"]),
        new(
            "6. 停止分享",
            "怎么做：点击“停止分享，只自己听”；也可以直接关闭 FlowCast。\n为什么：FlowCast 会把当前检测到的程序都改回 AUX。\n成功后：你仍然听得到声音，Discord 朋友听不到这些程序的声音。\n出问题时：如果朋友还听得到，重新打开 FlowCast，点“重新检测”后再停止分享一次。",
            ["停止分享", "回到 AUX", "只自己听"],
            ["停止分享或关闭 FlowCast 后，声音都会回到 AUX。"]),
        new(
            "7. 安全功能和更新",
            "怎么做：需要定时停止时，先开始分享，再点击“定时停止”，输入分钟数并开始计时。遇到问题时点击“诊断”，把复制的内容发给技术支持。看到更新提示时先下载，之后关闭并重新打开 FlowCast 完成更新。\n为什么：定时停止可避免忘记结束分享；诊断内容用于确认 Banana、设备和路由状态；更新会先验证文件再替换。\n成功后：定时到点会停止分享并取消勾选；更新完成后重新打开的就是新版本。\n出问题时：如果状态显示“需要处理”，不要继续分享，点击“停止分享”或重新打开 FlowCast，让程序先回到只自己听。",
            ["定时停止", "诊断", "关闭后重新打开"],
            ["只有主动点击“定时停止”后，才会开始计时。", "更新下载完成后，关闭并重新打开 FlowCast。"]),
    ];
}
