namespace AudioShare.Lyrics;

internal enum LyricsConnectionState
{
    FindingFlowcast,
    ConnectedAwaitingLyrics
}

internal sealed class LyricsConnectionRuntime : IAsyncDisposable
{
    private readonly ILyricsRelay relay;
    private int started;
    private int disposed;

    internal LyricsConnectionRuntime(ILyricsRelay relay)
    {
        ArgumentNullException.ThrowIfNull(relay);
        this.relay = relay;
        relay.Connected += Relay_OnConnected;
        relay.ConnectionClosed += Relay_OnConnectionClosed;
    }

    internal event EventHandler<LyricsConnectionState>? ConnectionStateChanged;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
        {
            return;
        }

        ConnectionStateChanged?.Invoke(this, LyricsConnectionState.FindingFlowcast);
        try
        {
            await relay.StartReceivingAsync(cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref started, 0);
            throw;
        }
    }

    private void Relay_OnConnected(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            ConnectionStateChanged?.Invoke(this, LyricsConnectionState.ConnectedAwaitingLyrics);
        }
    }

    private void Relay_OnConnectionClosed(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            ConnectionStateChanged?.Invoke(this, LyricsConnectionState.FindingFlowcast);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        relay.Connected -= Relay_OnConnected;
        relay.ConnectionClosed -= Relay_OnConnectionClosed;
        await relay.DisposeAsync();
    }
}
