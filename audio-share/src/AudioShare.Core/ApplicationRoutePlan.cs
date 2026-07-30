namespace AudioShare.Core;

public sealed record ApplicationRouteCommand(
    int ProcessId,
    long ProcessStartUtcTicks,
    string ProcessName,
    string TargetDeviceId);

public sealed record ApplicationRouteState(string? ConsoleDeviceId, string? MultimediaDeviceId);

public sealed record ApplicationRouteSnapshot(
    int ProcessId,
    long ProcessStartUtcTicks,
    string ProcessName,
    ApplicationRouteState PreviousRoute);

public sealed record ApplicationRoutePlan(IReadOnlyList<ApplicationRouteCommand> Commands);
