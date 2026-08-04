using AudioShare.Lyrics.Contracts;
using System.IO;

namespace AudioShare.Lyrics;

public sealed class LyricsConnectionRuntimeTests
{
    [Fact]
    public async Task RealConnectionAndConfirmedDisconnect_EmitExactTruthfulSequence()
    {
        var relay = new FakeLyricsRelay();
        await using var runtime = new LyricsConnectionRuntime(relay);
        var states = new List<LyricsConnectionState>();
        runtime.ConnectionStateChanged += (_, state) => states.Add(state);

        await runtime.StartAsync(CancellationToken.None);
        relay.RaiseConnected();
        relay.RaiseConnectionClosed();

        Assert.Equal(
            [
                LyricsConnectionState.FindingFlowcast,
                LyricsConnectionState.ConnectedAwaitingLyrics,
                LyricsConnectionState.FindingFlowcast
            ],
            states);
    }

    [Fact]
    public async Task RepeatedStart_StartsRelayAndEmitsInitialStateOnce()
    {
        var relay = new FakeLyricsRelay();
        await using var runtime = new LyricsConnectionRuntime(relay);
        var states = new List<LyricsConnectionState>();
        runtime.ConnectionStateChanged += (_, state) => states.Add(state);

        await runtime.StartAsync(CancellationToken.None);
        await runtime.StartAsync(CancellationToken.None);

        Assert.Equal(1, relay.StartCount);
        Assert.Equal([LyricsConnectionState.FindingFlowcast], states);
    }

    [Fact]
    public async Task FailedStart_CanBeRetried()
    {
        var relay = new FakeLyricsRelay();
        relay.StartFailures.Enqueue(new IOException("receiver failed"));
        await using var runtime = new LyricsConnectionRuntime(relay);

        await Assert.ThrowsAsync<IOException>(() => runtime.StartAsync(CancellationToken.None));
        await runtime.StartAsync(CancellationToken.None);

        Assert.Equal(2, relay.StartCount);
    }

    [Fact]
    public async Task CancelledStart_CanBeRetried()
    {
        var relay = new FakeLyricsRelay();
        relay.StartFailures.Enqueue(new OperationCanceledException());
        await using var runtime = new LyricsConnectionRuntime(relay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StartAsync(CancellationToken.None));
        await runtime.StartAsync(CancellationToken.None);

        Assert.Equal(2, relay.StartCount);
    }

    [Fact]
    public async Task RepeatedDispose_UnsubscribesAndDisposesRelayExactlyOnce()
    {
        var relay = new FakeLyricsRelay();
        var runtime = new LyricsConnectionRuntime(relay);
        var states = new List<LyricsConnectionState>();
        runtime.ConnectionStateChanged += (_, state) => states.Add(state);
        await runtime.StartAsync(CancellationToken.None);

        await runtime.DisposeAsync();
        await runtime.DisposeAsync();
        relay.RaiseConnected();
        relay.RaiseConnectionClosed();

        Assert.Equal(1, relay.ConnectedAddCount);
        Assert.Equal(1, relay.ConnectedRemoveCount);
        Assert.Equal(1, relay.ClosedAddCount);
        Assert.Equal(1, relay.ClosedRemoveCount);
        Assert.Equal(1, relay.DisposeCount);
        Assert.Equal([LyricsConnectionState.FindingFlowcast], states);
    }

    private sealed class FakeLyricsRelay : ILyricsRelay
    {
        private EventHandler? connected;
        private EventHandler? connectionClosed;

        public event EventHandler<RadminLyricsFrame>? FrameReceived
        {
            add { }
            remove { }
        }

        public event EventHandler? Connected
        {
            add
            {
                ConnectedAddCount++;
                connected += value;
            }
            remove
            {
                ConnectedRemoveCount++;
                connected -= value;
            }
        }

        public event EventHandler? ConnectionClosed
        {
            add
            {
                ClosedAddCount++;
                connectionClosed += value;
            }
            remove
            {
                ClosedRemoveCount++;
                connectionClosed -= value;
            }
        }

        public int StartCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int ConnectedAddCount { get; private set; }

        public int ConnectedRemoveCount { get; private set; }

        public int ClosedAddCount { get; private set; }

        public int ClosedRemoveCount { get; private set; }

        public Queue<Exception> StartFailures { get; } = new();

        public Task StartReceivingAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            if (StartFailures.TryDequeue(out var failure))
            {
                return Task.FromException(failure);
            }

            return Task.CompletedTask;
        }

        public Task PublishAsync(RadminLyricsFrame frame, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StopPublishingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RaiseConnected() => connected?.Invoke(this, EventArgs.Empty);

        public void RaiseConnectionClosed() => connectionClosed?.Invoke(this, EventArgs.Empty);

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
