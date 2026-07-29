namespace AudioShare.Core;

public static class AudioRoutingPolicy
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discord.exe", "voicemod", "voicemod.exe",
        "voicemeeter", "voicemeeter.exe", "voicemeeterpro", "voicemeeterpro.exe",
    };

    public static bool IsProtectedProcess(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && ProtectedProcessNames.Contains(processName);

    public static string GetSetupInstruction(IReadOnlyCollection<AudioSession> selectedSessions)
    {
        ArgumentNullException.ThrowIfNull(selectedSessions);

        if (selectedSessions.Any(session => IsProtectedProcess(session.ProcessName)))
        {
            throw new ArgumentException("Protected processes cannot be configured for sharing.", nameof(selectedSessions));
        }

        return selectedSessions.Count == 0
            ? "No application is selected. Nothing will be shared."
            : "Open Windows Volume Mixer and manually set each selected application to Voicemeeter Input. " +
              "Keep Discord and normal playback on Voicemeeter AUX Input.";
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

        return $"Selected apps: {string.Join(", ", selectedApplications)}.\n" +
               $"{selectedToInputCount} active application process(es) will route to Voicemeeter Input (share bus).\n" +
               $"{unselectedToAuxCount} active application process(es) will route to Voicemeeter AUX Input (local-only bus).\n\n" +
               "Selecting an application affects all active processes of the same application identity. " +
               "For example, selecting Chrome applies to all concurrently active Chrome audio processes.\n\n" +
               "Discord, Voicemod, Voicemeeter, and VoicemeeterPro are excluded and will not be routed.";
    }
}
