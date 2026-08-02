namespace AudioShare.Core;

public sealed record AudioSession(
    int ProcessId,
    long ProcessStartUtcTicks,
    string ProcessName,
    string DisplayName,
    bool HasAudio,
    string OutputDeviceId = "",
    string OutputDeviceName = "");
