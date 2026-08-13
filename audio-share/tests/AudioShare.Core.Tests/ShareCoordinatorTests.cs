using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareCoordinatorTests
{
    [Fact]
    public async Task DuplicateStartExecutesRuntimeOnce()
    {
        var runtime = new FakeRuntime { BeginDelay = TimeSpan.FromMilliseconds(30) };
        var coordinator = Coordinator(runtime);
        var selected = Session("chrome.exe");

        await Task.WhenAll(
            coordinator.StartAsync(selected, [selected], CancellationToken.None),
            coordinator.StartAsync(selected, [selected], CancellationToken.None));

        Assert.Equal(1, runtime.BeginCalls);
        Assert.Equal(FlowCastShareState.Sharing, coordinator.Snapshot.State);
    }

    [Fact]
    public async Task SwitchRestoresOldRouteBeforeStartingNewShare()
    {
        var runtime = new FakeRuntime();
        var coordinator = Coordinator(runtime);
        var chrome = Session("chrome.exe");
        var music = Session("cloudmusic.exe");
        await coordinator.StartAsync(chrome, [chrome], CancellationToken.None);

        var result = await coordinator.SwitchAsync(music, [music], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(
            ["begin:chrome.exe", "bus:false", "restore", "begin:cloudmusic.exe"],
            runtime.Calls);
        Assert.Equal("cloudmusic.exe", coordinator.Snapshot.Selected!.ProcessName);
    }

    [Fact]
    public async Task ExitStopReturnsToLocalOnlyEvenWhenRestoreFails()
    {
        var runtime = new FakeRuntime { RestoreFails = true };
        var journal = new FakeJournal();
        var coordinator = Coordinator(runtime, journal);
        var selected = Session("chrome.exe");
        await coordinator.StartAsync(selected, [selected], CancellationToken.None);

        var result = await coordinator.StopAsync("FlowCast closed", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("restore_pending", result.ErrorCode);
        Assert.Equal(FlowCastShareState.LocalOnly, coordinator.Snapshot.State);
        Assert.NotNull(journal.Record);
    }

    [Fact]
    public async Task ReconcileWritesExpandedRestoreRecordForNewApplication()
    {
        var runtime = new FakeRuntime();
        var journal = new FakeJournal();
        var coordinator = Coordinator(runtime, journal);
        var chrome = Session("chrome.exe");
        var steam = Session("steam.exe");
        await coordinator.StartAsync(chrome, [chrome], CancellationToken.None);

        var result = await coordinator.ReconcileAsync([chrome, steam], CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, journal.Record!.Snapshots.Count);
    }

    [Fact]
    public async Task StopWhenLocalOnlyDoesNotCheckB1OrAttemptRouteRestore()
    {
        var runtime = new FakeRuntime();
        var coordinator = Coordinator(runtime);

        var result = await coordinator.StopAsync("FlowCast closed", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(FlowCastShareState.LocalOnly, coordinator.Snapshot.State);
        Assert.Empty(runtime.Calls);
    }

    private static ShareCoordinator Coordinator(FakeRuntime runtime, FakeJournal? journal = null) =>
        new(new ShareStateMachine(), runtime, journal ?? new FakeJournal(), "input-id", () => DateTimeOffset.UnixEpoch);

    private static AudioSession Session(string processName) =>
        new(processName.GetHashCode(), 100, processName, processName, false);

    private sealed class FakeJournal : IShareRecoveryJournal
    {
        public ShareRecoveryRecord? Record { get; private set; }

        public Task<ShareRecoveryRecord?> ReadAsync(CancellationToken token) => Task.FromResult(Record);

        public Task WriteAsync(ShareRecoveryRecord record, CancellationToken token)
        {
            Record = record;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            Record = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRuntime : IShareRouteRuntime
    {
        public int BeginCalls { get; private set; }
        public bool RestoreFails { get; init; }
        public TimeSpan BeginDelay { get; init; }
        public List<string> Calls { get; } = [];

        public async Task<RouteMutationResult> BeginShareAsync(
            AudioSession selected,
            IReadOnlyList<AudioSession> active,
            CancellationToken token)
        {
            BeginCalls++;
            Calls.Add($"begin:{selected.ProcessName}");
            if (BeginDelay > TimeSpan.Zero)
            {
                await Task.Delay(BeginDelay, token);
            }

            return new(true, true, Snapshots(active), null);
        }

        public Task<RouteMutationResult> ReconcileApplicationsAsync(
            AudioSession selected,
            IReadOnlyList<AudioSession> active,
            CancellationToken token) =>
            Task.FromResult(new RouteMutationResult(true, true, Snapshots(active), null));

        public Task<RouteMutationResult> RestoreAsync(CancellationToken token)
        {
            Calls.Add("restore");
            return Task.FromResult(new RouteMutationResult(!RestoreFails, RestoreFails, [], RestoreFails ? "restore failed" : null));
        }

        public Task<RouteMutationResult> RestoreAsync(
            IReadOnlyList<ApplicationRouteSnapshot> snapshots,
            CancellationToken token) =>
            Task.FromResult(new RouteMutationResult(true, false, snapshots, null));

        public Task<RouteTruthSnapshot> ReadTruthAsync(
            AudioSession? selected,
            IReadOnlyList<AudioSession> active,
            CancellationToken token) =>
            Task.FromResult(new RouteTruthSnapshot(true, true, true, true, true, false, 0.2f, 0.2f, null));

        public Task SetSharingBusAsync(bool enabled, CancellationToken token)
        {
            Calls.Add($"bus:{enabled.ToString().ToLowerInvariant()}");
            return Task.CompletedTask;
        }

        private static IReadOnlyList<ApplicationRouteSnapshot> Snapshots(IReadOnlyList<AudioSession> sessions) =>
            sessions.Select(session => new ApplicationRouteSnapshot(
                session.ProcessId,
                session.ProcessStartUtcTicks,
                session.ProcessName,
                new ApplicationRouteState(null, null))).ToArray();
    }
}
