namespace AudioShare.Core;

public sealed record ApplicationRouteExecutionResult(
    bool Succeeded,
    bool HasPendingTransaction,
    string? Message,
    IReadOnlyList<ApplicationRouteSnapshot> Snapshots);

public interface IApplicationRouteExecutor
{
    Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token);

    Task<ApplicationRouteExecutionResult> ApplyAdditionalAsync(ApplicationRoutePlan plan, CancellationToken token);

    Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token);

    Task<ApplicationRouteExecutionResult> RestoreAsync(
        IReadOnlyList<ApplicationRouteSnapshot> snapshots,
        CancellationToken token);

    void CompletePersistentRouting();
}
