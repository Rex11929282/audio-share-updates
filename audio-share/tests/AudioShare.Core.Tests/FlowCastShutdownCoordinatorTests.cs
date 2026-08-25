using System.Diagnostics;
using AudioShare.App;
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class FlowCastShutdownCoordinatorTests
{
    [Fact]
    public async Task ShutdownDoesNotWaitBeyondConfiguredTimeout()
    {
        var shutdown = new FlowCastShutdownCoordinator(new HangingShareCoordinator(), TimeSpan.FromMilliseconds(100));
        var watch = Stopwatch.StartNew();

        await shutdown.ExitAsync();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    private sealed class HangingShareCoordinator : IShareCoordinator
    {
        public ShareStateSnapshot Snapshot { get; } = new(FlowCastShareState.Sharing, null, 0, null);

        public Task<ShareCommandResult> StartAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ShareCommandResult> SwitchAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<ShareCommandResult> StopAsync(string reason, TimeSpan timeout, CancellationToken token) =>
            Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith(
                _ => new ShareCommandResult(false, FlowCastShareState.LocalOnly, "restore_pending", null),
                CancellationToken.None);

        public Task<ShareCommandResult> ReconcileAsync(IReadOnlyList<AudioSession> active, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
