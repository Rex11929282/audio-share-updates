using System.Net;
using System.Net.NetworkInformation;
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
        var hostAddress = RadminAdapterSelector.SelectActiveAddress() ?? IPAddress.Loopback;
        var discoveryAddress = hostAddress.Equals(IPAddress.Loopback)
            ? IPAddress.Loopback
            : IPAddress.Parse("26.255.255.255");
        await using var host = new RadminLyricsHost(hostAddress, port: 0);
        await host.StartAsync();

        await using var receiver = new RadminLyricsReceiver();
        var connected = await receiver.DiscoverAndConnectAsync(discoveryAddress);

        Assert.True(connected);
        var peer = await host.WaitForReceiverAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(peer);
        Assert.Equal("FlowCast Lyrics", peer.Name);
    }

    [Fact]
    public async Task Receiver_RetriesDiscoveryWhenTheHostStartsLater()
    {
        var hostAddress = RadminAdapterSelector.SelectActiveAddress() ?? IPAddress.Loopback;
        var discoveryAddress = hostAddress.Equals(IPAddress.Loopback)
            ? IPAddress.Loopback
            : IPAddress.Parse("26.255.255.255");
        await using var receiver = new RadminLyricsReceiver();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var connecting = receiver.DiscoverAndConnectAsync(discoveryAddress, cancellation.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(900));
        await using var host = new RadminLyricsHost(hostAddress, port: 0);
        await host.StartAsync();

        Assert.True(await connecting);
    }
}
