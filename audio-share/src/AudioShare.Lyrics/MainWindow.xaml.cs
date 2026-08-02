using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using AudioShare.Lyrics.Contracts;
using AudioShare.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioShare.Lyrics;

public partial class MainWindow : Window
{
    private readonly LyricsOverlayPresenter presenter = new();
    private readonly CancellationTokenSource connectionCancellation = new();
    private RadminLyricsReceiver? receiver;
    private bool webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        presenter.StateChanged += Presenter_OnStateChanged;
        Loaded += MainWindow_OnLoaded;
        Closed += MainWindow_OnClosed;
        SourceInitialized += (_, _) => WindowBackdrop.TryEnableLightBackdrop(this);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        presenter.SetConnectionState(ConnectionState.Searching);
        var webViewInitialization = InitializeWebViewAsync();
        var connection = ConnectToFlowCastAsync(connectionCancellation.Token);
        await Task.WhenAll(webViewInitialization, connection);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await OverlayWebView.EnsureCoreWebView2Async();
            OverlayWebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            OverlayWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            OverlayWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            OverlayWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            OverlayWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            OverlayWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            OverlayWebView.CoreWebView2.WebMessageReceived += CoreWebView2_OnWebMessageReceived;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(webRoot, "index.html")))
            {
                throw new FileNotFoundException("FlowCast Lyrics web assets were not found.", webRoot);
            }

            OverlayWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "flowcast.local",
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            OverlayWebView.Source = new Uri("https://flowcast.local/index.html");
        }
        catch (Exception)
        {
            ShowNativeFallback();
        }
    }

    private async Task ConnectToFlowCastAsync(CancellationToken cancellationToken)
    {
        if (RadminAdapterSelector.SelectActiveAdapter() is null)
        {
            presenter.SetConnectionState(ConnectionState.Unavailable);
            return;
        }

        receiver = new RadminLyricsReceiver();
        try
        {
            presenter.SetConnectionState(ConnectionState.Searching);
            var connected = await receiver.DiscoverAndConnectOnRadminAsync(cancellationToken);
            presenter.SetConnectionState(connected ? ConnectionState.Connected : ConnectionState.Disconnected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            presenter.SetConnectionState(ConnectionState.Disconnected);
        }
    }

    private void CoreWebView2_OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            if (message.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ready")
            {
                webViewReady = true;
                OverlayWebView.CoreWebView2.PostWebMessageAsJson(presenter.GetSnapshotJson());
                OverlayWebView.Visibility = Visibility.Visible;
                NativeFallback.Visibility = Visibility.Collapsed;
            }
        }
        catch (JsonException)
        {
        }
    }

    private void Presenter_OnStateChanged(object? sender, string snapshotJson)
    {
        FallbackStatusText.Text = presenter.DisplayText;
        if (webViewReady)
        {
            OverlayWebView.CoreWebView2.PostWebMessageAsJson(snapshotJson);
        }
    }

    private void ShowNativeFallback()
    {
        webViewReady = false;
        OverlayWebView.Visibility = Visibility.Hidden;
        NativeFallback.Visibility = Visibility.Visible;
        FallbackStatusText.Text = presenter.DisplayText;
    }

    private void DragHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private async void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        connectionCancellation.Cancel();
        presenter.StateChanged -= Presenter_OnStateChanged;
        if (OverlayWebView.CoreWebView2 is not null)
        {
            OverlayWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_OnWebMessageReceived;
        }

        OverlayWebView.Dispose();
        if (receiver is not null)
        {
            await receiver.DisposeAsync();
        }

        connectionCancellation.Dispose();
    }
}
