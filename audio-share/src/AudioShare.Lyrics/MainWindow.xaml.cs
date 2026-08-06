using System.ComponentModel;
using System.Windows;

namespace AudioShare.Lyrics;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly LyricsGlassRendererSupervisor renderer;
    private readonly LyricsConnectionRuntime connectionRuntime;
    private readonly LyricsGlassHostStatePersistence hostStatePersistence;
    private readonly LyricsHostShutdownCoordinator shutdownCoordinator;
    private LyricsGlassOptionsWindow? optionsWindow;
    private bool closeCleanupStarted;
    private bool finalCloseAllowed;

    public MainWindow()
    {
        InitializeComponent();
        var settingsStore = LyricsGlassSettingsStore.CreateDefault();
        var hostState = settingsStore.Load();
        hostStatePersistence = new LyricsGlassHostStatePersistence(settingsStore, hostState);
        renderer = new LyricsGlassRendererSupervisor(hostState);
        connectionRuntime = new LyricsConnectionRuntime(new RadminLyricsRelay(Guid.NewGuid().ToString("N")));
        shutdownCoordinator = new LyricsHostShutdownCoordinator(
            renderer.StopAsync,
            () => renderer.DisposeAsync().AsTask(),
            () => connectionRuntime.DisposeAsync().AsTask(),
            LyricsDiagnosticLog.CreateDefault());

        renderer.SettingsCommitted += Renderer_OnSettingsCommitted;
        renderer.PositionChanged += Renderer_OnPositionChanged;
        renderer.CloseRequested += Renderer_OnCloseRequested;
        renderer.OptionsRequested += Renderer_OnOptionsRequested;
        connectionRuntime.ConnectionStateChanged += ConnectionRuntime_OnConnectionStateChanged;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Hide();
        try
        {
            var rendererStart = renderer.StartAsync(lifetimeCancellation.Token);
            await connectionRuntime.StartAsync(lifetimeCancellation.Token);
            await rendererStart;
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private void ConnectionRuntime_OnConnectionStateChanged(object? sender, LyricsConnectionState state)
    {
        var rendererState = state switch
        {
            LyricsConnectionState.FindingFlowcast => LyricsGlassConnectionState.FindingFlowcast,
            LyricsConnectionState.ConnectedAwaitingLyrics => LyricsGlassConnectionState.ConnectedAwaitingLyrics,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        _ = ForwardConnectionStateAsync(rendererState);
    }

    private async Task ForwardConnectionStateAsync(LyricsGlassConnectionState state)
    {
        try
        {
            await renderer.SetConnectionStateAsync(state, lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (closeCleanupStarted)
        {
        }
    }

    private void Renderer_OnSettingsCommitted(
        object? sender,
        LyricsGlassSettingsCommittedEventArgs eventArgs) =>
        hostStatePersistence.ApplySettings(eventArgs.Settings);

    private void Renderer_OnPositionChanged(
        object? sender,
        LyricsGlassPositionChangedEventArgs eventArgs) =>
        hostStatePersistence.ApplyPosition(eventArgs.Position);

    private void Renderer_OnCloseRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.BeginInvoke(new Action(Close));

    private void Renderer_OnOptionsRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.BeginInvoke(new Action(OpenOptionsWindow));

    private void OpenOptionsWindow()
    {
        if (closeCleanupStarted || optionsWindow is not null)
        {
            return;
        }

        var window = new LyricsGlassOptionsWindow(hostStatePersistence.Current.Glass);
        optionsWindow = window;
        try
        {
            if (window.ShowDialog() == true && window.Settings is { } settings)
            {
                _ = SetGlassSettingsAsync(settings);
            }
        }
        finally
        {
            if (ReferenceEquals(optionsWindow, window))
            {
                optionsWindow = null;
            }
        }
    }

    private async Task SetGlassSettingsAsync(LyricsGlassSettings settings)
    {
        try
        {
            await renderer.SetGlassSettingsAsync(settings, lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (closeCleanupStarted)
        {
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (finalCloseAllowed)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (closeCleanupStarted)
        {
            return;
        }

        closeCleanupStarted = true;
        _ = ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
        var window = optionsWindow;
        optionsWindow = null;
        window?.Close();
        lifetimeCancellation.Cancel();
        await shutdownCoordinator.ShutdownAsync();

        renderer.SettingsCommitted -= Renderer_OnSettingsCommitted;
        renderer.PositionChanged -= Renderer_OnPositionChanged;
        renderer.CloseRequested -= Renderer_OnCloseRequested;
        renderer.OptionsRequested -= Renderer_OnOptionsRequested;
        connectionRuntime.ConnectionStateChanged -= ConnectionRuntime_OnConnectionStateChanged;
        lifetimeCancellation.Dispose();
        finalCloseAllowed = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
