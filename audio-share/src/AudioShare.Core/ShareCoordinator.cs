namespace AudioShare.Core;

public sealed class ShareCoordinator : IShareCoordinator, IDisposable
{
    private readonly ShareStateMachine state;
    private readonly IShareRouteRuntime runtime;
    private readonly IShareRecoveryJournal journal;
    private readonly string shareEndpointId;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ShareCoordinator(
        ShareStateMachine state,
        IShareRouteRuntime runtime,
        IShareRecoveryJournal journal,
        string shareEndpointId,
        Func<DateTimeOffset>? clock = null)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        ArgumentException.ThrowIfNullOrWhiteSpace(shareEndpointId);
        this.shareEndpointId = shareEndpointId;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ShareStateSnapshot Snapshot => state.Snapshot;

    public async Task<ShareCommandResult> StartAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(active);
        if (!await gate.WaitAsync(0, token))
        {
            return Result(false, "operation_busy");
        }

        try
        {
            return await StartCoreAsync(selected, active, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShareCommandResult> SwitchAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(active);
        await gate.WaitAsync(token);
        try
        {
            if (state.Snapshot.State == FlowCastShareState.LocalOnly)
            {
                return await StartCoreAsync(selected, active, token);
            }

            if (state.Snapshot.Selected is not null && SameProcess(state.Snapshot.Selected, selected))
            {
                return Result(false, "already_sharing");
            }

            var stopped = await StopCoreAsync("Switch program", TimeSpan.FromSeconds(3), token);
            if (!stopped.Succeeded)
            {
                return stopped;
            }

            return await StartCoreAsync(selected, active, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShareCommandResult> StopAsync(string reason, TimeSpan timeout, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        await gate.WaitAsync(token);
        try
        {
            return await StopCoreAsync(reason, timeout, token);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShareCommandResult> ReconcileAsync(IReadOnlyList<AudioSession> active, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(active);
        await gate.WaitAsync(token);
        try
        {
            var selected = state.Snapshot.Selected;
            if (selected is null || state.Snapshot.State != FlowCastShareState.Sharing)
            {
                return Result(true, null);
            }

            var route = await runtime.ReconcileApplicationsAsync(selected, active, token);
            if (!route.Succeeded)
            {
                state.RequireAttention(state.Snapshot.OperationId, route.TechnicalMessage ?? "Route reconciliation failed.");
                return Result(false, "route_start_failed", route.TechnicalMessage);
            }

            await journal.WriteAsync(ToRecoveryRecord(selected, route.Snapshots), token);
            return Result(true, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private async Task<ShareCommandResult> StartCoreAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token)
    {
        if (state.Snapshot.State != FlowCastShareState.LocalOnly)
        {
            return Result(false, "already_sharing");
        }

        var operation = state.BeginStart(selected);
        RouteMutationResult route;
        try
        {
            route = await runtime.BeginShareAsync(selected, active, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await FailAndRestoreAsync("route_start_failed", "Sharing setup was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            return await FailAndRestoreAsync("route_start_failed", exception.Message);
        }

        if (!route.Succeeded)
        {
            return await FailAndRestoreAsync("route_start_failed", route.TechnicalMessage);
        }

        try
        {
            await journal.WriteAsync(ToRecoveryRecord(selected, route.Snapshots), token);
            var truth = await runtime.ReadTruthAsync(selected, active, token);
            if (!truth.IsSharing)
            {
                return await FailAndRestoreAsync("route_not_confirmed", truth.TechnicalMessage);
            }

            state.CompleteStart(operation);
            return Result(true, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await FailAndRestoreAsync("route_start_failed", "Sharing setup was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            return await FailAndRestoreAsync("route_start_failed", exception.Message);
        }
    }

    private async Task<ShareCommandResult> StopCoreAsync(string reason, TimeSpan timeout, CancellationToken token)
    {
        if (state.Snapshot.State == FlowCastShareState.LocalOnly)
        {
            return Result(true, null);
        }

        var operation = state.BeginStop();
        string? busFailure = null;
        try
        {
            await runtime.SetSharingBusAsync(false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            busFailure = exception.Message;
        }

        RouteMutationResult restored;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(timeout);
        try
        {
            restored = await runtime.RestoreAsync(bounded.Token);
        }
        catch (OperationCanceledException)
        {
            restored = new RouteMutationResult(false, true, [], "Route restoration timed out.");
        }
        catch (Exception exception)
        {
            restored = new RouteMutationResult(false, true, [], exception.Message);
        }

        if (restored.Succeeded)
        {
            await journal.ClearAsync();
        }

        // Closing FlowCast must never be blocked by a failed Windows restore.
        state.CompleteRestore(operation);
        var message = busFailure ?? restored.TechnicalMessage;
        return new ShareCommandResult(
            restored.Succeeded && busFailure is null,
            state.Snapshot.State,
            restored.Succeeded && busFailure is null ? null : "restore_pending",
            message);
    }

    private async Task<ShareCommandResult> FailAndRestoreAsync(string errorCode, string? technicalMessage)
    {
        var restored = await StopCoreAsync("Share setup failed", TimeSpan.FromSeconds(3), CancellationToken.None);
        return new ShareCommandResult(false, state.Snapshot.State, errorCode, technicalMessage ?? restored.TechnicalMessage);
    }

    private ShareRecoveryRecord ToRecoveryRecord(
        AudioSession selected,
        IReadOnlyList<ApplicationRouteSnapshot> snapshots) =>
        new(selected.ProcessName, selected.DisplayName, shareEndpointId, snapshots, clock());

    private ShareCommandResult Result(bool succeeded, string? errorCode, string? message = null) =>
        new(succeeded, state.Snapshot.State, errorCode, message);

    private static bool SameProcess(AudioSession left, AudioSession right) =>
        left.ProcessId == right.ProcessId && left.ProcessStartUtcTicks == right.ProcessStartUtcTicks;
}
