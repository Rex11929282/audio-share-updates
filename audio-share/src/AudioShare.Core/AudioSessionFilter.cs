namespace AudioShare.Core;

public static class AudioSessionFilter
{
    public static IReadOnlyList<AudioSession> GetActiveProcessSessions(
        IEnumerable<AudioSessionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate =>
                candidate.IsActive &&
                !candidate.IsSystemSession &&
                candidate.ProcessId > 0 &&
                !string.IsNullOrWhiteSpace(candidate.ProcessName))
            .GroupBy(candidate => candidate.ProcessId)
            .Select(group => group
                .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.DisplayName))
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
                .First())
            .Select(candidate => new AudioSession(
                candidate.ProcessId,
                candidate.ProcessName,
                string.IsNullOrWhiteSpace(candidate.DisplayName)
                    ? candidate.ProcessName
                    : candidate.DisplayName,
                HasAudio: true))
            .ToArray();
    }
}
