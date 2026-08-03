using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AudioShare.Lyrics.Contracts;
using AudioShare.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioShare.Lyrics;

public partial class MainWindow : Window
{
    private static readonly Duration IslandAnimationDuration = new(TimeSpan.FromMilliseconds(320));
    private const int MaximumBackdropRetries = 2;
    private readonly LyricsOverlayPresenter presenter = new();
    private readonly LyricsRuntime runtime = new();
    private readonly DesktopBackdropCapture desktopBackdrop = new();
    private readonly DispatcherTimer backdropRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(380)
    };
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly LiquidGlassSettingsController liquidGlassSettings =
        new(LiquidGlassSettingsStore.CreateDefault());
    private LiquidGlassTunerWindow? tunerWindow;
    private int backdropRetryAttempts;
    private bool webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        presenter.StateChanged += Presenter_OnStateChanged;
        runtime.SnapshotChanged += Runtime_OnSnapshotChanged;
        liquidGlassSettings.SettingsChanged += LiquidGlassSettings_OnSettingsChanged;
        Loaded += MainWindow_OnLoaded;
        Closed += MainWindow_OnClosed;
        LocationChanged += (_, _) => ScheduleBackdropRefresh();
        SizeChanged += (_, _) => ScheduleBackdropRefresh();
        backdropRefreshTimer.Tick += BackdropRefreshTimer_OnTick;
        SourceInitialized += (_, _) => WindowBackdrop.TryEnableLightBackdrop(this);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var webViewInitialization = InitializeWebViewAsync();
        await runtime.StartAsync(lifetimeCancellation.Token);
        await webViewInitialization;
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

    private void Runtime_OnSnapshotChanged(object? sender, IslandSnapshot snapshot)
    {
        _ = Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(IslandSnapshot snapshot)
    {
        presenter.Show(snapshot);
        FallbackStatusText.Text = presenter.DisplayText;
        FallbackStatusText.FontSize = snapshot.Mode is IslandMode.Playing or IslandMode.Paused ? 27 : 16;

        var size = IslandDimensions.For(snapshot.Mode);
        WindowSurface.CornerRadius = new CornerRadius(size.Height / 2);
        NativeFallback.CornerRadius = new CornerRadius(Math.Max(0, (size.Height / 2) - 4));
        ResizeIsland(size);
    }

    private void ResizeIsland(IslandSize size)
    {
        var currentWidth = Width;
        var currentHeight = Height;
        if (Math.Abs(currentWidth - size.Width) < 0.1 && Math.Abs(currentHeight - size.Height) < 0.1)
        {
            return;
        }

        var centerX = Left + (currentWidth / 2);
        var centerY = Top + (currentHeight / 2);
        var workArea = GetCurrentMonitorWorkArea();
        var targetLeft = Math.Clamp(
            centerX - (size.Width / 2),
            workArea.Left,
            Math.Max(workArea.Left, workArea.Right - size.Width));
        var targetTop = Math.Clamp(
            centerY - (size.Height / 2),
            workArea.Top,
            Math.Max(workArea.Top, workArea.Bottom - size.Height));

        if (!SystemParameters.ClientAreaAnimation)
        {
            Width = size.Width;
            Height = size.Height;
            Left = targetLeft;
            Top = targetTop;
            return;
        }

        var easing = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
        AnimateTo(WidthProperty, currentWidth, size.Width, easing);
        AnimateTo(HeightProperty, currentHeight, size.Height, easing);
        AnimateTo(LeftProperty, Left, targetLeft, easing);
        AnimateTo(TopProperty, Top, targetTop, easing);
    }

    private void AnimateTo(
        DependencyProperty property,
        double from,
        double to,
        IEasingFunction easing)
    {
        SetValue(property, to);
        BeginAnimation(
            property,
            new DoubleAnimation(from, to, IslandAnimationDuration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var information = new MonitorInformation { Size = Marshal.SizeOf<MonitorInformation>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref information))
        {
            return SystemParameters.WorkArea;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            information.WorkArea.Left / dpi.DpiScaleX,
            information.WorkArea.Top / dpi.DpiScaleY,
            (information.WorkArea.Right - information.WorkArea.Left) / dpi.DpiScaleX,
            (information.WorkArea.Bottom - information.WorkArea.Top) / dpi.DpiScaleY);
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
                OverlayWebView.CoreWebView2.PostWebMessageAsJson(liquidGlassSettings.GetSettingsJson());
                RefreshDesktopBackdrop();
                OverlayWebView.Visibility = Visibility.Visible;
                NativeFallback.Visibility = Visibility.Collapsed;
            }
        }
        catch (JsonException)
        {
        }
    }

    private void LiquidGlassSettings_OnSettingsChanged(object? sender, LiquidGlassSettings settings)
    {
        if (webViewReady)
        {
            OverlayWebView.CoreWebView2.PostWebMessageAsJson(liquidGlassSettings.GetSettingsJson());
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

    private void ScheduleBackdropRefresh()
    {
        if (!webViewReady)
        {
            return;
        }

        backdropRetryAttempts = 0;
        backdropRefreshTimer.Stop();
        backdropRefreshTimer.Start();
    }

    private void BackdropRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        backdropRefreshTimer.Stop();
        PublishDesktopBackdrop();
    }

    private void RefreshDesktopBackdrop()
    {
        backdropRetryAttempts = 0;
        PublishDesktopBackdrop();
    }

    private void ScheduleBackdropRetry()
    {
        if (backdropRetryAttempts >= MaximumBackdropRetries)
        {
            return;
        }

        backdropRetryAttempts++;
        backdropRefreshTimer.Stop();
        backdropRefreshTimer.Start();
    }

    private void PublishDesktopBackdrop()
    {
        if (!webViewReady || OverlayWebView.CoreWebView2 is null)
        {
            return;
        }

        if (!WindowBackdrop.TryExcludeFromCapture(this))
        {
            ScheduleBackdropRetry();
            return;
        }

        try
        {
            var dataUrl = desktopBackdrop.Capture(this);
            if (dataUrl is null)
            {
                ScheduleBackdropRetry();
                return;
            }

            var message = JsonSerializer.Serialize(new { type = "backdrop", dataUrl });
            OverlayWebView.CoreWebView2.PostWebMessageAsJson(message);
        }
        finally
        {
            WindowBackdrop.TryAllowCapture(this);
        }
    }

    private void ShowNativeFallback()
    {
        webViewReady = false;
        OverlayWebView.Visibility = Visibility.Hidden;
        NativeFallback.Visibility = Visibility.Visible;
        FallbackStatusText.Text = presenter.DisplayText;
    }

    private void WindowSurface_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            ScheduleBackdropRefresh();
        }
    }

    private void WindowSurface_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (tunerWindow is { IsVisible: true })
        {
            tunerWindow.Activate();
            return;
        }

        tunerWindow = new LiquidGlassTunerWindow(liquidGlassSettings);
        tunerWindow.Closed += TunerWindow_OnClosed;
        tunerWindow.Show();
    }

    private void TunerWindow_OnClosed(object? sender, EventArgs e)
    {
        if (sender is LiquidGlassTunerWindow closedTuner)
        {
            closedTuner.Closed -= TunerWindow_OnClosed;
            if (ReferenceEquals(tunerWindow, closedTuner))
            {
                tunerWindow = null;
            }
        }
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        var tuner = tunerWindow;
        tunerWindow = null;
        if (tuner is not null)
        {
            tuner.Closed -= TunerWindow_OnClosed;
            tuner.Close();
        }

        lifetimeCancellation.Cancel();
        backdropRefreshTimer.Stop();
        backdropRefreshTimer.Tick -= BackdropRefreshTimer_OnTick;
        presenter.StateChanged -= Presenter_OnStateChanged;
        runtime.SnapshotChanged -= Runtime_OnSnapshotChanged;
        liquidGlassSettings.SettingsChanged -= LiquidGlassSettings_OnSettingsChanged;
        if (OverlayWebView.CoreWebView2 is not null)
        {
            OverlayWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_OnWebMessageReceived;
        }

        OverlayWebView.Dispose();
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        lifetimeCancellation.Dispose();
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInformation
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle WorkArea;
        public uint Flags;
    }
}
