namespace AudioShare.Core;

public sealed record ApplicationRouteCommand(int ProcessId, string ProcessName, string TargetDeviceId);

public sealed record ApplicationRouteState(string? ConsoleDeviceId, string? MultimediaDeviceId);

public sealed record ApplicationRouteSnapshot(int ProcessId, string ProcessName, ApplicationRouteState PreviousRoute);

public sealed record ApplicationRoutePlan(IReadOnlyList<ApplicationRouteCommand> Commands);
