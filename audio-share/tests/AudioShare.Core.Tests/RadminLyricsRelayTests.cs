using System.Collections.Concurrent;
using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class RadminLyricsRelayTests
{
    [Fact]
    public async Task NoAdapter_DoesNotDiscoverOrSignalAConnection()
    {
        var receiver = new FakeReceiver((_, _) => Task.FromResult(false));
        var retry = new RetryProbe();
        await using var relay = new RadminLyricsRelay("instance", receiver, () => false, retry.WaitAsync);
        var events = ObserveConnectionEvents(relay);

        await relay.StartReceivingAsync(CancellationToken.None);
        await retry.Reached.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(0, receiver.DiscoverCount);
        Assert.Empty(events);
    }

    [Fact]
    public async Task NoPeer_DoesNotSignalAConnection()
    {
        var receiver = new FakeReceiver((_, _) => Task.FromResult(false));
        var retry = new RetryProbe();
        await using var relay = new RadminLyricsRelay("instance", receiver, () => true, retry.WaitAsync);
        var events = ObserveConnectionEvents(relay);

        await relay.StartReceivingAsync(CancellationToken.None);
        await retry.Reached.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, receiver.DiscoverCount);
        Assert.Empty(events);
    }

    [Fact]
    public async Task SuccessfulConnection_RaisesConnectedBeforeConnectionClosed()
    {
        var receiver = new FakeReceiver((_, _) => Task.FromResult(true));
        var retry = new RetryProbe();
        await using var relay = new RadminLyricsRelay("instance", receiver, () => true, retry.WaitAsync);
        var events = ObserveConnectionEvents(relay, out var connected, out var closed);

        await relay.StartReceivingAsync(CancellationToken.None);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        receiver.RaiseConnectionClosed();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(["connected", "closed"], events);
    }

    [Fact]
    public async Task ConnectionClosedDuringDiscovery_DoesNotPublishAStaleConnectedState()
    {
        var receiver = new FakeReceiver((current, _) =>
        {
            current.RaiseConnectionClosed();
            return Task.FromResult(true);
        });
        var retry = new RetryProbe();
        await using var relay = new RadminLyricsRelay("instance", receiver, () => true, retry.WaitAsync);
        var events = ObserveConnectionEvents(relay);

        await relay.StartReceivingAsync(CancellationToken.None);
        await retry.Reached.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(1, receiver.DiscoverCount);
        Assert.Empty(events);
    }

    private static ConcurrentQueue<string> ObserveConnectionEvents(RadminLyricsRelay relay)
    {
        var events = new ConcurrentQueue<string>();
        relay.Connected += (_, _) => events.Enqueue("connected");
        relay.ConnectionClosed += (_, _) => events.Enqueue("closed");
        return events;
    }

    private static ConcurrentQueue<string> ObserveConnectionEvents(
        RadminLyricsRelay relay,
        out TaskCompletionSource connected,
        out TaskCompletionSource closed)
    {
        var events = new ConcurrentQueue<string>();
        connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectedSignal = connected;
        var closedSignal = closed;
        relay.Connected += (_, _) =>
        {
            events.Enqueue("connected");
            connectedSignal.TrySetResult();
        };
        relay.ConnectionClosed += (_, _) =>
        {
            events.Enqueue("closed");
            closedSignal.TrySetResult();
        };
        return events;
    }

    private sealed class RetryProbe
    {
        internal TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task WaitAsync(CancellationToken cancellationToken)
        {
            Reached.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FakeReceiver(
        Func<FakeReceiver, CancellationToken, Task<bool>> discover) : ILyricsRelayReceiver
    {
        public event EventHandler<RadminLyricsFrame>? FrameReceived
        {
            add { }
            remove { }
        }

        public event EventHandler? ConnectionClosed;

        internal int DiscoverCount { get; private set; }

        public Task<bool> DiscoverAndConnectOnRadminAsync(CancellationToken cancellationToken)
        {
            DiscoverCount++;
            return discover(this, cancellationToken);
        }

        internal void RaiseConnectionClosed() => ConnectionClosed?.Invoke(this, EventArgs.Empty);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
