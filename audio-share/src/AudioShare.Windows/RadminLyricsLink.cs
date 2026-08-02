using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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

public sealed record RadminLyricsPeer(string Name, IPEndPoint Endpoint);

public sealed class RadminLyricsHost : IAsyncDisposable
{
    internal const int DiscoveryPort = 48921;
    private const string DiscoverMessage = "flowcast-lyrics/discover";
    private readonly TcpListener listener;
    private readonly UdpClient discoveryListener;
    private readonly TaskCompletionSource<RadminLyricsPeer> receiverConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource cancellation = new();
    private Task? acceptLoop;
    private Task? discoveryLoop;

    public RadminLyricsHost(IPAddress address, int port)
    {
        listener = new TcpListener(address, port);
        discoveryListener = new UdpClient(new IPEndPoint(address, DiscoveryPort));
    }

    public IPEndPoint Endpoint => (IPEndPoint)listener.LocalEndpoint;

    public Task StartAsync()
    {
        listener.Start();
        acceptLoop = AcceptLoopAsync(cancellation.Token);
        discoveryLoop = DiscoveryLoopAsync(cancellation.Token);
        return Task.CompletedTask;
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
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                var hello = await reader.ReadLineAsync(cancellationToken);
                if (hello is null)
                {
                    continue;
                }

                var request = JsonSerializer.Deserialize<ReceiverHello>(hello);
                if (string.IsNullOrWhiteSpace(request?.Name))
                {
                    continue;
                }

                var endpoint = (IPEndPoint)client.Client.RemoteEndPoint!;
                receiverConnected.TrySetResult(new RadminLyricsPeer(request.Name, endpoint));
                await writer.WriteLineAsync("flowcast-lyrics/connected");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var request = await discoveryListener.ReceiveAsync(cancellationToken);
                if (Encoding.UTF8.GetString(request.Buffer) != DiscoverMessage)
                {
                    continue;
                }

                var response = JsonSerializer.Serialize(new DiscoveryResponse(Endpoint.Address.ToString(), Endpoint.Port));
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await discoveryListener.SendAsync(responseBytes, request.RemoteEndPoint, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        listener.Stop();
        discoveryListener.Dispose();
        await IgnoreCancellationAsync(acceptLoop);
        await IgnoreCancellationAsync(discoveryLoop);
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

    private sealed record ReceiverHello(string Name);
    private sealed record DiscoveryResponse(string Address, int Port);
}

public sealed class RadminLyricsReceiver : IAsyncDisposable
{
    private const string DiscoverMessage = "flowcast-lyrics/discover";
    private TcpClient? client;

    public async Task<bool> DiscoverAndConnectAsync(IPAddress discoveryAddress, CancellationToken cancellationToken = default)
    {
        using var discoveryClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        discoveryClient.EnableBroadcast = true;
        var request = Encoding.UTF8.GetBytes(DiscoverMessage);
        var endpoint = new IPEndPoint(discoveryAddress, RadminLyricsHost.DiscoveryPort);
        while (true)
        {
            await discoveryClient.SendAsync(request, endpoint, cancellationToken);
            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(TimeSpan.FromSeconds(1));

            try
            {
                var response = await discoveryClient.ReceiveAsync(receiveTimeout.Token);
                var host = JsonSerializer.Deserialize<DiscoveryResponse>(Encoding.UTF8.GetString(response.Buffer));
                return host is not null && await ConnectAsync(new IPEndPoint(IPAddress.Parse(host.Address), host.Port), cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The host may start after the receiver. Keep searching until the caller cancels.
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

    public async Task<bool> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        client = new TcpClient(endpoint.AddressFamily);
        await client.ConnectAsync(endpoint, cancellationToken);
        var stream = client.GetStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new ReceiverHello("FlowCast Lyrics")));
        return await reader.ReadLineAsync(cancellationToken) == "flowcast-lyrics/connected";
    }

    public ValueTask DisposeAsync()
    {
        client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record ReceiverHello(string Name);
    private sealed record DiscoveryResponse(string Address, int Port);
}
