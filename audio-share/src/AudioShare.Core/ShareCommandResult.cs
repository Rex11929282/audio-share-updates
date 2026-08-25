namespace AudioShare.Core;

public sealed record ShareCommandResult(
    bool Succeeded,
    FlowCastShareState State,
    string? ErrorCode,
    string? TechnicalMessage);

public interface IShareCoordinator
{
    ShareStateSnapshot Snapshot { get; }

    Task<ShareCommandResult> StartAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token);

    Task<ShareCommandResult> SwitchAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token);

    Task<ShareCommandResult> StopAsync(string reason, TimeSpan timeout, CancellationToken token);

    Task<ShareCommandResult> ReconcileAsync(IReadOnlyList<AudioSession> active, CancellationToken token);
}
