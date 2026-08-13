namespace AudioShare.Core;

public sealed record ShareRecoveryRecord(
    string ProcessName,
    string DisplayName,
    string ShareEndpointId,
    IReadOnlyList<ApplicationRouteSnapshot> Snapshots,
    DateTimeOffset StartedAt);
