namespace AudioShare.Core;

public sealed record AudioSessionCandidate(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    bool IsActive,
    bool IsSystemSession);
