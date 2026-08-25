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
            ? "當前未勾選任何程序。未勾選程序不會分享到 B1；應用路由後會送到 Voicemeeter AUX Input。"
            : "可在 Windows 音量混音器中將所選程序的輸出設置為 Voicemeeter Input。" +
              "Discord 和正常播放應保持在 Voicemeeter AUX Input。";
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
            : "未勾選任何程序";

        return $"已勾選程序：{selectedApplicationsText}。\n" +
               $"{selectedToInputCount} 個正在播放的程序會送到 Voicemeeter Input（B1 分享總線）。\n" +
               $"{unselectedToAuxCount} 個正在播放的程序會送到 Voicemeeter AUX Input（僅本機收聽）。\n\n" +
               "勾選一個程序會影響同一程序名稱的全部活動進程。例如，勾選 Chrome 會影響同時播放音頻的所有 Chrome 進程。\n\n" +
               "Discord 和 Voicemeeter 不會被本程序路由。";
    }
}
