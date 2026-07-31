namespace AudioShare.Core;

public static class AudioRoutingPolicy
{
    private static readonly string[] ProtectedProcessPrefixes =
    {
        "discord", "voicemod", "voicemeeter", "wallpaper32", "wallpaper64", "wallpaper_engine",
    };

    public static bool IsProtectedProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var executableName = Path.GetFileName(processName);
        return ProtectedProcessPrefixes.Any(prefix =>
            executableName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetSetupInstruction(IReadOnlyCollection<AudioSession> selectedSessions)
    {
        ArgumentNullException.ThrowIfNull(selectedSessions);

        if (selectedSessions.Any(session => IsProtectedProcess(session.ProcessName)))
        {
            throw new ArgumentException("Protected processes cannot be configured for sharing.", nameof(selectedSessions));
        }

        return selectedSessions.Count == 0
            ? "当前未勾选任何程序。未勾选程序不会分享到 B1；应用路由后会送到 Voicemeeter AUX Input。"
            : "可在 Windows 音量混音器中将所选程序的输出设置为 Voicemeeter Input。" +
              "Discord 和正常播放应保持在 Voicemeeter AUX Input。";
    }

    public static string GetRouteConfirmationText(
        IReadOnlyCollection<AudioSession> selectedSessions,
        int selectedToInputCount,
        int unselectedToAuxCount)
    {
        ArgumentNullException.ThrowIfNull(selectedSessions);

        if (selectedSessions.Any(session => IsProtectedProcess(session.ProcessName)))
        {
            throw new ArgumentException("Protected processes cannot be configured for sharing.", nameof(selectedSessions));
        }

        var selectedApplications = selectedSessions
            .Select(session => string.IsNullOrWhiteSpace(session.DisplayName) ? session.ProcessName : session.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        var selectedApplicationsText = selectedApplications.Any()
            ? string.Join(", ", selectedApplications)
            : "未勾选任何程序";

        return $"已勾选程序：{selectedApplicationsText}。\n" +
               $"{selectedToInputCount} 个正在播放的程序会送到 Voicemeeter Input（B1 分享总线）。\n" +
               $"{unselectedToAuxCount} 个正在播放的程序会送到 Voicemeeter AUX Input（仅本机收听）。\n\n" +
               "勾选一个程序会影响同一程序名称的全部活动进程。例如，勾选 Chrome 会影响同时播放音频的所有 Chrome 进程。\n\n" +
               "Discord 和 Voicemeeter 不会被本程序路由。";
    }
}
