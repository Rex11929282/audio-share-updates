using System.ComponentModel;
using System.IO;
using System.Windows;

namespace AudioShare.Lyrics;

public partial class MainWindow : Window
{
    private readonly object hostStateGate = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly LyricsGlassSettingsStore settingsStore;
    private readonly LyricsGlassRendererSupervisor renderer;
    private readonly LyricsConnectionRuntime connectionRuntime;
    private LyricsGlassHostState hostState;
    private Task? closeCleanupTask;
    private bool closeCleanupStarted;
    private bool finalCloseAllowed;

    public MainWindow()
    {
        InitializeComponent();
        settingsStore = LyricsGlassSettingsStore.CreateDefault();
        hostState = settingsStore.Load();
        renderer = new LyricsGlassRendererSupervisor(hostState);
        connectionRuntime = new LyricsConnectionRuntime(new RadminLyricsRelay(Guid.NewGuid().ToString("N")));

        renderer.SettingsCommitted += Renderer_OnSettingsCommitted;
        renderer.PositionChanged += Renderer_OnPositionChanged;
        renderer.CloseRequested += Renderer_OnCloseRequested;
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
        LyricsGlassSettingsCommittedEventArgs eventArgs)
    {
        LyricsGlassHostState state;
        lock (hostStateGate)
        {
            hostState = hostState with { Glass = eventArgs.Settings };
            state = hostState;
        }

        TrySaveHostState(state);
    }

    private void Renderer_OnPositionChanged(
        object? sender,
        LyricsGlassPositionChangedEventArgs eventArgs)
    {
        LyricsGlassHostState state;
        lock (hostStateGate)
        {
            hostState = hostState with { Position = eventArgs.Position };
            state = hostState;
        }

        TrySaveHostState(state);
    }

    private void TrySaveHostState(LyricsGlassHostState state)
    {
        try
        {
            settingsStore.Save(state);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Renderer_OnCloseRequested(object? sender, EventArgs eventArgs) =>
        Dispatcher.BeginInvoke(new Action(Close));

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
        closeCleanupTask = ShutdownAndCloseAsync();
    }

    private async Task ShutdownAndCloseAsync()
    {
        lifetimeCancellation.Cancel();
        try
        {
            await renderer.StopAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }

        try
        {
            await renderer.DisposeAsync();
        }
        catch (Exception)
        {
        }

        try
        {
            await connectionRuntime.DisposeAsync();
        }
        catch (Exception)
        {
        }

        renderer.SettingsCommitted -= Renderer_OnSettingsCommitted;
        renderer.PositionChanged -= Renderer_OnPositionChanged;
        renderer.CloseRequested -= Renderer_OnCloseRequested;
        connectionRuntime.ConnectionStateChanged -= ConnectionRuntime_OnConnectionStateChanged;
        lifetimeCancellation.Dispose();
        finalCloseAllowed = true;
        await Dispatcher.InvokeAsync(Close);
    }
}
