namespace AudioShare.Core;

public sealed record AudioSessionCandidate(
    int ProcessId,
    long ProcessStartUtcTicks,
    string ProcessName,
    string DisplayName,
    bool IsActive,
    bool IsSystemSession);
