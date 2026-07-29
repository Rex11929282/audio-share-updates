namespace AudioShare.Core;

public sealed record ApplicationRouteCommand(int ProcessId, string ProcessName, string TargetDeviceId);

public sealed record ApplicationRouteSnapshot(int ProcessId, string ProcessName, string? PreviousDeviceId);

public sealed record ApplicationRoutePlan(IReadOnlyList<ApplicationRouteCommand> Commands);
