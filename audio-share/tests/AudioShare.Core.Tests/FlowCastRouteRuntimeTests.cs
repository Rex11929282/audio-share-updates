using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class FlowCastRouteRuntimeTests
{
    [Fact]
    public async Task BeginShareRoutesSilentSelectedApplicationToInputAndOtherApplicationToAux()
    {
        var executor = new RecordingRouteExecutor();
        var runtime = CreateRuntime(executor, new RecordingBus());
        var chrome = Session(1, "chrome.exe", hasAudio: false);
        var game = Session(2, "game.exe", hasAudio: true);

        var result = await runtime.BeginShareAsync(chrome, [chrome, game], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("input-id", executor.TargetFor("chrome.exe"));
        Assert.Equal("aux-id", executor.TargetFor("game.exe"));
    }

    [Fact]
    public async Task ReconcileApplicationsRoutesNewApplicationToAuxDuringShare()
    {
        var executor = new RecordingRouteExecutor();
        var runtime = CreateRuntime(executor, new RecordingBus());
        var chrome = Session(1, "chrome.exe", hasAudio: false);
        var steam = Session(3, "steam.exe", hasAudio: true);

        await runtime.BeginShareAsync(chrome, [chrome], CancellationToken.None);
        var result = await runtime.ReconcileApplicationsAsync(chrome, [chrome, steam], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("aux-id", executor.TargetFor("steam.exe"));
    }

    [Fact]
    public async Task ReadTruthReportsSharingOnlyWhenSelectedRouteAndB1AreBothCorrect()
    {
        var chrome = Session(1, "chrome.exe", hasAudio: false);
        var game = Session(2, "game.exe", hasAudio: true);
        var helper = new RecordingRoutingHelper(new Dictionary<int, ApplicationRouteState>
        {
            [1] = new("input-id", "input-id"),
            [2] = new("aux-id", "aux-id"),
        });
        var runtime = new FlowCastRouteRuntime(
            _ => Task.FromResult(new FlowCastEndpointSet("input-id", "aux-id")),
            new RecordingRouteExecutor(),
            helper,
            new RecordingBus(new SharingBusStatus(true, false, 0.3f, 0.2f)));

        var truth = await runtime.ReadTruthAsync(chrome, [chrome, game], CancellationToken.None);

        Assert.True(truth.IsSharing);
        Assert.True(truth.SelectedOnShareEndpoint);
        Assert.True(truth.UnselectedApplicationsLocalOnly);
    }

    private static FlowCastRouteRuntime CreateRuntime(
        RecordingRouteExecutor executor,
        RecordingBus bus) =>
        new(
            _ => Task.FromResult(new FlowCastEndpointSet("input-id", "aux-id")),
            executor,
            new RecordingRoutingHelper(),
            bus);

    private static AudioSession Session(int processId, string processName, bool hasAudio) =>
        new(processId, processId * 100L, processName, processName, hasAudio);

    private sealed class RecordingRouteExecutor : IApplicationRouteExecutor
    {
        private readonly Dictionary<string, string> targets = new(StringComparer.OrdinalIgnoreCase);

        public Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token)
        {
            return ApplyAsync(plan);
        }

        public Task<ApplicationRouteExecutionResult> ApplyAdditionalAsync(ApplicationRoutePlan plan, CancellationToken token)
        {
            return ApplyAsync(plan);
        }

        private Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan)
        {
            foreach (var command in plan.Commands)
            {
                targets[command.ProcessName] = command.TargetDeviceId;
            }

            var snapshots = plan.Commands
                .Select(command => new ApplicationRouteSnapshot(
                    command.ProcessId,
                    command.ProcessStartUtcTicks,
                    command.ProcessName,
                    new ApplicationRouteState(null, null)))
                .ToArray();
            return Task.FromResult(new ApplicationRouteExecutionResult(true, true, null, snapshots));
        }

        public Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token) =>
            Task.FromResult(new ApplicationRouteExecutionResult(true, false, null, []));

        public Task<ApplicationRouteExecutionResult> RestoreAsync(
            IReadOnlyList<ApplicationRouteSnapshot> snapshots,
            CancellationToken token) =>
            Task.FromResult(new ApplicationRouteExecutionResult(true, false, null, snapshots));

        public void CompletePersistentRouting()
        {
        }

        public string TargetFor(string processName) => targets[processName];
    }

    private sealed class RecordingBus : ISharingBusController
    {
        private readonly SharingBusStatus status;

        public RecordingBus(SharingBusStatus? status = null)
        {
            this.status = status ?? new SharingBusStatus(true, false, 0.5f, 0.5f);
        }

        public List<bool> RequestedStates { get; } = [];

        public Task SetSharedAsync(bool enabled, CancellationToken token)
        {
            RequestedStates.Add(enabled);
            return Task.CompletedTask;
        }

        public Task<SharingBusStatus> ReadAsync(CancellationToken token) =>
            Task.FromResult(status);
    }

    private sealed class RecordingRoutingHelper : IExternalRoutingHelper
    {
        private readonly IReadOnlyDictionary<int, ApplicationRouteState> routes;

        public RecordingRoutingHelper(IReadOnlyDictionary<int, ApplicationRouteState>? routes = null)
        {
            this.routes = routes ?? new Dictionary<int, ApplicationRouteState>();
        }

        public Task<ApplicationRouteState> GetRouteAsync(
            int processId,
            long processStartUtcTicks,
            string processName,
            CancellationToken token) =>
            Task.FromResult(routes.GetValueOrDefault(processId, new ApplicationRouteState(null, null)));

        public Task SetRouteAsync(
            int processId,
            long processStartUtcTicks,
            string processName,
            string deviceId,
            CancellationToken token) =>
            Task.CompletedTask;

        public Task RestoreRouteAsync(
            int processId,
            long processStartUtcTicks,
            string processName,
            ApplicationRouteState route,
            CancellationToken token) =>
            Task.CompletedTask;
    }
}
