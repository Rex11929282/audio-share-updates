using System.Windows;
using AudioShare.Windows;

namespace AudioShare.Lyrics;

public partial class MainWindow : Window
{
    private RadminLyricsReceiver? receiver;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += ConnectToFlowCastAsync;
        Closed += (_, _) => receiver?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async void ConnectToFlowCastAsync(object sender, RoutedEventArgs e)
    {
        receiver = new RadminLyricsReceiver();
        statusText.Text = "Looking for FlowCast...";
        try
        {
            statusText.Text = await receiver.DiscoverAndConnectOnRadminAsync()
                ? "Connected to FlowCast through Radmin VPN."
                : "Radmin VPN is not available on this computer.";
        }
        catch (Exception)
        {
            statusText.Text = "No FlowCast host was found on this Radmin VPN network.";
        }
    }
}
