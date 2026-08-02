namespace AudioShare.Core;

public static class AudioSessionFilter
{
    public static IReadOnlyList<AudioSession> GetActiveProcessSessions(
        IEnumerable<AudioSessionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => candidate.IsActive)
            .Where(candidate => !candidate.IsSystemSession)
            .Where(candidate => candidate.ProcessId > 0)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ProcessName))
            .Where(candidate => !AudioRoutingPolicy.IsProtectedProcess(candidate.ProcessName))
            .GroupBy(candidate => candidate.ProcessId)
            .Select(group =>
            {
                var representative = group
                    .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.DisplayName))
                    .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .First();
                var outputDevices = group
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.OutputDeviceId))
                    .GroupBy(candidate => candidate.OutputDeviceId, StringComparer.OrdinalIgnoreCase)
                    .Select(device => device
                        .OrderByDescending(candidate => candidate.HasAudio)
                        .ThenBy(candidate => candidate.OutputDeviceName, StringComparer.OrdinalIgnoreCase)
                        .First())
                    .OrderBy(candidate => candidate.OutputDeviceName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new AudioSession(
                    representative.ProcessId,
                    representative.ProcessStartUtcTicks,
                    representative.ProcessName,
                    string.IsNullOrWhiteSpace(representative.DisplayName)
                        ? representative.ProcessName
                        : representative.DisplayName,
                    HasAudio: group.Any(candidate => candidate.HasAudio),
                    OutputDeviceId: outputDevices.Length == 1 ? outputDevices[0].OutputDeviceId : string.Empty,
                    OutputDeviceName: string.Join("、", outputDevices
                        .Select(device => string.IsNullOrWhiteSpace(device.OutputDeviceName) ? "未知输出设备" : device.OutputDeviceName)));
            })
            .ToArray();
    }
}
