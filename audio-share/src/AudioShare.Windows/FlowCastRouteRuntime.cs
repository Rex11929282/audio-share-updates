using AudioShare.Core;

namespace AudioShare.Windows;

public sealed class FlowCastRouteRuntime : IShareRouteRuntime
{
    private readonly Func<CancellationToken, Task<FlowCastEndpointSet>> endpointProvider;
    private readonly IApplicationRouteExecutor executor;
    private readonly IExternalRoutingHelper routingHelper;
    private readonly ISharingBusController sharingBus;
    private IReadOnlyList<ApplicationRouteSnapshot> ownedSnapshots = [];

    public FlowCastRouteRuntime(
        Func<CancellationToken, Task<FlowCastEndpointSet>> endpointProvider,
        IApplicationRouteExecutor executor,
        IExternalRoutingHelper routingHelper,
        ISharingBusController sharingBus)
    {
        this.endpointProvider = endpointProvider ?? throw new ArgumentNullException(nameof(endpointProvider));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.routingHelper = routingHelper ?? throw new ArgumentNullException(nameof(routingHelper));
        this.sharingBus = sharingBus ?? throw new ArgumentNullException(nameof(sharingBus));
    }

    public async Task<RouteMutationResult> BeginShareAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(active);
        if (AudioRoutingPolicy.IsProtectedProcess(selected.ProcessName))
        {
            return Failed("This application cannot be routed by FlowCast.");
        }

        var endpoints = await endpointProvider(token);
        var routeable = GetRouteableSessions(active, selected);
        var plan = ApplicationRoutePlanner.Create(routeable, [selected], endpoints.InputDeviceId, endpoints.AuxDeviceId);
        var applied = await executor.ApplyAsync(plan, token);
        if (!applied.Succeeded)
        {
            return FromExecution(applied);
        }

        ownedSnapshots = applied.Snapshots;
        try
        {
            await SetSharingBusAsync(true, token);
            return new RouteMutationResult(true, true, ownedSnapshots, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            var restored = await executor.RestoreAsync(CancellationToken.None);
            if (restored.Succeeded)
            {
                ownedSnapshots = [];
            }

            return new RouteMutationResult(
                false,
                !restored.Succeeded,
                restored.Snapshots,
                $"Could not enable the sharing bus: {exception.Message}");
        }
    }

    public async Task<RouteMutationResult> ReconcileApplicationsAsync(
        AudioSession selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(active);
        if (ownedSnapshots.Count == 0)
        {
            return Failed("No active FlowCast share route requires reconciliation.");
        }

        var endpoints = await endpointProvider(token);
        var routeable = GetRouteableSessions(active, selected);
        var plan = ApplicationRoutePlanner.Create(routeable, [selected], endpoints.InputDeviceId, endpoints.AuxDeviceId);
        var applied = await executor.ApplyAdditionalAsync(plan, token);
        if (applied.Succeeded)
        {
            ownedSnapshots = applied.Snapshots;
        }

        return FromExecution(applied);
    }

    public async Task<RouteMutationResult> RestoreAsync(CancellationToken token)
    {
        Exception? busFailure = null;
        try
        {
            await SetSharingBusAsync(false, token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            busFailure = exception;
        }

        var restored = await executor.RestoreAsync(token);
        if (restored.Succeeded)
        {
            ownedSnapshots = [];
        }

        return new RouteMutationResult(
            restored.Succeeded && busFailure is null,
            !restored.Succeeded,
            restored.Snapshots,
            busFailure is null ? restored.Message : $"Could not disable the sharing bus: {busFailure.Message}");
    }

    public async Task<RouteMutationResult> RestoreAsync(
        IReadOnlyList<ApplicationRouteSnapshot> snapshots,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var restored = await executor.RestoreAsync(snapshots, token);
        return FromExecution(restored);
    }

    public async Task<RouteTruthSnapshot> ReadTruthAsync(
        AudioSession? selected,
        IReadOnlyList<AudioSession> active,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(active);
        try
        {
            var endpoints = await endpointProvider(token);
            var bus = await sharingBus.ReadAsync(token);
            if (selected is null)
            {
                return new RouteTruthSnapshot(
                    true,
                    false,
                    false,
                    true,
                    bus.IsMainInputShared,
                    bus.IsAuxShared,
                    bus.InputLevel,
                    bus.B1Level,
                    null);
            }

            var selectedAlive = active.Any(session => SameProcess(session, selected));
            var selectedOnInput = false;
            if (selectedAlive)
            {
                var selectedRoute = await routingHelper.GetRouteAsync(
                    selected.ProcessId,
                    selected.ProcessStartUtcTicks,
                    selected.ProcessName,
                    token);
                selectedOnInput = IsRoutedTo(selectedRoute, endpoints.InputDeviceId);
            }

            var everyOtherApplicationOnAux = true;
            foreach (var session in GetRouteableSessions(active, selected)
                         .Where(session => !SameApplication(session, selected)))
            {
                var route = await routingHelper.GetRouteAsync(
                    session.ProcessId,
                    session.ProcessStartUtcTicks,
                    session.ProcessName,
                    token);
                if (!IsRoutedTo(route, endpoints.AuxDeviceId))
                {
                    everyOtherApplicationOnAux = false;
                    break;
                }
            }

            return new RouteTruthSnapshot(
                true,
                selectedAlive,
                selectedOnInput,
                everyOtherApplicationOnAux,
                bus.IsMainInputShared,
                bus.IsAuxShared,
                bus.InputLevel,
                bus.B1Level,
                null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RouteTruthSnapshot(false, false, false, false, false, false, 0, 0, exception.Message);
        }
    }

    public Task SetSharingBusAsync(bool enabled, CancellationToken token) =>
        sharingBus.SetSharedAsync(enabled, token);

    private static IReadOnlyList<AudioSession> GetRouteableSessions(
        IReadOnlyList<AudioSession> active,
        AudioSession selected)
    {
        var routeable = active
            .Where(session => !AudioRoutingPolicy.IsProtectedProcess(session.ProcessName))
            .ToList();
        if (!routeable.Any(session => SameProcess(session, selected)))
        {
            routeable.Add(selected);
        }

        return routeable;
    }

    private static bool SameProcess(AudioSession left, AudioSession right) =>
        left.ProcessId == right.ProcessId && left.ProcessStartUtcTicks == right.ProcessStartUtcTicks;

    private static bool SameApplication(AudioSession left, AudioSession right) =>
        string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase);

    private static bool IsRoutedTo(ApplicationRouteState route, string targetDeviceId) =>
        string.Equals(route.ConsoleDeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(route.MultimediaDeviceId, targetDeviceId, StringComparison.OrdinalIgnoreCase);

    private static RouteMutationResult FromExecution(ApplicationRouteExecutionResult execution) =>
        new(execution.Succeeded, execution.HasPendingTransaction, execution.Snapshots, execution.Message);

    private static RouteMutationResult Failed(string message) =>
        new(false, false, [], message);
}
