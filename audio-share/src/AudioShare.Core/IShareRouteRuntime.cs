namespace AudioShare.Core;

public sealed record RouteTruthSnapshot(
    bool RuntimeReady,
    bool SelectedProcessAlive,
    bool SelectedOnShareEndpoint,
    bool UnselectedApplicationsLocalOnly,
    bool MainInputShared,
    bool AuxShared,
    float InputLevel,
    float B1Level,
    string? TechnicalMessage)
{
    public bool IsSharing => RuntimeReady &&
                             SelectedProcessAlive &&
                             SelectedOnShareEndpoint &&
                             UnselectedApplicationsLocalOnly &&
                             MainInputShared &&
                             !AuxShared;
}

public sealed record RouteMutationResult(
    bool Succeeded,
    bool HasPendingRestore,
    IReadOnlyList<ApplicationRouteSnapshot> Snapshots,
    string? TechnicalMessage);

public sealed record FlowCastEndpointSet(string InputDeviceId, string AuxDeviceId);

public interface ISharingBusController
{
    Task SetSharedAsync(bool enabled, CancellationToken token);

    Task<SharingBusStatus> ReadAsync(CancellationToken token);
}

public interface IShareRouteRuntime
{
    Task<RouteMutationResult> BeginShareAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token);

    Task<RouteMutationResult> ReconcileApplicationsAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token);

    Task<RouteMutationResult> RestoreAsync(CancellationToken token);

    Task<RouteMutationResult> RestoreAsync(
        IReadOnlyList<ApplicationRouteSnapshot> snapshots,
        CancellationToken token);

    Task<RouteTruthSnapshot> ReadTruthAsync(
        AudioSession? selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token);

    Task SetSharingBusAsync(bool enabled, CancellationToken token);
}
