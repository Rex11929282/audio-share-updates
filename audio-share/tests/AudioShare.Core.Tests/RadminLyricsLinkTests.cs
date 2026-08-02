using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using AudioShare.Lyrics.Contracts;
using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class RadminLyricsLinkTests
{
    [Fact]
    public void RadminAdapterSelector_ReturnsOnlyAnActiveRadminIpv4Address()
    {
        var address = RadminAdapterSelector.Select(new[]
        {
            new RadminAdapter("Ethernet", OperationalStatus.Up, IPAddress.Parse("192.168.1.4")),
            new RadminAdapter("Radmin VPN", OperationalStatus.Down, IPAddress.Parse("26.1.2.3")),
            new RadminAdapter("Radmin VPN", OperationalStatus.Up, IPAddress.Parse("26.233.18.34"))
        });

        Assert.Equal(IPAddress.Parse("26.233.18.34"), address);
    }

    [Fact]
    public void RadminAdapterSelector_UsesTheAdapterPrefixForDiscoveryBroadcast()
    {
        var broadcast = RadminAdapterSelector.GetDiscoveryBroadcastAddress(
            new RadminAdapter("Radmin VPN", OperationalStatus.Up, IPAddress.Parse("26.233.18.34"), PrefixLength: 8));

        Assert.Equal(IPAddress.Parse("26.255.255.255"), broadcast);
    }

    [Fact]
    public async Task HostAndReceiver_DiscoverAndConnectWithoutManuallyEnteringAnAddress()
    {
        var hostAddress = IPAddress.Loopback;
        var discoveryPort = GetAvailableUdpPort();
        await using var host = new RadminLyricsHost(hostAddress, port: 0, discoveryPort: discoveryPort);
        await host.StartAsync();

        await using var receiver = new RadminLyricsReceiver();
        var connected = await receiver.DiscoverAndConnectAsync(hostAddress, discoveryPort);

        Assert.True(connected);
        var peer = await host.WaitForReceiverAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(peer);
        Assert.Equal("FlowCast Lyrics", peer.Name);
    }

    [Fact]
    public async Task Receiver_RetriesDiscoveryWhenTheHostStartsLater()
    {
        var hostAddress = IPAddress.Loopback;
        var discoveryPort = GetAvailableUdpPort();
        await using var receiver = new RadminLyricsReceiver();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var connecting = receiver.DiscoverAndConnectAsync(hostAddress, discoveryPort, cancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(900));
        await using var host = new RadminLyricsHost(hostAddress, port: 0, discoveryPort: discoveryPort);
        await host.StartAsync();

        Assert.True(await connecting);
    }

    [Fact]
    public async Task Host_PublishesARealLyricFrameToTheConnectedReceiver()
    {
        var hostAddress = IPAddress.Loopback;
        var discoveryPort = GetAvailableUdpPort();
        await using var host = new RadminLyricsHost(
            hostAddress,
            port: 0,
            instanceId: "owner-a",
            discoveryPort: discoveryPort);
        await host.StartAsync();

        await using var receiver = new RadminLyricsReceiver(instanceId: "friend-a");
        var received = new TaskCompletionSource<RadminLyricsFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += (_, frame) => received.TrySetResult(frame);
        Assert.True(await receiver.DiscoverAndConnectAsync(hostAddress, discoveryPort));

        await host.PublishAsync(new RadminLyricsFrame(
            ProtocolVersion: 1,
            SessionId: "owner-a",
            Sequence: 1,
            TrackId: "496869422",
            Mode: IslandMode.Playing,
            LyricLine: new LyricLine("真實歌詞", 1000, 2500),
            PositionMilliseconds: 1200,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            IsPlaying: true));

        Assert.Equal(
            "真實歌詞",
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(2))).LyricLine?.Text);
    }

    [Fact]
    public async Task Receiver_IgnoresAFrameWhoseSequenceRegresses()
    {
        var hostAddress = IPAddress.Loopback;
        await using var host = new RadminLyricsHost(
            hostAddress,
            port: 0,
            instanceId: "owner-a",
            discoveryPort: GetAvailableUdpPort());
        await host.StartAsync();

        await using var receiver = new RadminLyricsReceiver(instanceId: "friend-a");
        var received = new List<RadminLyricsFrame>();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += (_, frame) =>
        {
            lock (received)
            {
                received.Add(frame);
            }

            firstFrame.TrySetResult();
        };
        Assert.True(await receiver.ConnectAsync(host.Endpoint));

        await host.PublishAsync(CreateFrame(sequence: 2, text: "最新"));
        await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await host.PublishAsync(CreateFrame(sequence: 1, text: "過期"));
        await Task.Delay(150);

        lock (received)
        {
            Assert.Single(received);
            Assert.Equal("最新", received[0].LyricLine?.Text);
        }
    }

    [Fact]
    public async Task Receiver_DoesNotDiscoverItsOwnInstance()
    {
        var hostAddress = IPAddress.Loopback;
        var discoveryPort = GetAvailableUdpPort();
        await using var host = new RadminLyricsHost(
            hostAddress,
            port: 0,
            instanceId: "same-instance",
            discoveryPort: discoveryPort);
        await host.StartAsync();
        await using var receiver = new RadminLyricsReceiver(instanceId: "same-instance");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            receiver.DiscoverAndConnectAsync(hostAddress, discoveryPort, cancellation.Token));
        Assert.Null(await host.WaitForReceiverAsync(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void Protocol_RejectsMalformedOversizedAndUnsupportedFrames()
    {
        Assert.False(RadminLyricsProtocol.TryDeserialize("{broken", out _));
        Assert.False(RadminLyricsProtocol.TryDeserialize(new string('x', 65_537), out _));

        var unsupported = JsonSerializer.Serialize(CreateFrame(sequence: 1, text: "歌詞") with
        {
            ProtocolVersion = 2
        });
        Assert.False(RadminLyricsProtocol.TryDeserialize(unsupported, out _));

        var oversizedLyric = JsonSerializer.Serialize(CreateFrame(sequence: 1, text: new string('詞', 4097)));
        Assert.False(RadminLyricsProtocol.TryDeserialize(oversizedLyric, out _));
    }

    private static int GetAvailableUdpPort()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }

    private static RadminLyricsFrame CreateFrame(long sequence, string text) =>
        new(
            ProtocolVersion: 1,
            SessionId: "owner-a",
            Sequence: sequence,
            TrackId: "496869422",
            Mode: IslandMode.Playing,
            LyricLine: new LyricLine(text, 1000, null),
            PositionMilliseconds: 1200,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            IsPlaying: true);
}
