using AudioShare.Core;

namespace AudioShare.Windows;

public interface IExternalRoutingHelper
{
    Task<string?> GetRouteAsync(int processId, CancellationToken token);

    Task SetRouteAsync(int processId, string deviceId, CancellationToken token);

    Task ClearRouteAsync(int processId, CancellationToken token);
}

public sealed class ApplicationRouteExecutor : IApplicationRouteExecutor
{
    private readonly IExternalRoutingHelper helper;
    private IReadOnlyList<ApplicationRouteSnapshot>? latestSuccessfulTransaction;

    public ApplicationRouteExecutor(IExternalRoutingHelper helper)
    {
        this.helper = helper;
    }

    public async Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token)
    {
        if (plan.Commands.Count == 0)
        {
            return new ApplicationRouteExecutionResult(false, "Application route plan is empty.", []);
        }

        var snapshots = new List<ApplicationRouteSnapshot>(plan.Commands.Count);
        foreach (var command in plan.Commands)
        {
            var previousDeviceId = await helper.GetRouteAsync(command.ProcessId, token);
            snapshots.Add(new ApplicationRouteSnapshot(command.ProcessId, command.ProcessName, previousDeviceId));
        }

        var changedSnapshots = new List<ApplicationRouteSnapshot>(snapshots.Count);
        try
        {
            for (var index = 0; index < plan.Commands.Count; index++)
            {
                var command = plan.Commands[index];
                await helper.SetRouteAsync(command.ProcessId, command.TargetDeviceId, token);
                changedSnapshots.Add(snapshots[index]);
            }
        }
        catch (Exception exception)
        {
            var recoveryFailures = new List<string>();
            foreach (var snapshot in changedSnapshots.AsEnumerable().Reverse())
            {
                try
                {
                    await RestoreSnapshotAsync(snapshot, CancellationToken.None);
                }
                catch (Exception recoveryException)
                {
                    recoveryFailures.Add($"Process {snapshot.ProcessId}: {recoveryException.Message}");
                }
            }

            var message = recoveryFailures.Count == 0
                ? exception.Message
                : $"{exception.Message} Recovery failed: {string.Join("; ", recoveryFailures)}";
            return new ApplicationRouteExecutionResult(false, message, snapshots);
        }

        latestSuccessfulTransaction = snapshots.ToArray();
        return new ApplicationRouteExecutionResult(true, null, snapshots);
    }

    public async Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token)
    {
        if (latestSuccessfulTransaction is null)
        {
            return new ApplicationRouteExecutionResult(false, "No successful application route transaction exists.", []);
        }

        if (token.IsCancellationRequested)
        {
            return new ApplicationRouteExecutionResult(false, "Restore was canceled before recovery began.", []);
        }

        try
        {
            foreach (var snapshot in latestSuccessfulTransaction.Reverse())
            {
                await RestoreSnapshotAsync(snapshot, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            return new ApplicationRouteExecutionResult(false, exception.Message, latestSuccessfulTransaction);
        }

        var snapshots = latestSuccessfulTransaction;
        latestSuccessfulTransaction = null;
        return new ApplicationRouteExecutionResult(true, null, snapshots);
    }

    private Task RestoreSnapshotAsync(ApplicationRouteSnapshot snapshot, CancellationToken token)
    {
        return snapshot.PreviousDeviceId is null
            ? helper.ClearRouteAsync(snapshot.ProcessId, token)
            : helper.SetRouteAsync(snapshot.ProcessId, snapshot.PreviousDeviceId, token);
    }
}
