namespace AudioShare.Core;

public static class AudioSessionFilter
{
    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Voicemod",
        "Voicemod.exe",
        "voicemeeterpro",
        "voicemeeterpro.exe",
    };

    public static IReadOnlyList<AudioSession> GetActiveProcessSessions(
        IEnumerable<AudioSessionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate =>
                candidate.IsActive &&
                !candidate.IsSystemSession &&
                candidate.ProcessId > 0 &&
                !string.IsNullOrWhiteSpace(candidate.ProcessName) &&
                !ExcludedProcessNames.Contains(candidate.ProcessName))
            .GroupBy(candidate => candidate.ProcessId)
            .Select(group => group
                .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.DisplayName))
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
                .First())
            .Select(candidate => new AudioSession(
                candidate.ProcessId,
                candidate.ProcessStartUtcTicks,
                candidate.ProcessName,
                string.IsNullOrWhiteSpace(candidate.DisplayName)
                    ? candidate.ProcessName
                    : candidate.DisplayName,
                HasAudio: true))
            .ToArray();
    }
}
