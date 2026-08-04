using System.IO;
using System.Net.Sockets;
using AudioShare.Lyrics.Contracts;
using AudioShare.Windows;

namespace AudioShare.Lyrics;

internal interface ILyricsRelay : IAsyncDisposable
{
    event EventHandler<RadminLyricsFrame>? FrameReceived;

    event EventHandler? Connected;

    event EventHandler? ConnectionClosed;

    Task StartReceivingAsync(CancellationToken cancellationToken);

    Task PublishAsync(RadminLyricsFrame frame, CancellationToken cancellationToken);

    Task StopPublishingAsync(CancellationToken cancellationToken);
}

internal interface ILyricsRelayReceiver : IAsyncDisposable
{
    event EventHandler<RadminLyricsFrame>? FrameReceived;

    event EventHandler? ConnectionClosed;

    Task<bool> DiscoverAndConnectOnRadminAsync(CancellationToken cancellationToken);
}

internal sealed class RadminLyricsReceiverAdapter : ILyricsRelayReceiver
{
    private readonly RadminLyricsReceiver receiver;

    internal RadminLyricsReceiverAdapter(string instanceId) => receiver = new RadminLyricsReceiver(instanceId);

    public event EventHandler<RadminLyricsFrame>? FrameReceived
    {
        add => receiver.FrameReceived += value;
        remove => receiver.FrameReceived -= value;
    }

    public event EventHandler? ConnectionClosed
    {
        add => receiver.ConnectionClosed += value;
        remove => receiver.ConnectionClosed -= value;
    }

    public Task<bool> DiscoverAndConnectOnRadminAsync(CancellationToken cancellationToken) =>
        receiver.DiscoverAndConnectOnRadminAsync(cancellationToken);

    public ValueTask DisposeAsync() => receiver.DisposeAsync();
}

internal sealed class RadminLyricsRelay : ILyricsRelay
{
    private readonly string instanceId;
    private readonly ILyricsRelayReceiver receiver;
    private readonly Func<bool> hasActiveAdapter;
    private readonly Func<CancellationToken, Task> delayBeforeRetry;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim hostGate = new(1, 1);
    private CancellationTokenSource? receiveCancellation;
    private RadminLyricsHost? host;
    private Task? receiveTask;
    private bool disposed;

    public RadminLyricsRelay(string instanceId)
        : this(
            instanceId,
            CreateReceiver(instanceId),
            static () => RadminAdapterSelector.SelectActiveAdapter() is not null,
            static cancellationToken => DelayBeforeRetryAsync(TimeSpan.FromSeconds(1), cancellationToken))
    {
    }

    internal RadminLyricsRelay(
        string instanceId,
        ILyricsRelayReceiver receiver,
        Func<bool> hasActiveAdapter,
        Func<CancellationToken, Task> delayBeforeRetry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(hasActiveAdapter);
        ArgumentNullException.ThrowIfNull(delayBeforeRetry);
        this.instanceId = instanceId;
        this.receiver = receiver;
        this.hasActiveAdapter = hasActiveAdapter;
        this.delayBeforeRetry = delayBeforeRetry;
        receiver.FrameReceived += Receiver_OnFrameReceived;
    }

    public event EventHandler<RadminLyricsFrame>? FrameReceived;

    public event EventHandler? Connected;

    public event EventHandler? ConnectionClosed;

    public Task StartReceivingAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (receiveTask is not null)
        {
            return Task.CompletedTask;
        }

        receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeCancellation.Token,
            cancellationToken);
        receiveTask = ReceiveLoopAsync(receiveCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task PublishAsync(RadminLyricsFrame frame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await hostGate.WaitAsync(cancellationToken);
        try
        {
            host ??= await TryStartHostAsync();
            if (host is not null)
            {
                await host.PublishAsync(frame, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            if (host is not null)
            {
                await host.DisposeAsync();
                host = null;
            }
        }
        finally
        {
            hostGate.Release();
        }
    }

    public async Task StopPublishingAsync(CancellationToken cancellationToken)
    {
        await hostGate.WaitAsync(cancellationToken);
        try
        {
            if (host is not null)
            {
                await host.DisposeAsync();
                host = null;
            }
        }
        finally
        {
            hostGate.Release();
        }
    }

    private async Task<RadminLyricsHost?> TryStartHostAsync()
    {
        var address = RadminAdapterSelector.SelectActiveAddress();
        if (address is null)
        {
            return null;
        }

        RadminLyricsHost? nextHost = null;
        try
        {
            nextHost = new RadminLyricsHost(address, port: 0, instanceId);
            await nextHost.StartAsync();
            return nextHost;
        }
        catch (SocketException)
        {
            if (nextHost is not null)
            {
                await nextHost.DisposeAsync();
            }

            return null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!hasActiveAdapter())
            {
                await delayBeforeRetry(cancellationToken);
                continue;
            }

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var connectionGate = new object();
            var closeObserved = false;
            var publishingConnected = false;
            var connectedPublished = false;
            void OnConnectionClosed(object? sender, EventArgs eventArgs)
            {
                bool publishClosed;
                lock (connectionGate)
                {
                    closeObserved = true;
                    publishClosed = connectedPublished && !publishingConnected;
                }

                if (publishClosed)
                {
                    ConnectionClosed?.Invoke(this, EventArgs.Empty);
                }

                disconnected.TrySetResult();
            }

            receiver.ConnectionClosed += OnConnectionClosed;
            try
            {
                if (await receiver.DiscoverAndConnectOnRadminAsync(cancellationToken))
                {
                    bool publishConnected;
                    lock (connectionGate)
                    {
                        publishConnected = !closeObserved;
                        publishingConnected = publishConnected;
                    }

                    if (publishConnected)
                    {
                        Connected?.Invoke(this, EventArgs.Empty);
                        bool publishDeferredClose;
                        lock (connectionGate)
                        {
                            publishingConnected = false;
                            connectedPublished = true;
                            publishDeferredClose = closeObserved;
                        }

                        if (publishDeferredClose)
                        {
                            ConnectionClosed?.Invoke(this, EventArgs.Empty);
                        }

                        await disconnected.Task.WaitAsync(cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
            }
            finally
            {
                receiver.ConnectionClosed -= OnConnectionClosed;
            }

            await delayBeforeRetry(cancellationToken);
        }
    }

    private static async Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Receiver_OnFrameReceived(object? sender, RadminLyricsFrame frame) =>
        FrameReceived?.Invoke(this, frame);

    private static ILyricsRelayReceiver CreateReceiver(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return new RadminLyricsReceiverAdapter(instanceId);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        receiveCancellation?.Cancel();
        await receiver.DisposeAsync();
        if (receiveTask is not null)
        {
            try
            {
                await receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await StopPublishingAfterDisposeAsync();
        receiver.FrameReceived -= Receiver_OnFrameReceived;
        receiveCancellation?.Dispose();
        lifetimeCancellation.Dispose();
        hostGate.Dispose();
    }

    private async Task StopPublishingAfterDisposeAsync()
    {
        await hostGate.WaitAsync();
        try
        {
            if (host is not null)
            {
                await host.DisposeAsync();
                host = null;
            }
        }
        finally
        {
            hostGate.Release();
        }
    }
}
