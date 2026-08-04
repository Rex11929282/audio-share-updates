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

internal sealed class RadminLyricsRelay : ILyricsRelay
{
    private readonly string instanceId;
    private readonly RadminLyricsReceiver receiver;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly SemaphoreSlim hostGate = new(1, 1);
    private CancellationTokenSource? receiveCancellation;
    private RadminLyricsHost? host;
    private Task? receiveTask;
    private bool disposed;

    public RadminLyricsRelay(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        this.instanceId = instanceId;
        receiver = new RadminLyricsReceiver(instanceId);
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
            if (RadminAdapterSelector.SelectActiveAdapter() is null)
            {
                await DelayBeforeRetryAsync(cancellationToken);
                continue;
            }

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnConnectionClosed(object? sender, EventArgs eventArgs)
            {
                ConnectionClosed?.Invoke(this, EventArgs.Empty);
                disconnected.TrySetResult();
            }

            receiver.ConnectionClosed += OnConnectionClosed;
            try
            {
                if (await receiver.DiscoverAndConnectOnRadminAsync(cancellationToken))
                {
                    Connected?.Invoke(this, EventArgs.Empty);
                    await disconnected.Task.WaitAsync(cancellationToken);
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

            await DelayBeforeRetryAsync(cancellationToken);
        }
    }

    private static async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Receiver_OnFrameReceived(object? sender, RadminLyricsFrame frame) =>
        FrameReceived?.Invoke(this, frame);

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
