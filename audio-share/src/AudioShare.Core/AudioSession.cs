namespace AudioShare.Core;

public sealed record AudioSession(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    bool HasAudio);
