using AudioShare.Core;

namespace AudioShare.Windows;

public interface IExternalRoutingHelper
{
    Task<ApplicationRouteState> GetRouteAsync(int processId, CancellationToken token);

    Task SetRouteAsync(int processId, string deviceId, CancellationToken token);

    Task RestoreRouteAsync(int processId, ApplicationRouteState route, CancellationToken token);
}

public sealed class ApplicationRouteExecutor : IApplicationRouteExecutor
{
    private readonly IExternalRoutingHelper helper;
    private IReadOnlyList<ApplicationRouteSnapshot>? pendingTransaction;

    public ApplicationRouteExecutor(IExternalRoutingHelper helper)
    {
        this.helper = helper;
    }

    public async Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token)
    {
        if (plan.Commands.Count == 0)
        {
            return new ApplicationRouteExecutionResult(false, false, "Application route plan is empty.", []);
        }

        var snapshots = new List<ApplicationRouteSnapshot>(plan.Commands.Count);
        foreach (var command in plan.Commands)
        {
            var previousRoute = await helper.GetRouteAsync(command.ProcessId, token);
            snapshots.Add(new ApplicationRouteSnapshot(command.ProcessId, command.ProcessName, previousRoute));
        }

        var ownedSnapshots = new List<ApplicationRouteSnapshot>(snapshots.Count);
        try
        {
            for (var index = 0; index < plan.Commands.Count; index++)
            {
                var command = plan.Commands[index];
                ownedSnapshots.Add(snapshots[index]);
                await helper.SetRouteAsync(command.ProcessId, command.TargetDeviceId, token);
            }
        }
        catch (Exception exception)
        {
            var recovery = await RestoreSnapshotsAsync(ownedSnapshots);
            pendingTransaction = recovery.FailedSnapshots.Count == 0
                ? null
                : recovery.FailedSnapshots;
            var message = recovery.Messages.Count == 0
                ? exception.Message
                : $"{exception.Message} Recovery failed: {string.Join("; ", recovery.Messages)}";
            return new ApplicationRouteExecutionResult(
                false,
                pendingTransaction is not null,
                message,
                snapshots);
        }

        pendingTransaction = snapshots.ToArray();
        return new ApplicationRouteExecutionResult(true, true, null, snapshots);
    }

    public async Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token)
    {
        if (pendingTransaction is null)
        {
            return new ApplicationRouteExecutionResult(
                false,
                false,
                "No application route transaction requires recovery.",
                []);
        }

        if (token.IsCancellationRequested)
        {
            return new ApplicationRouteExecutionResult(
                false,
                true,
                "Restore was canceled before recovery began.",
                pendingTransaction);
        }

        var snapshots = pendingTransaction;
        var recovery = await RestoreSnapshotsAsync(snapshots);
        if (recovery.FailedSnapshots.Count > 0)
        {
            pendingTransaction = recovery.FailedSnapshots;
            return new ApplicationRouteExecutionResult(
                false,
                true,
                $"Restore failed: {string.Join("; ", recovery.Messages)}",
                pendingTransaction);
        }

        pendingTransaction = null;
        return new ApplicationRouteExecutionResult(true, false, null, snapshots);
    }

    private async Task<RecoveryResult> RestoreSnapshotsAsync(IReadOnlyList<ApplicationRouteSnapshot> snapshots)
    {
        var failedSnapshots = new List<ApplicationRouteSnapshot>();
        var messages = new List<string>();
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                await helper.RestoreRouteAsync(snapshot.ProcessId, snapshot.PreviousRoute, CancellationToken.None);
            }
            catch (Exception exception)
            {
                failedSnapshots.Insert(0, snapshot);
                messages.Add($"Process {snapshot.ProcessId}: {exception.Message}");
            }
        }

        return new RecoveryResult(failedSnapshots, messages);
    }

    private sealed record RecoveryResult(
        IReadOnlyList<ApplicationRouteSnapshot> FailedSnapshots,
        IReadOnlyList<string> Messages);
}
