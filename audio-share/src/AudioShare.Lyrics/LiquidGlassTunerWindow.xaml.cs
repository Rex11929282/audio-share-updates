using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioShare.Lyrics;

public partial class LiquidGlassTunerWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly LiquidGlassSettingsController controller;
    private bool closeCommitted;
    private bool webViewReady;

    internal LiquidGlassTunerWindow(LiquidGlassSettingsController controller)
    {
        this.controller = controller;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        controller.SettingsChanged += Controller_OnSettingsChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await TunerWebView.EnsureCoreWebView2Async();
            var settings = TunerWebView.CoreWebView2.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            TunerWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(webRoot, "index.html")))
            {
                throw new FileNotFoundException("FlowCast Lyrics web assets were not found.", webRoot);
            }

            TunerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "flowcast.local",
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            TunerWebView.Source = new Uri("https://flowcast.local/index.html?surface=tuner");
        }
        catch (Exception)
        {
            TunerStatus.Text = "Unable to load the Liquid Glass tuner.";
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            var commandType = GetCommandType(root);
            if (commandType is null)
            {
                return;
            }

            switch (commandType)
            {
                case "tuner-ready":
                    webViewReady = true;
                    TunerWebView.Visibility = Visibility.Visible;
                    TunerStatus.Visibility = Visibility.Collapsed;
                    PostSettings();
                    break;
                case "liquid-preview":
                    if (root.TryGetProperty("settings", out var preview) &&
                        preview.Deserialize<LiquidGlassSettings>(JsonOptions) is { IsValid: true } settings)
                    {
                        controller.Preview(settings);
                    }
                    break;
                case "liquid-reset":
                    controller.Reset();
                    PostSettings();
                    break;
                case "liquid-cancel":
                    controller.Cancel();
                    closeCommitted = true;
                    Close();
                    break;
                case "liquid-save":
                    try
                    {
                        controller.Save();
                    }
                    catch (IOException)
                    {
                        ShowSaveFailure();
                        break;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        ShowSaveFailure();
                        break;
                    }

                    closeCommitted = true;
                    Close();
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    internal static string? GetCommandType(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return type.GetString();
    }

    private void ShowSaveFailure()
    {
        MessageBox.Show(
            this,
            "Unable to save Liquid Glass settings. Check access to local app data and try again.",
            "FlowCast Lyrics",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void Controller_OnSettingsChanged(object? sender, LiquidGlassSettings settings)
    {
        _ = Dispatcher.BeginInvoke(PostSettings);
    }

    private void PostSettings()
    {
        if (webViewReady && TunerWebView.CoreWebView2 is not null)
        {
            TunerWebView.CoreWebView2.PostWebMessageAsJson(controller.GetSettingsJson());
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!closeCommitted)
        {
            controller.Cancel();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        controller.SettingsChanged -= Controller_OnSettingsChanged;
        if (TunerWebView.CoreWebView2 is not null)
        {
            TunerWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        TunerWebView.Dispose();
    }
}
