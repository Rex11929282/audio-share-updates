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
}
