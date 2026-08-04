using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsRuntimeTests
{
    private static readonly MediaPlaybackSnapshot PlayingMedia = new(
        "歌",
        ["歌手"],
        180_000,
        1_000,
        DateTimeOffset.UtcNow,
        true);

    [Fact]
    public async Task LocalTrack_ResolvesThenShowsAndPublishesARealLyric()
    {
        var media = new FakeMediaSessionSource();
        var relay = new FakeLyricsRelay();
        await using var runtime = CreateRuntime(media, relay);
        var snapshots = Observe(runtime);
        await runtime.StartAsync(CancellationToken.None);

        media.Emit(PlayingMedia);

        Assert.Equal(
            IslandMode.Resolving,
            (await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Mode == IslandMode.Resolving)).Mode);
        var playing = await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Mode == IslandMode.Playing);
        Assert.Equal("真實歌詞", playing.DisplayText);
        Assert.Equal(LyricSource.LocalNetEase, playing.Source);
        await WaitUntilAsync(() => relay.Published.Any(frame => frame.LyricLine?.Text == "真實歌詞"));
    }

    [Fact]
    public async Task IncomingRemoteLyric_WinsThenExpiresBackToLocal()
    {
        var media = new FakeMediaSessionSource();
        var relay = new FakeLyricsRelay();
        await using var runtime = CreateRuntime(media, relay, TimeSpan.FromMilliseconds(180));
        var snapshots = Observe(runtime);
        await runtime.StartAsync(CancellationToken.None);
        media.Emit(PlayingMedia);
        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Source == LyricSource.LocalNetEase);

        relay.Emit(new RadminLyricsFrame(
            1,
            "friend-a",
            1,
            "123",
            IslandMode.Playing,
            new LyricLine("朋友歌詞", 0, null),
            1_000,
            DateTimeOffset.UtcNow,
            true));

        Assert.Equal(
            "朋友歌詞",
            (await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Source == LyricSource.RemoteRadmin)).DisplayText);
        snapshots.Clear();
        Assert.Equal(
            "真實歌詞",
            (await WaitForSnapshotAsync(snapshots, snapshot =>
                snapshot.Source == LyricSource.LocalNetEase && snapshot.DisplayText == "真實歌詞")).DisplayText);
    }

    [Fact]
    public async Task ConfirmedRemoteDisconnect_ClearsTheLastRemoteLyricImmediately()
    {
        var media = new FakeMediaSessionSource();
        var relay = new FakeLyricsRelay();
        await using var runtime = CreateRuntime(media, relay, TimeSpan.FromSeconds(5));
        var snapshots = Observe(runtime);
        await runtime.StartAsync(CancellationToken.None);
        relay.Emit(new RadminLyricsFrame(
            1,
            "friend-a",
            1,
            "123",
            IslandMode.Playing,
            new LyricLine("朋友歌詞", 0, null),
            1_000,
            DateTimeOffset.UtcNow,
            true));
        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Source == LyricSource.RemoteRadmin);

        snapshots.Clear();
        relay.Disconnect();

        var idle = await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Mode == IslandMode.Idle);
        Assert.Equal("等待播放", idle.DisplayText);
        Assert.Null(idle.LyricLine);
    }

    [Fact]
    public async Task TrackResolutionTimeout_ShowsUnavailableWithoutFaultingTheRuntime()
    {
        var media = new FakeMediaSessionSource();
        var relay = new FakeLyricsRelay();
        await using var runtime = CreateRuntime(
            media,
            relay,
            trackResolver: new FakeTrackResolver(new OperationCanceledException("search timeout")));
        var snapshots = Observe(runtime);
        await runtime.StartAsync(CancellationToken.None);

        media.Emit(PlayingMedia);

        var unavailable = await WaitForSnapshotAsync(
            snapshots,
            snapshot => snapshot.Mode == IslandMode.Unavailable);
        Assert.Equal("暫時無法取得歌詞", unavailable.DisplayText);
    }

    [Fact]
    public async Task LocalSessionLoss_ClearsTheLyricAndStopsPublishing()
    {
        var media = new FakeMediaSessionSource();
        var relay = new FakeLyricsRelay();
        await using var runtime = CreateRuntime(media, relay);
        var snapshots = Observe(runtime);
        await runtime.StartAsync(CancellationToken.None);
        media.Emit(PlayingMedia);
        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Mode == IslandMode.Playing);

        snapshots.Clear();
        media.Emit(null);

        await WaitForSnapshotAsync(snapshots, snapshot => snapshot.Mode == IslandMode.Idle);
        await WaitUntilAsync(() => relay.StopPublishingCalled);
    }

    [Fact]
    public async Task Dispose_CancelsTheWatchAndDisposesTheRelay()
    {
        var media = new FakeMediaSessionSource();
        var relay = new FakeLyricsRelay();
        var runtime = CreateRuntime(media, relay);
        await runtime.StartAsync(CancellationToken.None);

        await runtime.DisposeAsync();

        Assert.True(relay.Disposed);
        Assert.True(media.WatchCancelled);
    }

    private static LyricsRuntime CreateRuntime(
        FakeMediaSessionSource media,
        FakeLyricsRelay relay,
        TimeSpan? remoteLifetime = null,
        INeteaseTrackResolver? trackResolver = null) =>
        new(
            media,
            trackResolver ?? new FakeTrackResolver(),
            new FakeLyricsProvider(),
            relay,
            new LyricsSourceCoordinator(remoteLifetime ?? TimeSpan.FromSeconds(5)),
            TimeProvider.System);

    private static ConcurrentQueue<IslandSnapshot> Observe(LyricsRuntime runtime)
    {
        var snapshots = new ConcurrentQueue<IslandSnapshot>();
        runtime.SnapshotChanged += (_, snapshot) => snapshots.Enqueue(snapshot);
        return snapshots;
    }

    private static async Task<IslandSnapshot> WaitForSnapshotAsync(
        ConcurrentQueue<IslandSnapshot> snapshots,
        Func<IslandSnapshot, bool> predicate)
    {
        await WaitUntilAsync(() => snapshots.Any(predicate));
        return snapshots.Last(predicate);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakeMediaSessionSource : INeteaseMediaSessionSource
    {
        private readonly Channel<MediaPlaybackSnapshot?> snapshots = Channel.CreateUnbounded<MediaPlaybackSnapshot?>();

        public bool WatchCancelled { get; private set; }

        public void Emit(MediaPlaybackSnapshot? snapshot) => snapshots.Writer.TryWrite(snapshot);

        public async IAsyncEnumerable<MediaPlaybackSnapshot?> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var snapshot in snapshots.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return snapshot;
                }
            }
            finally
            {
                WatchCancelled = cancellationToken.IsCancellationRequested;
            }
        }
    }

    private sealed class FakeTrackResolver(Exception? exception = null) : INeteaseTrackResolver
    {
        public Task<string?> ResolveAsync(
            string title,
            IReadOnlyList<string> artists,
            CancellationToken cancellationToken) => exception is null
            ? Task.FromResult<string?>("123")
            : Task.FromException<string?>(exception);
    }

    private sealed class FakeLyricsProvider : INeteaseLyricsProvider
    {
        public Task<NeteaseLyricsResult> GetAsync(string trackId, CancellationToken cancellationToken) =>
            Task.FromResult(new NeteaseLyricsResult(
                NeteaseLyricsStatus.Available,
                [new LyricLine("真實歌詞", 0, null)]));
    }

    private sealed class FakeLyricsRelay : ILyricsRelay
    {
        public event EventHandler<RadminLyricsFrame>? FrameReceived;

        public event EventHandler? Connected
        {
            add { }
            remove { }
        }

        public event EventHandler? ConnectionClosed;

        public ConcurrentQueue<RadminLyricsFrame> Published { get; } = new();

        public bool StopPublishingCalled { get; private set; }

        public bool Disposed { get; private set; }

        public Task StartReceivingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishAsync(RadminLyricsFrame frame, CancellationToken cancellationToken)
        {
            Published.Enqueue(frame);
            return Task.CompletedTask;
        }

        public Task StopPublishingAsync(CancellationToken cancellationToken)
        {
            StopPublishingCalled = true;
            return Task.CompletedTask;
        }

        public void Emit(RadminLyricsFrame frame) => FrameReceived?.Invoke(this, frame);

        public void Disconnect() => ConnectionClosed?.Invoke(this, EventArgs.Empty);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
