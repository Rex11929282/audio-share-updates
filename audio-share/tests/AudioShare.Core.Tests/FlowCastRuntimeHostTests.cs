using AudioShare.App;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class FlowCastRuntimeHostTests
{
    [Fact]
    public async Task StartupDisablesBusBeforeRecoveringJournal()
    {
        var calls = new List<string>();
        var journal = new RecordingJournal(calls, new ShareRecoveryRecord(
            "chrome.exe",
            "Chrome",
            "input-id",
            [new ApplicationRouteSnapshot(1, 2, "chrome.exe", new ApplicationRouteState(null, null))],
            DateTimeOffset.UnixEpoch));
        var runtime = new RecordingRuntime(calls);
        var host = new FlowCastRuntimeHost(runtime, journal, () => calls.Add("banana"), () => calls.Add("voicemod"));

        await host.InitializeAsync(CancellationToken.None);

        Assert.Equal(["banana", "voicemod", "bus:false", "restore:snapshots", "journal:clear"], calls);
    }

    private sealed class RecordingJournal : IShareRecoveryJournal
    {
        private readonly List<string> calls;
        private readonly ShareRecoveryRecord? record;

        public RecordingJournal(List<string> calls, ShareRecoveryRecord? record)
        {
            this.calls = calls;
            this.record = record;
        }

        public Task<ShareRecoveryRecord?> ReadAsync(CancellationToken token) => Task.FromResult(record);

        public Task WriteAsync(ShareRecoveryRecord record, CancellationToken token) => Task.CompletedTask;

        public Task ClearAsync()
        {
            calls.Add("journal:clear");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRuntime : IShareRouteRuntime
    {
        private readonly List<string> calls;

        public RecordingRuntime(List<string> calls)
        {
            this.calls = calls;
        }

        public Task<RouteMutationResult> BeginShareAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token) =>
            Task.FromResult(new RouteMutationResult(true, false, [], null));

        public Task<RouteMutationResult> ReconcileApplicationsAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token) =>
            Task.FromResult(new RouteMutationResult(true, false, [], null));

        public Task<RouteMutationResult> RestoreAsync(CancellationToken token) =>
            Task.FromResult(new RouteMutationResult(true, false, [], null));

        public Task<RouteMutationResult> RestoreAsync(IReadOnlyList<ApplicationRouteSnapshot> snapshots, CancellationToken token)
        {
            calls.Add("restore:snapshots");
            return Task.FromResult(new RouteMutationResult(true, false, snapshots, null));
        }

        public Task<RouteTruthSnapshot> ReadTruthAsync(AudioSession? selected, IReadOnlyList<AudioSession> active, CancellationToken token) =>
            Task.FromResult(new RouteTruthSnapshot(true, false, false, true, false, false, 0, 0, null));

        public Task SetSharingBusAsync(bool enabled, CancellationToken token)
        {
            calls.Add($"bus:{enabled.ToString().ToLowerInvariant()}");
            return Task.CompletedTask;
        }
    }
}
