using System.IO;
using System.Net.Http;
using System.Text.Json;
using AudioShare.Lyrics.Contracts;
using AudioShare.Windows;

namespace AudioShare.Lyrics;

public sealed class LyricsRuntime : IAsyncDisposable
{
    private static readonly TimeSpan DisplayInterval = TimeSpan.FromMilliseconds(100);
    private readonly object sync = new();
    private readonly INeteaseMediaSessionSource mediaSource;
    private readonly INeteaseTrackResolver trackResolver;
    private readonly INeteaseLyricsProvider lyricsProvider;
    private readonly ILyricsRelay relay;
    private readonly LyricsSourceCoordinator coordinator;
    private readonly TimeProvider timeProvider;
    private readonly HttpClient? ownedHttpClient;
    private readonly string sessionId;
    private CancellationTokenSource? runtimeCancellation;
    private CancellationTokenSource? resolutionCancellation;
    private Task? mediaTask;
    private Task? displayTask;
    private Task? resolutionTask;
    private MediaPlaybackSnapshot? currentMedia;
    private LocalLyricsPlayback? currentLocal;
    private IslandSnapshot? lastSnapshot;
    private string? currentTrackKey;
    private long sequence;
    private bool disposed;

    public LyricsRuntime()
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics",
            "LyricsCache");

        mediaSource = new NeteaseMediaSessionSource();
        trackResolver = new NeteaseTrackResolver(
            NeteasePlayingListReader.FromLocalApplicationData(),
            new NeteaseSearchClient(httpClient));
        lyricsProvider = new NeteaseLyricsClient(httpClient, new FileLyricsCache(cacheRoot));
        relay = new RadminLyricsRelay(instanceId);
        coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
        timeProvider = TimeProvider.System;
        ownedHttpClient = httpClient;
        sessionId = instanceId;
    }

    internal LyricsRuntime(
        INeteaseMediaSessionSource mediaSource,
        INeteaseTrackResolver trackResolver,
        INeteaseLyricsProvider lyricsProvider,
        ILyricsRelay relay,
        LyricsSourceCoordinator coordinator,
        TimeProvider timeProvider)
    {
        this.mediaSource = mediaSource;
        this.trackResolver = trackResolver;
        this.lyricsProvider = lyricsProvider;
        this.relay = relay;
        this.coordinator = coordinator;
        this.timeProvider = timeProvider;
        sessionId = Guid.NewGuid().ToString("N");
    }

    public event EventHandler<IslandSnapshot>? SnapshotChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (runtimeCancellation is not null)
        {
            return;
        }

        runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        relay.FrameReceived += Relay_OnFrameReceived;
        relay.ConnectionClosed += Relay_OnConnectionClosed;
        await relay.StartReceivingAsync(runtimeCancellation.Token);
        mediaTask = WatchMediaAsync(runtimeCancellation.Token);
        displayTask = RunDisplayLoopAsync(runtimeCancellation.Token);
        EmitCurrentSnapshot();
    }

    private async Task WatchMediaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in mediaSource.WatchAsync(cancellationToken))
            {
                await HandleMediaSnapshotAsync(snapshot, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            lock (sync)
            {
                currentLocal = null;
                currentMedia = null;
                currentTrackKey = null;
                coordinator.SetLocal(null);
            }

            EmitCurrentSnapshot();
        }
    }

    private async Task HandleMediaSnapshotAsync(
        MediaPlaybackSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot is null)
        {
            CancelResolution();
            lock (sync)
            {
                currentLocal = null;
                currentMedia = null;
                currentTrackKey = null;
                coordinator.SetLocal(null);
            }

            EmitCurrentSnapshot();
            await relay.StopPublishingAsync(cancellationToken);
            return;
        }

        var trackKey = CreateTrackKey(snapshot);
        var isNewTrack = false;
        lock (sync)
        {
            currentMedia = snapshot;
            if (string.Equals(currentTrackKey, trackKey, StringComparison.Ordinal))
            {
                if (currentLocal is not null)
                {
                    currentLocal = currentLocal with { Media = snapshot };
                    coordinator.SetLocal(currentLocal);
                }
            }
            else
            {
                isNewTrack = true;
                currentTrackKey = trackKey;
                currentLocal = new LocalLyricsPlayback(
                    string.Empty,
                    snapshot,
                    NeteaseLyricsStatus.Available,
                    []);
                coordinator.SetLocal(currentLocal);
            }
        }

        EmitCurrentSnapshot();
        if (!isNewTrack)
        {
            return;
        }

        CancelResolution();
        await PublishLocalSnapshotAsync(cancellationToken);
        var nextCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        resolutionCancellation = nextCancellation;
        resolutionTask = ResolveTrackAsync(trackKey, snapshot, nextCancellation.Token);
    }

    private async Task ResolveTrackAsync(
        string trackKey,
        MediaPlaybackSnapshot media,
        CancellationToken cancellationToken)
    {
        string? trackId = null;
        NeteaseLyricsResult result;
        try
        {
            trackId = await trackResolver.ResolveAsync(media.Title, media.Artists, cancellationToken);
            result = trackId is null
                ? new NeteaseLyricsResult(NeteaseLyricsStatus.Unavailable, [])
                : await lyricsProvider.GetAsync(trackId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            result = new NeteaseLyricsResult(NeteaseLyricsStatus.Unavailable, []);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
        {
            result = new NeteaseLyricsResult(NeteaseLyricsStatus.Unavailable, []);
        }

        lock (sync)
        {
            if (!string.Equals(currentTrackKey, trackKey, StringComparison.Ordinal) || currentMedia is null)
            {
                return;
            }

            currentLocal = new LocalLyricsPlayback(
                trackId ?? string.Empty,
                currentMedia,
                result.Status,
                result.Lines);
            coordinator.SetLocal(currentLocal);
        }

        EmitCurrentSnapshot();
        await PublishLocalSnapshotAsync(cancellationToken);
    }

    private async Task RunDisplayLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(DisplayInterval, timeProvider, cancellationToken);
                EmitCurrentSnapshot();
                await PublishLocalSnapshotAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishLocalSnapshotAsync(CancellationToken cancellationToken)
    {
        RadminLyricsFrame? frame = null;
        lock (sync)
        {
            if (currentLocal is null)
            {
                return;
            }

            var now = timeProvider.GetUtcNow();
            var snapshot = coordinator.SnapshotAt(now);
            if (snapshot.Source != LyricSource.LocalNetEase)
            {
                return;
            }

            frame = new RadminLyricsFrame(
                RadminLyricsProtocol.CurrentVersion,
                sessionId,
                Interlocked.Increment(ref sequence),
                currentLocal.TrackId.Length > 0 ? currentLocal.TrackId : null,
                snapshot.Mode,
                snapshot.LyricLine,
                currentLocal.Media.PositionAt(now),
                now,
                currentLocal.Media.IsPlaying);
        }

        await relay.PublishAsync(frame, cancellationToken);
    }

    private void Relay_OnFrameReceived(object? sender, RadminLyricsFrame frame)
    {
        lock (sync)
        {
            coordinator.SetRemote(new RemoteLyricsPlayback(
                frame.SessionId,
                frame.Sequence,
                frame.Mode,
                frame.LyricLine,
                timeProvider.GetUtcNow()));
        }

        EmitCurrentSnapshot();
    }

    private void Relay_OnConnectionClosed(object? sender, EventArgs eventArgs)
    {
        lock (sync)
        {
            coordinator.ClearRemote();
        }

        EmitCurrentSnapshot();
    }

    private void EmitCurrentSnapshot()
    {
        IslandSnapshot snapshot;
        lock (sync)
        {
            snapshot = coordinator.SnapshotAt(timeProvider.GetUtcNow());
            if (snapshot == lastSnapshot)
            {
                return;
            }

            lastSnapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void CancelResolution()
    {
        var cancellation = Interlocked.Exchange(ref resolutionCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        resolutionTask = null;
    }

    private static string CreateTrackKey(MediaPlaybackSnapshot snapshot) =>
        string.Join('\u001f', new[] { snapshot.Title.Trim() }.Concat(snapshot.Artists.Select(artist => artist.Trim())));

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        runtimeCancellation?.Cancel();
        var cancellation = Interlocked.Exchange(ref resolutionCancellation, null);
        cancellation?.Cancel();
        await IgnoreCancellationAsync(mediaTask);
        await IgnoreCancellationAsync(displayTask);
        await IgnoreCancellationAsync(resolutionTask);
        relay.FrameReceived -= Relay_OnFrameReceived;
        relay.ConnectionClosed -= Relay_OnConnectionClosed;
        await relay.DisposeAsync();
        cancellation?.Dispose();
        runtimeCancellation?.Dispose();
        ownedHttpClient?.Dispose();
    }

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
