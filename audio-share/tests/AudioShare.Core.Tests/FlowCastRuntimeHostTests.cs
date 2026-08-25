using AudioShare.App;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class FlowCastRuntimeHostTests
{
    [Fact]
    public async Task Startup_StartsVoicemodBeforeWaitingForBanana()
    {
        var calls = new List<string>();
        var host = new FlowCastRuntimeHost(
            new RecordingRuntime(calls),
            new RecordingJournal(calls, null),
            _ =>
            {
                calls.Add("banana-ready");
                return Task.FromResult(true);
            },
            _ =>
            {
                calls.Add("voicemod");
                return Task.FromResult(true);
            });

        await host.InitializeAsync(CancellationToken.None);

        Assert.Equal(["voicemod", "banana-ready"], calls);
    }

    [Fact]
    public async Task Startup_StartsVoicemodEvenWhenBananaIsNotReady()
    {
        var calls = new List<string>();
        var host = new FlowCastRuntimeHost(
            new RecordingRuntime(calls),
            new RecordingJournal(calls, null),
            _ =>
            {
                calls.Add("banana-not-ready");
                return Task.FromResult(false);
            },
            _ =>
            {
                calls.Add("voicemod");
                return Task.FromResult(false);
            });

        await host.InitializeAsync(CancellationToken.None);

        Assert.Equal(["voicemod", "banana-not-ready"], calls);
    }

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
        var host = new FlowCastRuntimeHost(
            runtime,
            journal,
            _ =>
            {
                calls.Add("banana");
                return Task.FromResult(true);
            },
            _ =>
            {
                calls.Add("voicemod");
                return Task.FromResult(true);
            });

        await host.InitializeAsync(CancellationToken.None);

        Assert.Equal(["voicemod", "banana", "bus:false", "restore:snapshots", "journal:clear"], calls);
    }

    [Fact]
    public async Task Startup_WaitsForVoicemodReadinessBeforeStartingBanana()
    {
        var calls = new List<string>();
        var voicemodReady = new TaskCompletionSource<bool>();
        var host = new FlowCastRuntimeHost(
            new RecordingRuntime(calls),
            new RecordingJournal(calls, null),
            _ =>
            {
                calls.Add("banana");
                return Task.FromResult(true);
            },
            async _ =>
            {
                calls.Add("voicemod:start");
                await voicemodReady.Task;
                calls.Add("voicemod:ready");
                return true;
            });

        var initialization = host.InitializeAsync(CancellationToken.None);
        await Task.Yield();
        Assert.Equal(["voicemod:start"], calls);

        voicemodReady.SetResult(true);
        await initialization;

        Assert.Equal(["voicemod:start", "voicemod:ready", "banana"], calls);
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
