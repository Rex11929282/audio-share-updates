namespace AudioShare.Core;

public sealed record ApplicationRouteExecutionResult(
    bool Succeeded,
    bool HasPendingTransaction,
    string? Message,
    IReadOnlyList<ApplicationRouteSnapshot> Snapshots);

public interface IApplicationRouteExecutor
{
    Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token);

    Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token);

    void CompletePersistentRouting();
}
