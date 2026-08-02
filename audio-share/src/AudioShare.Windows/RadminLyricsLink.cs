using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Windows;

public sealed record RadminAdapter(string Name, OperationalStatus Status, IPAddress Address, int PrefixLength = 24);

public static class RadminAdapterSelector
{
    public static IPAddress? Select(IEnumerable<RadminAdapter> adapters) => adapters
        .FirstOrDefault(adapter =>
            adapter.Status == OperationalStatus.Up &&
            adapter.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase) &&
            adapter.Address.AddressFamily == AddressFamily.InterNetwork &&
            adapter.Address.GetAddressBytes()[0] == 26)
        ?.Address;

    public static IPAddress? SelectActiveAddress() => SelectActiveAdapter()?.Address;

    public static RadminAdapter? SelectActiveAdapter() =>
        GetActiveAdapters().FirstOrDefault(adapter =>
            adapter.Status == OperationalStatus.Up &&
            adapter.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase) &&
            adapter.Address.AddressFamily == AddressFamily.InterNetwork &&
            adapter.Address.GetAddressBytes()[0] == 26);

    public static IPAddress GetDiscoveryBroadcastAddress(RadminAdapter adapter)
    {
        var addressBytes = adapter.Address.GetAddressBytes();
        if (addressBytes.Length != 4 || adapter.PrefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(adapter));
        }

        var broadcast = new byte[4];
        for (var index = 0; index < broadcast.Length; index++)
        {
            var remainingPrefixBits = Math.Clamp(adapter.PrefixLength - (index * 8), 0, 8);
            var mask = (byte)(0xFF << (8 - remainingPrefixBits));
            broadcast[index] = (byte)((addressBytes[index] & mask) | ~mask);
        }

        return new IPAddress(broadcast);
    }

    private static IEnumerable<RadminAdapter> GetActiveAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces().SelectMany(networkInterface =>
            networkInterface.GetIPProperties().UnicastAddresses.Select(unicast =>
                new RadminAdapter(networkInterface.Name, networkInterface.OperationalStatus, unicast.Address, unicast.PrefixLength)));
}

public static class RadminLyricsProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumFrameCharacters = 65_536;
    private const int MaximumLyricCharacters = 4_096;

    public static bool TryDeserialize(string json, out RadminLyricsFrame frame)
    {
        frame = null!;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumFrameCharacters)
        {
            return false;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<RadminLyricsFrame>(json, RadminLyricsJson.Options);
            if (!IsValid(candidate))
            {
                return false;
            }

            frame = candidate!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static string Serialize(RadminLyricsFrame frame)
    {
        if (!IsValid(frame))
        {
            throw new ArgumentException("The Radmin lyric frame is invalid.", nameof(frame));
        }

        var json = JsonSerializer.Serialize(frame, RadminLyricsJson.Options);
        if (json.Length > MaximumFrameCharacters)
        {
            throw new ArgumentException("The Radmin lyric frame is too large.", nameof(frame));
        }

        return json;
    }

    internal static async Task<string?> ReadLineLimitedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[1];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return builder.Length == 0 ? null : builder.ToString().TrimEnd('\r');
            }

            if (buffer[0] == '\n')
            {
                return builder.ToString().TrimEnd('\r');
            }

            if (builder.Length >= MaximumFrameCharacters)
            {
                throw new InvalidDataException("The Radmin lyric frame exceeded the size limit.");
            }

            builder.Append(buffer[0]);
        }
    }

    private static bool IsValid(RadminLyricsFrame? frame)
    {
        if (frame is null ||
            frame.ProtocolVersion != CurrentVersion ||
            string.IsNullOrWhiteSpace(frame.SessionId) ||
            frame.SessionId.Length > 128 ||
            frame.Sequence <= 0 ||
            frame.PositionMilliseconds < 0 ||
            !Enum.IsDefined(frame.Mode))
        {
            return false;
        }

        if (frame.TrackId is not null &&
            (frame.TrackId.Length is 0 or > 32 || !frame.TrackId.All(char.IsAsciiDigit)))
        {
            return false;
        }

        return frame.LyricLine is null ||
               (!string.IsNullOrWhiteSpace(frame.LyricLine.Text) &&
                frame.LyricLine.Text.Length <= MaximumLyricCharacters &&
                frame.LyricLine.StartTimeMilliseconds >= 0 &&
                (frame.LyricLine.EndTimeMilliseconds is null ||
                 frame.LyricLine.EndTimeMilliseconds >= frame.LyricLine.StartTimeMilliseconds));
    }
}

public sealed record RadminLyricsPeer(string Name, IPEndPoint Endpoint);

public sealed class RadminLyricsHost : IAsyncDisposable
{
    internal const int DiscoveryPort = 48921;
    private const string DiscoveryProtocol = "flowcast-lyrics/discover-v1";
    private const string ConnectedMessage = "flowcast-lyrics/connected-v1";
    private readonly string instanceId;
    private readonly TcpListener listener;
    private readonly UdpClient discoveryListener;
    private readonly ConcurrentDictionary<int, ClientConnection> clients = new();
    private readonly ConcurrentDictionary<int, Task> clientTasks = new();
    private readonly TaskCompletionSource<RadminLyricsPeer> receiverConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource cancellation = new();
    private Task? acceptLoop;
    private Task? discoveryLoop;
    private int nextClientId;

    public RadminLyricsHost(
        IPAddress address,
        int port,
        string? instanceId = null,
        int discoveryPort = DiscoveryPort)
    {
        this.instanceId = string.IsNullOrWhiteSpace(instanceId)
            ? Guid.NewGuid().ToString("N")
            : instanceId;
        listener = new TcpListener(address, port);
        discoveryListener = new UdpClient(new IPEndPoint(address, discoveryPort));
    }

    public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;

    public Task StartAsync()
    {
        if (acceptLoop is not null)
        {
            return Task.CompletedTask;
        }

        listener.Start();
        acceptLoop = AcceptLoopAsync(cancellation.Token);
        discoveryLoop = DiscoveryLoopAsync(cancellation.Token);
        return Task.CompletedTask;
    }

    public async Task PublishAsync(
        RadminLyricsFrame frame,
        CancellationToken cancellationToken = default)
    {
        var json = RadminLyricsProtocol.Serialize(frame);
        foreach (var pair in clients.ToArray())
        {
            try
            {
                await pair.Value.SendAsync(json, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or SocketException)
            {
                if (clients.TryRemove(pair.Key, out var connection))
                {
                    await connection.DisposeAsync();
                }
            }
        }
    }

    public async Task<RadminLyricsPeer?> WaitForReceiverAsync(TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        try
        {
            return await receiverConnected.Task.WaitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                var clientId = Interlocked.Increment(ref nextClientId);
                var task = HandleClientAsync(clientId, client, cancellationToken);
                clientTasks[clientId] = task;
                _ = task.ContinueWith(
                    completed => clientTasks.TryRemove(clientId, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(int clientId, TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
            {
                var helloJson = await RadminLyricsProtocol.ReadLineLimitedAsync(reader, cancellationToken);
                var hello = string.IsNullOrWhiteSpace(helloJson)
                    ? null
                    : JsonSerializer.Deserialize<ReceiverHello>(helloJson, RadminLyricsJson.Options);
                if (hello is null ||
                    hello.Protocol != DiscoveryProtocol ||
                    string.IsNullOrWhiteSpace(hello.Name) ||
                    string.Equals(hello.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return;
                }

                var connection = new ClientConnection(client, writer);
                clients[clientId] = connection;
                var endpoint = (IPEndPoint)client.Client.RemoteEndPoint!;
                receiverConnected.TrySetResult(new RadminLyricsPeer(hello.Name, endpoint));
                await writer.WriteLineAsync(ConnectedMessage);

                while (await reader.ReadLineAsync(cancellationToken) is not null)
                {
                }
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException or JsonException)
        {
        }
        finally
        {
            clients.TryRemove(clientId, out _);
        }
    }

    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await discoveryListener.ReceiveAsync(cancellationToken);
                DiscoveryRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<DiscoveryRequest>(packet.Buffer, RadminLyricsJson.Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (request is null ||
                    request.Protocol != DiscoveryProtocol ||
                    string.IsNullOrWhiteSpace(request.InstanceId) ||
                    string.Equals(request.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    continue;
                }

                var response = JsonSerializer.SerializeToUtf8Bytes(
                    new DiscoveryResponse(DiscoveryProtocol, instanceId, Endpoint.Address.ToString(), Endpoint.Port),
                    RadminLyricsJson.Options);
                await discoveryListener.SendAsync(response, packet.RemoteEndPoint, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        listener.Stop();
        discoveryListener.Dispose();

        foreach (var connection in clients.Values)
        {
            await connection.DisposeAsync();
        }

        clients.Clear();
        await IgnoreCancellationAsync(acceptLoop);
        await IgnoreCancellationAsync(discoveryLoop);
        await Task.WhenAll(clientTasks.Values);
        cancellation.Dispose();
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

    private sealed class ClientConnection(TcpClient client, StreamWriter writer) : IAsyncDisposable
    {
        private readonly SemaphoreSlim writeGate = new(1, 1);

        public async Task SendAsync(string json, CancellationToken cancellationToken)
        {
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }
        }

        public ValueTask DisposeAsync()
        {
            client.Dispose();
            writeGate.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ReceiverHello(string Protocol, string InstanceId, string Name);
    private sealed record DiscoveryRequest(string Protocol, string InstanceId);
    private sealed record DiscoveryResponse(string Protocol, string InstanceId, string Address, int Port);
}

public sealed class RadminLyricsReceiver : IAsyncDisposable
{
    private const string DiscoveryProtocol = "flowcast-lyrics/discover-v1";
    private const string ConnectedMessage = "flowcast-lyrics/connected-v1";
    private readonly string instanceId;
    private TcpClient? client;
    private StreamReader? reader;
    private StreamWriter? writer;
    private CancellationTokenSource? connectionCancellation;
    private Task? receiveLoop;
    private string? currentSessionId;
    private long lastSequence;
    private bool disposed;

    public RadminLyricsReceiver(string? instanceId = null)
    {
        this.instanceId = string.IsNullOrWhiteSpace(instanceId)
            ? Guid.NewGuid().ToString("N")
            : instanceId;
    }

    public event EventHandler<RadminLyricsFrame>? FrameReceived;

    public event EventHandler? ConnectionClosed;

    public async Task<bool> DiscoverAndConnectAsync(
        IPAddress discoveryAddress,
        CancellationToken cancellationToken = default)
    {
        return await DiscoverAndConnectAsync(discoveryAddress, RadminLyricsHost.DiscoveryPort, cancellationToken);
    }

    public async Task<bool> DiscoverAndConnectAsync(
        IPAddress discoveryAddress,
        int discoveryPort,
        CancellationToken cancellationToken = default)
    {
        using var discoveryClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        discoveryClient.EnableBroadcast = true;
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new DiscoveryRequest(DiscoveryProtocol, instanceId),
            RadminLyricsJson.Options);
        var endpoint = new IPEndPoint(discoveryAddress, discoveryPort);
        while (true)
        {
            await discoveryClient.SendAsync(request, endpoint, cancellationToken);
            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(1));

            try
            {
                var packet = await discoveryClient.ReceiveAsync(receiveTimeout.Token);
                var host = JsonSerializer.Deserialize<DiscoveryResponse>(packet.Buffer, RadminLyricsJson.Options);
                if (host is null ||
                    host.Protocol != DiscoveryProtocol ||
                    string.IsNullOrWhiteSpace(host.InstanceId) ||
                    string.Equals(host.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    continue;
                }

                return await ConnectAsync(
                    new IPEndPoint(IPAddress.Parse(host.Address), host.Port),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionReset)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
            catch (JsonException)
            {
            }
        }
    }

    public async Task<bool> DiscoverAndConnectOnRadminAsync(CancellationToken cancellationToken = default)
    {
        var radminAdapter = RadminAdapterSelector.SelectActiveAdapter();
        if (radminAdapter is null)
        {
            return false;
        }

        return await DiscoverAndConnectAsync(
            RadminAdapterSelector.GetDiscoveryBroadcastAddress(radminAdapter),
            cancellationToken);
    }

    public async Task<bool> ConnectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await CloseConnectionAsync();

        var nextClient = new TcpClient(endpoint.AddressFamily);
        try
        {
            await nextClient.ConnectAsync(endpoint, cancellationToken);
            var stream = nextClient.GetStream();
            var nextReader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var nextWriter = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await nextWriter.WriteLineAsync(JsonSerializer.Serialize(
                new ReceiverHello(DiscoveryProtocol, instanceId, "FlowCast Lyrics"),
                RadminLyricsJson.Options));
            var response = await nextReader.ReadLineAsync(cancellationToken);
            if (response != ConnectedMessage)
            {
                nextReader.Dispose();
                nextWriter.Dispose();
                nextClient.Dispose();
                return false;
            }

            client = nextClient;
            reader = nextReader;
            writer = nextWriter;
            currentSessionId = null;
            lastSequence = 0;
            connectionCancellation = new CancellationTokenSource();
            receiveLoop = ReceiveLoopAsync(nextReader, connectionCancellation.Token);
            return true;
        }
        catch
        {
            nextClient.Dispose();
            throw;
        }
    }

    private async Task ReceiveLoopAsync(StreamReader streamReader, CancellationToken cancellationToken)
    {
        var closedUnexpectedly = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var json = await RadminLyricsProtocol.ReadLineLimitedAsync(streamReader, cancellationToken);
                if (json is null)
                {
                    closedUnexpectedly = true;
                    break;
                }

                if (!RadminLyricsProtocol.TryDeserialize(json, out var frame))
                {
                    continue;
                }

                if (!string.Equals(currentSessionId, frame.SessionId, StringComparison.Ordinal))
                {
                    currentSessionId = frame.SessionId;
                    lastSequence = 0;
                }

                if (frame.Sequence <= lastSequence)
                {
                    continue;
                }

                lastSequence = frame.Sequence;
                FrameReceived?.Invoke(this, frame);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or SocketException or InvalidDataException)
        {
            closedUnexpectedly = true;
        }
        finally
        {
            if (closedUnexpectedly && !disposed)
            {
                ConnectionClosed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task CloseConnectionAsync()
    {
        connectionCancellation?.Cancel();
        client?.Dispose();
        if (receiveLoop is not null)
        {
            try
            {
                await receiveLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        reader?.Dispose();
        writer?.Dispose();
        connectionCancellation?.Dispose();
        connectionCancellation = null;
        receiveLoop = null;
        reader = null;
        writer = null;
        client = null;
        currentSessionId = null;
        lastSequence = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await CloseConnectionAsync();
    }

    private sealed record ReceiverHello(string Protocol, string InstanceId, string Name);
    private sealed record DiscoveryRequest(string Protocol, string InstanceId);
    private sealed record DiscoveryResponse(string Protocol, string InstanceId, string Address, int Port);
}

internal static class RadminLyricsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
