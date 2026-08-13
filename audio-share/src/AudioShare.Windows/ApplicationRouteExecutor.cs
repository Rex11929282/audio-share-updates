using AudioShare.Core;

namespace AudioShare.Windows;

public interface IExternalRoutingHelper
{
    Task<ApplicationRouteState> GetRouteAsync(
        int processId,
        long processStartUtcTicks,
        string processName,
        CancellationToken token);

    Task SetRouteAsync(
        int processId,
        long processStartUtcTicks,
        string processName,
        string deviceId,
        CancellationToken token);

    Task RestoreRouteAsync(
        int processId,
        long processStartUtcTicks,
        string processName,
        ApplicationRouteState route,
        CancellationToken token);
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
        if (pendingTransaction is not null)
        {
            return new ApplicationRouteExecutionResult(
                false,
                true,
                "Restore the owned application route transaction before applying another plan.",
                pendingTransaction);
        }

        return await ApplyCoreAsync(plan, [], token);
    }

    public async Task<ApplicationRouteExecutionResult> ApplyAdditionalAsync(ApplicationRoutePlan plan, CancellationToken token)
    {
        if (pendingTransaction is null)
        {
            return new ApplicationRouteExecutionResult(
                false,
                false,
                "No owned application route transaction exists for reconciliation.",
                []);
        }

        var existing = pendingTransaction;
        var knownRoutes = existing
            .Select(snapshot => (snapshot.ProcessId, snapshot.ProcessStartUtcTicks))
            .ToHashSet();
        var additionalCommands = plan.Commands
            .Where(command => !knownRoutes.Contains((command.ProcessId, command.ProcessStartUtcTicks)))
            .ToArray();
        if (additionalCommands.Length == 0)
        {
            return new ApplicationRouteExecutionResult(true, true, null, existing);
        }

        return await ApplyCoreAsync(new ApplicationRoutePlan(additionalCommands), existing, token);
    }

    private async Task<ApplicationRouteExecutionResult> ApplyCoreAsync(
        ApplicationRoutePlan plan,
        IReadOnlyList<ApplicationRouteSnapshot> existingSnapshots,
        CancellationToken token)
    {

        if (plan.Commands.Count == 0)
        {
            return new ApplicationRouteExecutionResult(
                false,
                existingSnapshots.Count > 0,
                "Application route plan is empty.",
                existingSnapshots);
        }

        var snapshots = new List<ApplicationRouteSnapshot>(plan.Commands.Count);
        foreach (var command in plan.Commands)
        {
            ApplicationRouteState previousRoute;
            try
            {
                previousRoute = await helper.GetRouteAsync(
                    command.ProcessId,
                    command.ProcessStartUtcTicks,
                    command.ProcessName,
                    token);
            }
            catch (InvalidOperationException exception) when (IsMissingActiveOutputSession(exception))
            {
                // The persisted Windows rule can still be written before the app starts outputting audio.
                previousRoute = new ApplicationRouteState(null, null);
            }

            snapshots.Add(new ApplicationRouteSnapshot(
                command.ProcessId,
                command.ProcessStartUtcTicks,
                command.ProcessName,
                previousRoute));
        }

        var ownedSnapshots = new List<ApplicationRouteSnapshot>(snapshots.Count);
        try
        {
            for (var index = 0; index < plan.Commands.Count; index++)
            {
                var command = plan.Commands[index];
                ownedSnapshots.Add(snapshots[index]);
                await helper.SetRouteAsync(
                    command.ProcessId,
                    command.ProcessStartUtcTicks,
                    command.ProcessName,
                    command.TargetDeviceId,
                    token);
            }
        }
        catch (Exception exception)
        {
            var recovery = await RestoreSnapshotsAsync(ownedSnapshots);
            pendingTransaction = recovery.FailedSnapshots.Count == 0
                ? existingSnapshots.Count == 0 ? null : existingSnapshots
                : existingSnapshots.Concat(recovery.FailedSnapshots).ToArray();
            var message = recovery.Messages.Count == 0
                ? exception.Message
                : $"{exception.Message} Recovery failed: {string.Join("; ", recovery.Messages)}";
            return new ApplicationRouteExecutionResult(
                false,
                pendingTransaction is not null,
                message,
                existingSnapshots.Concat(snapshots).ToArray());
        }

        pendingTransaction = existingSnapshots.Concat(snapshots).ToArray();
        return new ApplicationRouteExecutionResult(true, true, null, pendingTransaction);
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

    public async Task<ApplicationRouteExecutionResult> RestoreAsync(
        IReadOnlyList<ApplicationRouteSnapshot> snapshots,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0)
        {
            return new ApplicationRouteExecutionResult(true, false, null, []);
        }

        if (token.IsCancellationRequested)
        {
            return new ApplicationRouteExecutionResult(
                false,
                true,
                "Restore was canceled before recovery began.",
                snapshots);
        }

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

        return new ApplicationRouteExecutionResult(true, false, null, snapshots);
    }

    public void CompletePersistentRouting() => pendingTransaction = null;

    private async Task<RecoveryResult> RestoreSnapshotsAsync(IReadOnlyList<ApplicationRouteSnapshot> snapshots)
    {
        var failedSnapshots = new List<ApplicationRouteSnapshot>();
        var messages = new List<string>();
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                await helper.RestoreRouteAsync(
                    snapshot.ProcessId,
                    snapshot.ProcessStartUtcTicks,
                    snapshot.ProcessName,
                    snapshot.PreviousRoute,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                if (IsMissingActiveOutputSession(exception))
                {
                    continue;
                }

                if (IsMissingPlaybackDevice(exception) &&
                    await RestoreWindowsDefaultAsync(snapshot))
                {
                    continue;
                }

                failedSnapshots.Insert(0, snapshot);
                messages.Add($"Process {snapshot.ProcessId}: {exception.Message}");
            }
        }

        return new RecoveryResult(failedSnapshots, messages);
    }

    private static bool IsMissingActiveOutputSession(Exception exception) =>
        exception.Message.Contains("Active output session not found", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingPlaybackDevice(Exception exception) =>
        exception.Message.Contains("device not found", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("endpoint not found", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("device is not connected", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> RestoreWindowsDefaultAsync(ApplicationRouteSnapshot snapshot)
    {
        try
        {
            await helper.RestoreRouteAsync(
                snapshot.ProcessId,
                snapshot.ProcessStartUtcTicks,
                snapshot.ProcessName,
                new ApplicationRouteState(null, null),
                CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (IsMissingActiveOutputSession(exception))
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record RecoveryResult(
        IReadOnlyList<ApplicationRouteSnapshot> FailedSnapshots,
        IReadOnlyList<string> Messages);
}
