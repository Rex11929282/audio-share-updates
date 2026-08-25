using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.App;

public partial class MainWindow : Window
{
    private const string VoicemeeterBananaDownloadUrl = "https://vb-audio.com/Voicemeeter/banana.htm";
    private static readonly TimeSpan ResetRecoveryRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PassiveRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SharingRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BackgroundSharingRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignalRefreshInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CloseRouteRecoveryTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly string FavoritesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowCast",
        "favorites.json");
    private static readonly string ApplicationOrderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowCast",
        "application-order.json");

    private readonly IAudioSessionDiscovery discovery = new WasapiAudioSessionDiscovery();
    private readonly RouteCoordinator routeCoordinator = new();
    private readonly SharingRecoveryScope sharingRecoveryScope = new();
    private readonly ExternalRoutingHelperClient routingHelper = new(Path.Combine(AppContext.BaseDirectory, "router-helper"));
    private readonly IApplicationRouteExecutor routeExecutor;
    private readonly IShareRecoveryJournal recoveryJournal = new FlowCastRecoveryJournal();
    private readonly VoicemeeterSharingBusService voicemeeterSharingBusService = new();
    private readonly FlowCastHealthProbe healthProbe;
    private readonly DispatcherTimer refreshTimer = new() { Interval = PassiveRefreshInterval };
    private readonly DispatcherTimer signalTimer = new() { Interval = SignalRefreshInterval };
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ShareSession shareSession = new();
    private readonly FavoritePrograms favoritePrograms;
    private readonly ApplicationOrder applicationOrder;
    private readonly FlowCastPreferencesStore preferencesStore = new();
    private readonly ShareHistoryStore shareHistoryStore = new();
    private readonly FlowCastTrayIcon trayIcon;
    private readonly Task startupInitialization;
    private FlowCastPreferences preferences;
    private readonly MotionController motionController;
    private GlobalHotkeyService? globalHotkeys;
    private readonly List<string> activityLog = [];
    private readonly List<ShareHistoryEntry> shareHistory = [];
    private DateTimeOffset? currentShareStartedAt;
    private string? currentShareProgramName;
    private IReadOnlyList<AudioSession> activeSessions = [];
    private IReadOnlyList<ExternalAudioDevice> outputDevices = [];
    private string? inputDeviceId;
    private string? auxDeviceId;
    private string experimentalRoutingStatus = "正在檢查音頻路由組件和輸出設備。";
    private bool isRefreshing;
    private bool isRoutingOperation;
    private bool isStartingShare;
    private bool isClosing;
    private bool isUpdateStaging;
    private bool exitRequested;
    private bool launchPlayed;
    private bool wasMinimized;
    private bool experimentalRoutingAvailable;
    private bool hasOwnedRoutingTransaction;
    private bool voicemeeterBananaInstalled;
    private bool requiresAttention;
    private bool pendingLocalOnlyReset;
    private bool wasAttention;
    private int backgroundRefreshCount;
    private SharingRouteState sharingRouteState = SharingRouteState.Unknown;
    private SharingBusStatus? sharingBusStatus;
    private HealthSummary healthSummary = HealthSummary.Create(false, false, false, false, routingHelperReady: false);

    public MainWindow()
        : this(Task.CompletedTask)
    {
    }

    public MainWindow(Task startupInitialization)
    {
        this.startupInitialization = startupInitialization ?? throw new ArgumentNullException(nameof(startupInitialization));
        InitializeComponent();
        DataContext = this;
        routeExecutor = new ApplicationRouteExecutor(routingHelper);
        healthProbe = new FlowCastHealthProbe(routingHelper);
        favoritePrograms = new FavoritePrograms(LoadFavoritePrograms());
        applicationOrder = new ApplicationOrder(LoadApplicationOrder());
        preferences = preferencesStore.Load();
        shareHistory.AddRange(shareHistoryStore.Load());
        motionController = new MotionController(
            this,
            MainContent,
            LogoMark,
            LaunchOverlay,
            LaunchBrand,
            LaunchTitle,
            [LaunchFlow, LaunchNote, LaunchCastArcOne, LaunchCastArcTwo],
            [LaunchNoteHead, LaunchPlay],
            TopStatusCard,
            StatusPulse,
            B1MeterFill,
            B1ActivityBars,
            [AtmosphereBarOne, AtmosphereBarTwo, AtmosphereBarThree, AtmosphereBarFour, AtmosphereBarFive, AtmosphereBarSix, AtmosphereBarSeven, AtmosphereBarEight],
            RouteFlowPath,
            RouteBeaconOne,
            RouteBeaconTwo,
            ApplicationListPanel,
            preferences.ReduceMotion);

        ProcessStatuses.Add(new ProcessStatus("Voicemeeter Banana", "voicemeeterpro"));
        UpdateShareStatistics();

        Loaded += MainWindow_Loaded;
        ContentRendered += MainWindow_ContentRendered;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        SourceInitialized += MainWindow_SourceInitialized;
        refreshTimer.Tick += RefreshTimer_Tick;
        signalTimer.Tick += SignalTimer_Tick;
        trayIcon = new FlowCastTrayIcon(
            () => Dispatcher.BeginInvoke(ShowWindowFromTray),
            () => Dispatcher.BeginInvoke(StartSharingFromTray),
            () => Dispatcher.BeginInvoke(StopSharingFromTray),
            () => Dispatcher.BeginInvoke(RequestExitFromTray),
            GetTrayPrograms);
    }

    public ObservableCollection<AudioApplicationRow> Applications { get; } = [];

    public ObservableCollection<ProcessStatus> ProcessStatuses { get; } = [];

    public ObservableCollection<HealthChip> HealthItems { get; } = [];

    public void SetUpdateAvailable(bool isAvailable) =>
        UpdateButton.Visibility = isAvailable ? Visibility.Visible : Visibility.Collapsed;

    public void SetUpdateStaging(bool isStaging) => isUpdateStaging = isStaging;

    public bool IsSharingActive => shareSession.State == ShareSessionState.Sharing;

    public void PrepareForUpdateRestart() => exitRequested = true;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await startupInitialization;
        if (isClosing)
        {
            return;
        }

        await RefreshAsync();
        refreshTimer.Start();
        signalTimer.Start();
        _ = ResetStartupToLocalOnlyAfterWindowIsReadyAsync();
        _ = ShowQuickStartAfterLaunchAsync();
    }

    private void WindowHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (launchPlayed)
        {
            return;
        }

        launchPlayed = true;
        motionController.PlayLaunch();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e) => ApplyGlobalHotkeys();

    private void ApplyGlobalHotkeys()
    {
        // Rebuild the registration each time so a preference change takes effect immediately.
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        globalHotkeys?.Dispose();
        globalHotkeys = new GlobalHotkeyService(source);
        if (!preferences.GlobalHotkeysEnabled)
        {
            return;
        }

        globalHotkeys.TryRegister(FlowCastHotkeys.ToggleShare, () => Dispatcher.BeginInvoke(ToggleShareFromHotkey));
        globalHotkeys.TryRegister(FlowCastHotkeys.ToggleWindow, () => Dispatcher.BeginInvoke(ToggleWindowFromHotkey));
    }

    private void ToggleShareFromHotkey()
    {
        if (IsSharingActive)
        {
            StopSharingFromTray();
        }
        else
        {
            StartSharingFromTray();
        }
    }

    private void ToggleWindowFromHotkey()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            HideToTray();
        }
        else
        {
            ShowWindowFromTray();
        }
    }

    private async Task ResetStartupToLocalOnlyAfterWindowIsReadyAsync()
    {
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        if (isClosing)
        {
            return;
        }

        SetMainInputSharing(false);
        await ResetToLocalOnlyAsync("已打開 FlowCast，正在確認未分享。");
        AddActivity("FlowCast 已啟動，當前不分享音頻。");
    }

    private async Task ShowQuickStartAfterLaunchAsync()
    {
        if (preferences.QuickStartCompleted)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (isClosing)
        {
            return;
        }
        if (isClosing)
        {
            return;
        }

        new TutorialWindow(this).ShowDialog();
        preferences = preferences.CompleteQuickStart();
        preferencesStore.Save(preferences);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (isUpdateStaging)
        {
            e.Cancel = true;
            return;
        }

        if (isClosing)
        {
            return;
        }

        if (!exitRequested)
        {
            e.Cancel = true;
            var dialog = new CloseFlowCastDialog(this);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (dialog.Action == CloseFlowCastAction.Minimize)
            {
                HideToTray();
                return;
            }

            if (dialog.Action != CloseFlowCastAction.Exit)
            {
                return;
            }

            exitRequested = true;
            e.Cancel = false;
        }

        if (isRoutingOperation)
        {
            DisableSharingBeforeImmediateClose();
            AddActivity("關閉時路由仍在處理中；已停止 B1 分享，下次打開 FlowCast 會自動恢復原播放路徑。");
            PrepareForClose();
            return;
        }

        if (GetSelectedSessions().Count == 0 &&
            shareSession.State is ShareSessionState.Idle or ShareSessionState.Disconnected)
        {
            PrepareForClose();
            return;
        }

        if (HasActiveResetGate())
        {
            e.Cancel = true;
            using var timeout = new CancellationTokenSource(CloseRouteRecoveryTimeout);
            DisableSharingBeforeImmediateClose();
            try
            {
                await RestoreRoutesBeforeExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                AddActivity("已停止 B1 分享；播放路徑會在下次打開 FlowCast 時自動恢復。");
            }
            catch (Exception)
            {
                AddActivity("已停止 B1 分享；播放路徑會在下次打開 FlowCast 時自動恢復。");
            }

            PrepareForClose();
            Close();
            return;
        }

        PrepareForClose();
    }

    private void DisableSharingBeforeImmediateClose()
    {
        try
        {
            SetMainInputSharing(false);
        }
        catch
        {
            // The recovery journal preserves the route transaction for the next launch.
        }
    }

    private void PrepareForClose()
    {
        isClosing = true;
        motionController.Suspend();
        refreshTimer.Stop();
        signalTimer.Stop();
        globalHotkeys?.Dispose();
        trayIcon.Dispose();
        lifetimeCancellation.Cancel();
    }

    private void ShowWindowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        motionController.Resume();
        if (!isClosing && !signalTimer.IsEnabled)
        {
            signalTimer.Start();
        }

        if (!isClosing && !refreshTimer.IsEnabled)
        {
            refreshTimer.Start();
        }
    }

    private void HideToTray()
    {
        Hide();
        motionController.Suspend();
        signalTimer.Stop();
        if (ShouldKeepBackgroundMonitoring())
        {
            refreshTimer.Interval = BackgroundSharingRefreshInterval;
            if (!refreshTimer.IsEnabled)
            {
                refreshTimer.Start();
            }

            return;
        }

        refreshTimer.Stop();
    }

    private bool ShouldKeepBackgroundMonitoring() =>
        IsSharingActive || hasOwnedRoutingTransaction || pendingLocalOnlyReset;

    private void RequestExitFromTray()
    {
        exitRequested = true;
        ShowWindowFromTray();
        Close();
    }

    private void StartSharingFromTray()
    {
        if (ApplyRoutingButton.IsEnabled)
        {
            ApplyRoutingButton_Click(ApplyRoutingButton, new RoutedEventArgs());
        }
    }

    private IReadOnlyList<TrayProgramEntry> GetTrayPrograms() =>
        Applications
            .Where(row => !row.IsProtected)
            .Select(row => new TrayProgramEntry(
                row.DisplayName,
                routeCoordinator.IsSelected(row.Session),
                () => Dispatcher.BeginInvoke(() => ShareProgramFromTray(row))))
            .ToList();

    private void ShareProgramFromTray(AudioApplicationRow row)
    {
        ShowWindowFromTray();
        if (isClosing || row.IsProtected || !row.CanRequestSelection)
        {
            return;
        }

        // Realize the row's container so its check box exists, then drive selection through the
        // model. The TwoWay binding raises the same Checked event a manual click would, so the
        // existing confirm / switch / restore / start-sharing path runs unchanged.
        ApplicationList.ScrollIntoView(row);
        ApplicationList.UpdateLayout();
        row.IsSelected = true;
    }

    private async void StopSharingFromTray()
    {
        await StopSharingWithResetAsync();
    }

    public async Task<bool> StopSharingWithResetAsync()
    {
        if (!CanStopSharing())
        {
            return !IsSharingActive;
        }

        isRoutingOperation = true;
        UpdateRoutingSetupState();
        try
        {
            return await StopSharingAndRestoreRoutesAsync(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (isClosing)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable("暫時無法確認是否已停止分享，請重新打開主窗口後再試。", exception);
            ShowError("FlowCast 還沒能確認聲音已恢復分享前的播放路徑。", null);
            return false;
        }
        finally
        {
            isRoutingOperation = false;
            if (!isClosing)
            {
                await RefreshSharingRouteStateAsync();
            }

            UpdateRoutingSetupState();
        }
    }

    private async void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            wasMinimized = true;
            motionController.Suspend();
            signalTimer.Stop();
            if (ShouldKeepBackgroundMonitoring())
            {
                refreshTimer.Interval = BackgroundSharingRefreshInterval;
            }
            else
            {
                refreshTimer.Stop();
            }

            return;
        }

        motionController.Resume();
        if (!isClosing && !signalTimer.IsEnabled)
        {
            signalTimer.Start();
        }
        if (wasMinimized)
        {
            wasMinimized = false;
            motionController.PlayWindowResume();
        }

        if (!isClosing && !refreshTimer.IsEnabled)
        {
            refreshTimer.Start();
            await RefreshAsync(refreshRouting: false);
        }
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshAsync(refreshRouting: false);
    }

    private void SignalTimer_Tick(object? sender, EventArgs e)
    {
        if (isClosing)
        {
            return;
        }

        var previousRouteState = sharingRouteState;
        var previousAttention = requiresAttention;
        var previousMainInputShared = sharingBusStatus?.IsMainInputShared;
        try
        {
            sharingBusStatus = voicemeeterSharingBusService.GetStatus();
            ReconcileSharingStateWithB1();
            if (shareSession.State == ShareSessionState.Sharing && sharingBusStatus.IsMainInputShared)
            {
                var b1Level = Math.Clamp(sharingBusStatus.B1Level * 100f, 0f, 100f);
                motionController.PlayAudioLevelPulse(b1Level);
            }
            else
            {
                motionController.SetAtmosphereLevel(0);
            }
        }
        catch
        {
            sharingBusStatus = null;
            motionController.SetAtmosphereLevel(0);
        }

        if (previousRouteState != sharingRouteState ||
            previousAttention != requiresAttention ||
            previousMainInputShared != sharingBusStatus?.IsMainInputShared)
        {
            UpdateRoutingSetupState();
            return;
        }

        UpdateLiveSignalVisuals();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private static bool IsVoicemeeterBananaRunning()
    {
        var processes = Process.GetProcessesByName("voicemeeterpro");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void TutorialButton_Click(object sender, RoutedEventArgs e) => new TutorialWindow(this).ShowDialog();

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var preferencesWindow = new PreferencesWindow(this, preferences);
        if (preferencesWindow.ShowDialog() != true)
        {
            return;
        }

        preferences = preferencesWindow.UpdatedPreferences;
        preferencesStore.Save(preferences);
        motionController.SetReduceMotion(preferences.ReduceMotion);
        ApplyGlobalHotkeys();
        await RefreshApplicationsOnlyAsync();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) =>
        await ((App)Application.Current).CheckForUpdatesFromUserAsync(this);

    private void DownloadVoicemeeterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(VoicemeeterBananaDownloadUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError("無法打開 Voicemeeter Banana 官方下載頁面。", exception);
        }
    }

    private async Task RefreshAsync(bool refreshRouting = true)
    {
        if (isRefreshing || isClosing || isRoutingOperation)
        {
            return;
        }

        isRefreshing = true;
        if (refreshRouting)
        {
            RefreshButton.IsEnabled = false;
        }

        try
        {
            var wasSharing = sharingRouteState == SharingRouteState.Sharing;
            var sessions = await discovery.GetActiveSessionsAsync(lifetimeCancellation.Token);
            activeSessions = sessions;
            var selectedProgramClosed = UpdateApplications(activeSessions);
            var refreshEndpoints = refreshRouting || ++backgroundRefreshCount % 4 == 0;
            var endpointsChanged = refreshEndpoints && await RefreshExperimentalRoutingAvailabilityAsync();
            if (refreshEndpoints)
            {
                UpdateApplications(activeSessions);
            }

            var reconciliationFailed = !await ReconcileNewApplicationsDuringShareAsync();
            var refreshRouteState = refreshRouting || sharingRouteState == SharingRouteState.Sharing;
            if (refreshRouteState)
            {
                await RefreshSharingRouteStateAsync();
            }

            var shouldRecoverPendingReset = pendingLocalOnlyReset && GetRoutableActiveSessions().Count > 0;
            if (reconciliationFailed)
            {
                await StopSharingForSafetyAsync(experimentalRoutingStatus);
            }
            else if (selectedProgramClosed || shouldRecoverPendingReset ||
                RouteSafetyPolicy.ShouldStopSharing(wasSharing, endpointsChanged))
            {
                await StopSharingForSafetyAsync(endpointsChanged
                    ? "Voicemeeter 音頻設備已變化，已停止分享。"
                    : selectedProgramClosed
                        ? "分享程序已關閉，已停止分享。"
                        : "分享路由異常，已停止分享。");
            }
            if (!requiresAttention)
            {
                ErrorPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            ShowError("無法檢測當前正在播放音頻的程序。", exception);
        }
        finally
        {
            isRefreshing = false;
            if (!isClosing)
            {
                if (refreshRouting)
                {
                    RefreshButton.IsEnabled = true;
                }
                UpdateRoutingSetupState();
            }
        }
    }

    private async Task<bool> ReconcileNewApplicationsDuringShareAsync()
    {
        if (shareSession.State != ShareSessionState.Sharing ||
            !experimentalRoutingAvailable ||
            !hasOwnedRoutingTransaction ||
            string.IsNullOrWhiteSpace(inputDeviceId) ||
            string.IsNullOrWhiteSpace(auxDeviceId))
        {
            return true;
        }

        var selected = GetSelectedSessions().SingleOrDefault();
        if (selected is null)
        {
            experimentalRoutingStatus = "未找到正在分享的程序，已停止分享以保護本機播放。";
            return false;
        }

        var routeableSessions = await GetRouteableSessionsAsync(
            GetRoutableActiveSessions(),
            lifetimeCancellation.Token);
        if (!routeableSessions.Any(session => session.ProcessId == selected.ProcessId &&
                                              session.ProcessStartUtcTicks == selected.ProcessStartUtcTicks))
        {
            experimentalRoutingStatus = "正在分享的程序已不可路由，已停止分享以保護本機播放。";
            return false;
        }

        var plan = ApplicationRoutePlanner.Create(routeableSessions, [selected], inputDeviceId, auxDeviceId);
        var result = await routeExecutor.ApplyAdditionalAsync(plan, lifetimeCancellation.Token);
        if (!result.Succeeded)
        {
            requiresAttention = true;
            experimentalRoutingStatus = $"無法確認新程序只在本機播放：{result.Message}";
            AddActivity("新程序的本機播放保護失敗。");
            return false;
        }

        hasOwnedRoutingTransaction = result.HasPendingTransaction;
        await PersistRecoveryAsync(selected, result.Snapshots, lifetimeCancellation.Token);
        return true;
    }

    private void UpdateProcessStatuses()
    {
        foreach (var status in ProcessStatuses)
        {
            var processes = Process.GetProcessesByName(status.ProcessName);
            try
            {
                status.IsRunning = processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    private bool UpdateApplications(IReadOnlyList<AudioSession> sessions)
    {
        var eligibleSessions = sessions.Where(session => !preferences.IsExcluded(session.ProcessName)).ToArray();
        var selectedProgramClosed = routeCoordinator.RemoveSelectionsAbsentFrom(sessions);
        Applications.Clear();

        var visibleSessions = favoritePrograms.Order(eligibleSessions
            .OrderByDescending(session => session.HasAudio));
        applicationOrder.Synchronize(visibleSessions);
        foreach (var session in applicationOrder.Order(visibleSessions))
        {
            var isProtected = AudioRoutingPolicy.IsProtectedProcess(session.ProcessName);
            Applications.Add(new AudioApplicationRow(
                session,
                isProtected,
                !isProtected && routeCoordinator.IsSelected(session),
                favoritePrograms.Contains(session.ProcessName),
                outputDevices,
                inputDeviceId,
                auxDeviceId));
        }

        EmptyStateText.Visibility = Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "暫時沒有可路由的程序。打開音樂、瀏覽器或遊戲後再刷新。";
        UpdateRoutingSetupState();
        return selectedProgramClosed;
    }

    private async Task RefreshApplicationsOnlyAsync()
    {
        if (isRefreshing || isClosing)
        {
            return;
        }

        try
        {
            var sessions = await discovery.GetActiveSessionsAsync(lifetimeCancellation.Token);
            activeSessions = sessions;
            UpdateApplications(activeSessions);
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            ShowError("無法刷新程序列表。", exception);
        }
    }

    private async void ApplicationSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not AudioApplicationRow row || row.IsProtected)
        {
            return;
        }

        var shouldShare = checkBox.IsChecked == true;

        if (routeCoordinator.IsSelected(row.Session) == shouldShare)
        {
            return;
        }

        checkBox.IsEnabled = false;
        try
        {
            var previousSelection = shouldShare
                ? Applications.FirstOrDefault(application =>
                    !ReferenceEquals(application, row) && routeCoordinator.IsSelected(application.Session))
                : null;
            if (previousSelection is not null)
            {
                var message = $"選擇“{row.DisplayName}”會停止分享“{previousSelection.DisplayName}”，並恢復它原來的播放路徑。\n\n要繼續嗎？";
                if (!FlowCastMessageDialog.Confirm(this, "切換分享程序", message, "確認切換", "取消", FlowCastWindowTone.Share))
                {
                    row.IsSelected = false;
                    checkBox.IsChecked = false;
                    return;
                }

                if (shareSession.State == ShareSessionState.Sharing)
                {
                    isRoutingOperation = true;
                    UpdateRoutingSetupState();
                    bool restored;
                    try
                    {
                        restored = await StopSharingAndRestoreRoutesAsync(lifetimeCancellation.Token);
                    }
                    finally
                    {
                        isRoutingOperation = false;
                    }

                    if (!restored)
                    {
                        row.IsSelected = false;
                        checkBox.IsChecked = false;
                        return;
                    }
                }
                else
                {
                    await routeCoordinator.UnshareAsync(previousSelection.Session, lifetimeCancellation.Token);
                }
            }
            else if (shouldShare &&
                     !FlowCastMessageDialog.Confirm(
                         this,
                         "確認分享",
                         $"要開始分享“{row.DisplayName}”的聲音嗎？\n\n分享開始後，其他人會聽到這個程序的音頻。",
                         "開始分享",
                         "取消",
                         FlowCastWindowTone.Share))
            {
                row.IsSelected = false;
                checkBox.IsChecked = false;
                return;
            }

            var result = shouldShare
                ? await routeCoordinator.SelectOnlyAsync(row.Session, lifetimeCancellation.Token)
                : await routeCoordinator.UnshareAsync(row.Session, lifetimeCancellation.Token);

            foreach (var application in Applications)
            {
                application.IsSelected = routeCoordinator.IsSelected(application.Session);
            }
            if (!result.Succeeded)
            {
                ShowError(result.Message ?? "無法更新本地勾選狀態。", null);
                return;
            }

            ErrorPanel.Visibility = Visibility.Collapsed;
            motionController.PlaySelectionConfirmed(checkBox);
            if (!shouldShare)
            {
                return;
            }

            isStartingShare = true;
            UpdateRoutingSetupState();
            try
            {
                await StartSharingAsync(showConfirmation: false);
            }
            finally
            {
                isStartingShare = false;
            }
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            row.IsSelected = routeCoordinator.IsSelected(row.Session);
            ShowError("無法更新本地勾選狀態或打開 Windows 音量混音器。", exception);
        }
        finally
        {
            if (!isClosing)
            {
                checkBox.ClearValue(IsEnabledProperty);
                UpdateRoutingSetupState();
            }
        }
    }

    private void UpdateRoutingSetupState()
    {
        var selected = GetSelectedSessions();
        trayIcon.SetState(shareSession.State);
        motionController.SetRouteActive(shareSession.State == ShareSessionState.Sharing);
        InstructionText.Text = AudioRoutingPolicy.GetSetupInstruction(selected);
        ExperimentalRoutingStatusText.Text = experimentalRoutingStatus;
        UpdateLiveSignalVisuals();
        UpdateHealthChips();
        (FlowCastStatusText.Text, FlowCastStatusHintText.Text) = requiresAttention
            ? ("需要處理", experimentalRoutingStatus)
            : shareSession.State switch
            {
                ShareSessionState.Sharing => ("正在分享", $"已分享 {shareSession.Duration(DateTimeOffset.Now):hh\\:mm\\:ss}"),
                ShareSessionState.Disconnected => ("分享已斷開", $"{shareSession.StopReason ?? "音頻連接已中斷。"} 已分享 {shareSession.FinalDuration:hh\\:mm\\:ss}"),
                _ when sharingRouteState == SharingRouteState.LocalOnly => ("只自己聽到", "當前所有正在播放的程序都只在本機播放。"),
                _ => ("未確認", "正在檢查程序的分享狀態；確認前不會啟用停止分享。"),
            };
        ApplyRoutingButton.Content = "開始分享";
        ApplyRoutingButton.IsEnabled = !isStartingShare && CanApplyRouting();
        RestoreRoutingButton.IsEnabled = CanStopSharing();
        refreshTimer.Interval = pendingLocalOnlyReset
            ? ResetRecoveryRefreshInterval
            : sharingRouteState == SharingRouteState.Sharing
                ? SharingRefreshInterval
                : PassiveRefreshInterval;
        if (WindowState == WindowState.Minimized && ShouldKeepBackgroundMonitoring())
        {
            refreshTimer.Interval = BackgroundSharingRefreshInterval;
        }

        var canEditRoutes = experimentalRoutingAvailable && !isRoutingOperation;
        foreach (var row in Applications)
        {
            row.SetSelectionEditingEnabled(!isRoutingOperation);
            row.SetRouteEditingEnabled(canEditRoutes);
            row.SetSharingState(shareSession.State == ShareSessionState.Sharing);
        }

        if (requiresAttention && !wasAttention)
        {
            motionController.PlayAttention();
        }

        wasAttention = requiresAttention;
    }

    private void UpdateLiveSignalVisuals()
    {
        var hasSelection = GetSelectedSessions().Count > 0;
        var b1Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        B1StatusText.Visibility = b1Visibility;
        B1MeterPanel.Visibility = b1Visibility;
        B1SelfTestText.Visibility = b1Visibility;
        B1ActivityBars.Visibility = b1Visibility;
        B1StatusText.Text = GetB1StatusText();
        if (!hasSelection)
        {
            B1MeterFill.Width = 0;
            B1MeterValueText.Text = "0%";
            B1SelfTestText.Text = "選擇程序後才會檢查 B1 音頻信號。";
            return;
        }

        var b1Percent = Math.Clamp((sharingBusStatus?.B1Level ?? 0f) * 100f, 0f, 100f);
        B1MeterFill.Width = b1Percent;
        B1MeterValueText.Text = $"{b1Percent:0}%";
        B1SelfTestText.Text = ShareSignalSelfTest.GetMessage(
            sharingRouteState == SharingRouteState.Sharing,
            sharingBusStatus?.IsMainInputShared == true,
            sharingBusStatus?.InputLevel ?? 0f,
            sharingBusStatus?.B1Level ?? 0f);

        if (!requiresAttention &&
            shareSession.State == ShareSessionState.Sharing)
        {
            FlowCastStatusHintText.Text = shareSession.State == ShareSessionState.Sharing
                ? $"已分享 {shareSession.Duration(DateTimeOffset.Now):hh\\:mm\\:ss}"
                : $"分享仍在繼續 {shareSession.Duration(DateTimeOffset.Now):hh\\:mm\\:ss}";
        }
    }

    private IReadOnlyList<AudioSession> GetSelectedSessions() =>
        applicationOrder.Order(routeCoordinator.GetSelectedSessions(activeSessions)
            .Where(session => !preferences.IsExcluded(session.ProcessName)));

    private IReadOnlyList<AudioSession> GetRoutableActiveSessions() =>
        activeSessions
            .Where(session => !AudioRoutingPolicy.IsProtectedProcess(session.ProcessName))
            .Where(session => !preferences.IsExcluded(session.ProcessName))
            .ToArray();

    private IReadOnlyList<AudioSession> GetRecoverySessions() =>
        sharingRecoveryScope.IncludeCurrent(
            GetSelectedSessions().Where(session => !AudioRoutingPolicy.IsProtectedProcess(session.ProcessName)));

    private bool CanApplyRouting() =>
        ShareStartPolicy.CanStart(
            GetSelectedSessions().Count > 0,
            voicemeeterBananaInstalled &&
            healthSummary.IsReady("Banana") &&
            healthSummary.IsReady("Input") &&
            healthSummary.IsReady("AUX") &&
            GetRoutableActiveSessions().Count > 0 &&
            experimentalRoutingAvailable &&
            !string.IsNullOrWhiteSpace(inputDeviceId) &&
            !string.IsNullOrWhiteSpace(auxDeviceId) &&
            shareSession.State is ShareSessionState.Idle or ShareSessionState.Disconnected,
            isRoutingOperation);

    private void UpdateHealthChips()
    {
        var items = healthSummary.Items;
        if (HealthItems.Count == items.Count &&
            HealthItems.Select(item => item.Key).SequenceEqual(items.Select(item => item.Key)) &&
            HealthItems.Select(item => item.State).SequenceEqual(items.Select(item => item.State)) &&
            HealthItems.Select(item => item.Message).SequenceEqual(items.Select(item => item.Message)))
        {
            return;
        }

        HealthItems.Clear();
        foreach (var item in items)
        {
            HealthItems.Add(HealthChip.From(item));
        }

        var allReady = items.All(item => item.State == HealthState.Ready);
        HealthSummaryText.Text = allReady ? "系統已就緒" : "需要檢查";
        HealthSummaryDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            allReady ? "#1DBE86" : "#E58A2B"));
    }

    private void HealthSummaryButton_Click(object sender, RoutedEventArgs e)
    {
        var isExpanded = HealthDetailsPanel.Visibility == Visibility.Visible;
        HealthDetailsPanel.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        HealthSummaryChevron.Text = isExpanded ? "⌄" : "⌃";
    }

    private string GetB1StatusText()
    {
        if (GetSelectedSessions().Count == 0)
        {
            return "B1：未選擇分享程序";
        }

        if (sharingBusStatus is null)
        {
            return "B1：無法讀取 Banana 狀態";
        }

        var signal = $"Input 音量 {(sharingBusStatus.InputLevel * 100):0}%";
        if (sharingBusStatus.IsMainInputShared)
        {
            return $"B1：正在分享 · {signal}";
        }

        return $"B1：僅自己聽 · {signal}";
    }

    private bool CanStopSharing() =>
        voicemeeterBananaInstalled &&
        HasActiveResetGate() &&
        experimentalRoutingAvailable &&
        (sharingRouteState == SharingRouteState.Sharing || requiresAttention) &&
        !isRoutingOperation &&
        !string.IsNullOrWhiteSpace(inputDeviceId) &&
        !string.IsNullOrWhiteSpace(auxDeviceId);

    private bool CanResetToLocalOnly() =>
        voicemeeterBananaInstalled &&
        HasActiveResetGate() &&
        experimentalRoutingAvailable &&
        !isRoutingOperation &&
        !string.IsNullOrWhiteSpace(inputDeviceId) &&
        !string.IsNullOrWhiteSpace(auxDeviceId);

    private bool HasActiveResetGate() =>
        RouteSafetyPolicy.RequiresReset(
            IsConfirmedLocalOnly(),
            sharingRouteState == SharingRouteState.Sharing,
            GetRecoverySessions().Count > 0,
            hasOwnedRoutingTransaction);

    private bool IsConfirmedLocalOnly() =>
        sharingRouteState == SharingRouteState.LocalOnly &&
        sharingBusStatus is { IsMainInputShared: false, IsAuxShared: false };

    private async Task<bool> RestoreRoutesBeforeExitAsync(CancellationToken token)
    {
        if (!HasActiveResetGate())
        {
            return true;
        }

        if (!CanResetToLocalOnly())
        {
            experimentalRoutingStatus = "暫時無法確認聲音已恢復；下次打開 FlowCast 會先自動重試。";
            return false;
        }

        return await ResetToLocalOnlyAsync("正在恢復分享前的播放路徑，然後關閉窗口...", token) &&
            IsConfirmedLocalOnly();
    }

    private async Task RefreshSharingRouteStateAsync()
    {
        var previousState = sharingRouteState;
        sharingRouteState = SharingRouteState.Unknown;
        if (!experimentalRoutingAvailable ||
            string.IsNullOrWhiteSpace(inputDeviceId) ||
            string.IsNullOrWhiteSpace(auxDeviceId))
        {
            return;
        }

        var routableSessions = GetRoutableActiveSessions();
        if (routableSessions.Count == 0)
        {
            sharingRouteState = previousState;
            return;
        }

        try
        {
            var routeStates = new List<SharingRouteState>(routableSessions.Count);
            foreach (var session in routableSessions)
            {
                try
                {
                    var routes = await routingHelper.GetRouteAsync(
                        session.ProcessId,
                        session.ProcessStartUtcTicks,
                        session.ProcessName,
                        lifetimeCancellation.Token);
                    routeStates.Add(SharingRouteState.Classify(routes, inputDeviceId, auxDeviceId));
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.Contains("Active output session not found", StringComparison.OrdinalIgnoreCase))
                {
                    // The program already sends audio directly to Voicemeeter Input, so B1 remains its safe gate.
                }
            }

            if (routeStates.Count == 0)
            {
                sharingRouteState = previousState;
                return;
            }

            sharingRouteState = SharingRouteState.Aggregate(routeStates);
            if (previousState == SharingRouteState.Sharing && sharingRouteState != SharingRouteState.Sharing)
            {
                requiresAttention = true;
                experimentalRoutingStatus = "檢測到 Windows 音頻路由已變更，無法確認分享。請重新開始分享或停止分享。";
                AddActivity("分享路由已在外部變更，等待你確認處理。");
            }
        }
        catch (OperationCanceledException) when (isClosing)
        {
            throw;
        }
        catch
        {
            sharingRouteState = SharingRouteState.Unknown;
            if (previousState == SharingRouteState.Sharing)
            {
                requiresAttention = true;
                experimentalRoutingStatus = "無法重新確認分享路由。請停止分享以恢復只自己聽。";
                AddActivity("分享路由暫時無法讀取，已保留停止分享入口。");
            }
        }
    }

    private void ReconcileSharingStateWithB1()
    {
        if (GetSelectedSessions().Count == 0 &&
            !hasOwnedRoutingTransaction &&
            GetRecoverySessions().Count == 0)
        {
            if (sharingBusStatus is { IsMainInputShared: false, IsAuxShared: false })
            {
                if (shareSession.Stop(DateTimeOffset.Now, "未選擇分享程序。"))
                {
                    RecordCompletedShare("未選擇分享程序。");
                }

                sharingRouteState = SharingRouteState.LocalOnly;
                requiresAttention = false;
                experimentalRoutingStatus = "當前未選擇分享程序，聲音只在本機播放。";
            }

            return;
        }

        if (sharingRouteState != SharingRouteState.Sharing ||
            sharingBusStatus is { IsMainInputShared: true, IsAuxShared: false })
        {
            return;
        }

        sharingRouteState = SharingRouteState.Unknown;
        requiresAttention = true;
        experimentalRoutingStatus = sharingBusStatus is { IsAuxShared: true }
            ? "檢測到 AUX 也已發送到 B1。為避免誤分享，請停止分享。"
            : "檢測到 Banana 的 B1 已關閉，無法確認分享。請重新開始分享或停止分享。";
        AddActivity("Banana 的 B1 狀態已變更，分享狀態需要確認。");
    }

    private async Task<bool> RefreshExperimentalRoutingAvailabilityAsync()
    {
        if (RoutingRefreshDisplay.ShouldShowCheckingMessage(experimentalRoutingAvailable))
        {
            experimentalRoutingStatus = "正在檢查音頻路由組件和輸出設備。";
            UpdateRoutingSetupState();
        }

        var probe = await healthProbe.CheckAsync(lifetimeCancellation.Token);
        healthSummary = probe.Summary;
        outputDevices = probe.OutputDevices;
        SetVoicemeeterBananaInstalled(probe.BananaInstalled);
        if (!probe.RoutingAvailable)
        {
            SetExperimentalRoutingUnavailable(probe.RoutingMessage);
            return false;
        }

        try
        {
            if (probe.Endpoints is null)
            {
                SetExperimentalRoutingUnavailable(probe.BananaInstalled
                    ? "Voicemeeter Banana 已安裝，但必要的音頻設備尚未就緒。請重新啟動電腦後再刷新。"
                    : "未檢測到 Voicemeeter Banana。請安裝後重啟電腦，再重新檢測。");
                return false;
            }

            var endpointsChanged = !string.IsNullOrWhiteSpace(inputDeviceId) &&
                                   (!string.Equals(inputDeviceId, probe.Endpoints.Input.Id, StringComparison.OrdinalIgnoreCase) ||
                                    !string.Equals(auxDeviceId, probe.Endpoints.AuxInput.Id, StringComparison.OrdinalIgnoreCase));
            if (!healthSummary.IsReady("Banana"))
            {
                SetExperimentalRoutingUnavailable(probe.BananaInstalled
                    ? "Voicemeeter Banana 已安裝但尚未運行。請打開 Banana 後重新檢測。"
                    : "未檢測到 Voicemeeter Banana。請安裝後重啟電腦，再重新檢測。");
                return false;
            }

            inputDeviceId = probe.Endpoints.Input.Id;
            auxDeviceId = probe.Endpoints.AuxInput.Id;
            experimentalRoutingAvailable = true;
            experimentalRoutingStatus = hasOwnedRoutingTransaction
                ? "本次音頻路由已生效。再次應用會按當前勾選替換；停止分享會恢復分享前的播放路徑。"
                : "音頻路由已就緒。點擊應用並確認後才會更改 Windows 路由；停止分享會恢復分享前的播放路徑。";
            requiresAttention = false;
            return endpointsChanged;
        }
        catch (OperationCanceledException) when (isClosing)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable(
                "暫時無法確認音頻設備。你的聲音不會被自動分享，請點擊“刷新”後再試。",
                exception);
            return false;
        }

    }

    private async void ApplyRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        if (isStartingShare || !CanApplyRouting())
        {
            UpdateRoutingSetupState();
            return;
        }

        isStartingShare = true;
        experimentalRoutingStatus = "正在確認開始分享。";
        UpdateRoutingSetupState();
        try
        {
            await StartSharingAsync();
        }
        finally
        {
            isStartingShare = false;
            UpdateRoutingSetupState();
        }
    }

    private async Task StartSharingAsync(bool showConfirmation = true)
    {
        var selected = GetSelectedSessions();
        if (!CanApplyRouting())
        {
            return;
        }

        var routableSessions = GetRoutableActiveSessions();

        var routeableSessions = await GetRouteableSessionsAsync(routableSessions, lifetimeCancellation.Token);
        var selectedNames = selected.Select(session => session.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directInputSessions = routableSessions
            .Where(session => !routeableSessions.Any(routeable => routeable.ProcessId == session.ProcessId))
            .ToArray();
        if (directInputSessions.Any(session => !selectedNames.Contains(session.ProcessName)))
        {
            SetMainInputSharing(false);
            sharingRouteState = SharingRouteState.LocalOnly;
            experimentalRoutingStatus = "有未選中的程序無法由 Windows 路由。為避免誤分享，未開啟 B1。";
            ShowError("請先關閉未選中的音頻程序，再開始分享。", null);
            return;
        }

        ApplicationRoutePlan plan;
        try
        {
            plan = ApplicationRoutePlanner.Create(
                routeableSessions,
                selected.Where(session => routeableSessions.Any(routeable => routeable.ProcessId == session.ProcessId)).ToArray(),
                inputDeviceId!,
                auxDeviceId!);
        }
        catch (Exception exception)
        {
            SetMainInputSharing(false);
            sharingRouteState = SharingRouteState.LocalOnly;
            hasOwnedRoutingTransaction = false;
            SetExperimentalRoutingUnavailable(
                "暫時無法準備分享。你的聲音不會被自動分享，請點擊“刷新”後再試。",
                exception);
            ShowError("這次沒有開始分享，你和朋友的通話設置不會被改變。", null);
            return;
        }

        var confirmation = ShareConfirmation.Create(routableSessions, selected);
        if (showConfirmation && new ShareConfirmationWindow(this, confirmation).ShowDialog() != true)
        {
            return;
        }

        isRoutingOperation = true;
        UpdateRoutingSetupState();
        try
        {
            if (hasOwnedRoutingTransaction)
            {
                var restoreResult = await routeExecutor.RestoreAsync(lifetimeCancellation.Token);
                if (!restoreResult.Succeeded)
                {
                    hasOwnedRoutingTransaction = restoreResult.HasPendingTransaction;
                    await RecoverFromShareStartFailureAsync(
                        $"無法替換路由：{restoreResult.Message}",
                        null);
                    return;
                }

                hasOwnedRoutingTransaction = false;
                await recoveryJournal.ClearAsync();
            }

            if (plan.Commands.Count > 0)
            {
                sharingRecoveryScope.Track(routeableSessions);
                var result = await routeExecutor.ApplyAsync(plan, lifetimeCancellation.Token);
                if (result.Snapshots.Count > 0)
                {
                    await PersistRecoveryAsync(selected.First(), result.Snapshots, lifetimeCancellation.Token);
                }
                if (!result.Succeeded)
                {
                    hasOwnedRoutingTransaction = result.HasPendingTransaction;
                    await RecoverFromShareStartFailureAsync($"應用失敗：{result.Message}", null);
                    return;
                }

                hasOwnedRoutingTransaction = result.HasPendingTransaction;
                if (!await VerifyRoutePlanAsync(plan, lifetimeCancellation.Token))
                {
                    await RecoverFromShareStartFailureAsync("分享路由未完全寫入", null);
                    return;
                }
            }

            if (!SetMainInputSharing(true))
            {
                await RecoverFromShareStartFailureAsync("無法開啟 B1 分享通道", null);
                return;
            }

            if (!shareSession.Start(DateTimeOffset.Now))
            {
                await RecoverFromShareStartFailureAsync("開始分享狀態無效。", null);
                return;
            }
            currentShareStartedAt = DateTimeOffset.Now;
            currentShareProgramName = selected.FirstOrDefault()?.DisplayName ?? "未知程序";
            UpdateShareStatistics();
            sharingRouteState = SharingRouteState.Sharing;
            motionController.PlaySharingConfirmed();
            experimentalRoutingStatus = "正在分享所選程序。停止分享、切換程序或關閉 FlowCast 時都會恢復原來的播放路徑。";
            AddActivity("分享已確認。");
            ErrorPanel.Visibility = Visibility.Collapsed;
            FlowCastMessageDialog.Show(this, "分享已開始", "已確認分享。朋友現在會聽到你勾選的程序音頻。", FlowCastWindowTone.Share);
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            await RecoverFromShareStartFailureAsync("暫時無法應用分享設置。", exception);
        }
        finally
        {
            isRoutingOperation = false;
            if (!isClosing)
            {
                await RefreshSharingRouteStateAsync();
            }
            UpdateRoutingSetupState();
        }
    }

    private async Task RecoverFromShareStartFailureAsync(string failureReason, Exception? exception)
    {
        bool recovered;
        try
        {
            recovered = await StopSharingAndRestoreRoutesAsync(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (isClosing)
        {
            throw;
        }
        catch (Exception recoveryException)
        {
            requiresAttention = true;
            SetExperimentalRoutingUnavailable(
                "這次沒有開始分享，且暫時無法確認聲音是否已回到只自己聽。請點擊“停止分享”再試一次。",
                recoveryException);
            ShowError("未開始分享，無法恢復只自己聽，請重試。", recoveryException);
            return;
        }

        var outcome = ShareStartRecoveryPolicy.FromRecoveryResult(recovered);
        requiresAttention = outcome.RequiresAttention;
        if (outcome.LocalOnlyRecovered)
        {
            experimentalRoutingStatus = "未開始分享，已恢復只自己聽。";
            ShowError("未開始分享，已恢復只自己聽。", null);
            return;
        }

        SetExperimentalRoutingUnavailable($"{failureReason}；無法恢復只自己聽。");
        ShowError("未開始分享，無法恢復只自己聽，請重試。", exception);
    }

    private async void RestoreRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStopSharing())
        {
            UpdateRoutingSetupState();
            return;
        }

        var confirmationText = "停止分享會關閉 B1，並恢復你開始分享前的播放路徑。\n\n現在停止分享嗎？";
        if (!FlowCastMessageDialog.Confirm(this, "停止分享", confirmationText, "停止分享", "取消", FlowCastWindowTone.Error))
        {
            return;
        }

        isRoutingOperation = true;
        UpdateRoutingSetupState();
        try
        {
            await StopSharingAndRestoreRoutesAsync(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable(
                "暫時無法確認是否已停止分享。請點擊“停止分享”再試一次。",
                exception);
            ShowError("FlowCast 還沒能確認聲音已回到只自己聽。", null);
        }
        finally
        {
            isRoutingOperation = false;
            if (!isClosing)
            {
                await RefreshSharingRouteStateAsync();
            }
            UpdateRoutingSetupState();
        }
    }

    private void SetExperimentalRoutingUnavailable(string reason, Exception? exception = null)
    {
        experimentalRoutingAvailable = false;
        inputDeviceId = null;
        auxDeviceId = null;
        sharingRouteState = SharingRouteState.Unknown;
        requiresAttention = true;
        experimentalRoutingStatus = $"音頻路由不可用：{reason}";
        UpdateRoutingSetupState();
    }

    private async Task<bool> StopSharingAndRestoreRoutesAsync(
        CancellationToken token,
        bool disconnected = false,
        string? stopReason = null)
    {
        var wasSharing = shareSession.State == ShareSessionState.Sharing;
        if (!voicemeeterBananaInstalled || !experimentalRoutingAvailable ||
            string.IsNullOrWhiteSpace(inputDeviceId) || string.IsNullOrWhiteSpace(auxDeviceId))
        {
            ShowError("無法停止分享：Voicemeeter Banana 尚未就緒。", null);
            return false;
        }

        if (!SetMainInputSharing(false))
        {
            return false;
        }

        if (hasOwnedRoutingTransaction)
        {
            var restoreResult = await routeExecutor.RestoreAsync(token);
            hasOwnedRoutingTransaction = restoreResult.HasPendingTransaction;
            if (!restoreResult.Succeeded)
            {
                experimentalRoutingStatus = $"停止分享失敗：{restoreResult.Message}";
                ShowError("無法先清除上一筆路由。請重試。", null);
                return false;
            }

            await recoveryJournal.ClearAsync();
        }

        var routesVerified = true;

        SharingBusStatus status;
        try
        {
            status = voicemeeterSharingBusService.GetStatus();
            sharingBusStatus = status;
        }
        catch (Exception)
        {
            requiresAttention = true;
            experimentalRoutingStatus = "暫時無法確認 B1 是否已關閉。請再點一次“停止分享”。";
            ShowError("停止分享後無法確認 B1 已關閉。請重試“停止分享”。", null);
            return false;
        }

        var resultingRouteState = StopVerificationPolicy.ResultingRouteState(status, routesVerified);
        if (resultingRouteState != SharingRouteState.LocalOnly)
        {
            requiresAttention = true;
            sharingRouteState = SharingRouteState.Sharing;
            experimentalRoutingStatus = "停止分享尚未確認。請重試“停止分享”。";
            ShowError("停止分享未完成：請確認 B1 已關閉且程序已回到僅自己聽，然後重試。", null);
            return false;
        }

        routeExecutor.CompletePersistentRouting();
        sharingRecoveryScope.Clear();

        hasOwnedRoutingTransaction = false;
        sharingRouteState = resultingRouteState;
        if (shareSession.Stop(
            DateTimeOffset.Now,
            stopReason ?? "已停止分享。",
            disconnected))
        {
            RecordCompletedShare(stopReason ?? "已停止分享。");
        }
        foreach (var session in GetRecoverySessions())
        {
            await routeCoordinator.UnshareAsync(session, token);
        }

        UpdateApplications(activeSessions);

        experimentalRoutingStatus = disconnected
            ? stopReason ?? "分享已斷開，已恢復分享前的播放路徑。"
            : "已停止分享，已恢復分享前的播放路徑。";
        requiresAttention = false;
        if (wasSharing && preferences.EndSharingSoundEnabled)
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        if (disconnected && preferences.DisconnectNotificationsEnabled)
        {
            trayIcon.ShowDisconnect(experimentalRoutingStatus);
        }
        motionController.PlayLocalOnlyConfirmed();
        AddActivity(disconnected ? "分享已斷開並恢復分享前的播放路徑。" : "已停止分享並取消勾選。");
        ErrorPanel.Visibility = Visibility.Collapsed;
        return true;
    }

    private async Task PersistRecoveryAsync(
        AudioSession selected,
        IReadOnlyList<ApplicationRouteSnapshot> snapshots,
        CancellationToken token)
    {
        if (snapshots.Count == 0 || string.IsNullOrWhiteSpace(inputDeviceId))
        {
            return;
        }

        await recoveryJournal.WriteAsync(
            new ShareRecoveryRecord(
                selected.ProcessName,
                selected.DisplayName,
                inputDeviceId,
                snapshots,
                DateTimeOffset.Now),
            token);
    }

    private async Task<IReadOnlyList<AudioSession>> GetRouteableSessionsAsync(
        IEnumerable<AudioSession> sessions,
        CancellationToken token)
    {
        var routeable = new List<AudioSession>();
        foreach (var session in sessions)
        {
            try
            {
                await routingHelper.GetRouteAsync(
                    session.ProcessId,
                    session.ProcessStartUtcTicks,
                    session.ProcessName,
                    token);
                routeable.Add(session);
            }
            catch (InvalidOperationException exception) when (
                exception.Message.Contains("Active output session not found", StringComparison.OrdinalIgnoreCase))
            {
                // Windows stores app routes by process identity, so a silent process can be prepared safely.
                routeable.Add(session);
            }
        }

        return routeable;
    }

    private async Task<bool> VerifyRoutePlanAsync(ApplicationRoutePlan plan, CancellationToken token)
    {
        foreach (var command in plan.Commands)
        {
            var actualRoute = await routingHelper.GetRouteAsync(
                command.ProcessId,
                command.ProcessStartUtcTicks,
                command.ProcessName,
                token);
            if (!ApplicationRouteVerifier.MatchesTarget(actualRoute, command.TargetDeviceId))
            {
                return false;
            }
        }

        return true;
    }

    private async Task StopSharingForSafetyAsync(string reason)
    {
        var wasSharing = shareSession.State == ShareSessionState.Sharing;
        if (!CanResetToLocalOnly())
        {
            if (hasOwnedRoutingTransaction && experimentalRoutingAvailable &&
                GetRecoverySessions().Count == 0)
            {
                if (!SetMainInputSharing(false))
                {
                    requiresAttention = true;
                    experimentalRoutingStatus = reason;
                    AddActivity(reason);
                    return;
                }

                pendingLocalOnlyReset = true;
                requiresAttention = false;
                if (shareSession.Stop(DateTimeOffset.Now, reason, disconnected: true))
                {
                    RecordCompletedShare(reason);
                }
                experimentalRoutingStatus = reason;
                AddActivity("分享程序已關閉，等待自動恢復本機播放。");
                if (wasSharing && preferences.EndSharingSoundEnabled)
                {
                    System.Media.SystemSounds.Asterisk.Play();
                }
                if (wasSharing && preferences.DisconnectNotificationsEnabled)
                {
                    trayIcon.ShowDisconnect(reason);
                }

                UpdateRoutingSetupState();
                return;
            }

            requiresAttention = true;
            experimentalRoutingStatus = reason;
            AddActivity(reason);
            return;
        }

        isRoutingOperation = true;
        try
        {
            if (!await StopSharingAndRestoreRoutesAsync(lifetimeCancellation.Token, disconnected: true, stopReason: reason))
            {
                requiresAttention = true;
                experimentalRoutingStatus = reason;
            }
            else
            {
                pendingLocalOnlyReset = false;
                AddActivity(reason);
            }
        }
        finally
        {
            isRoutingOperation = false;
        }
    }

    private void AddActivity(string message)
    {
        activityLog.Add($"{DateTime.Now:HH:mm} {message}");
        if (activityLog.Count > 3)
        {
            activityLog.RemoveAt(0);
        }

        RecentActivityText.Text = string.Join("  ·  ", activityLog);
    }

    private void UpdateShareStatistics()
    {
        var total = shareHistory.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration);
        if (currentShareStartedAt is { } startedAt)
        {
            total += DateTimeOffset.Now > startedAt ? DateTimeOffset.Now - startedAt : TimeSpan.Zero;
        }

        TotalShareDurationText.Text = $"累計分享 {total:hh\\:mm\\:ss} · {shareHistory.Count} 次";
    }

    private void RecordCompletedShare(string reason)
    {
        if (currentShareStartedAt is not { } startedAt || shareSession.FinalDuration <= TimeSpan.Zero)
        {
            currentShareStartedAt = null;
            currentShareProgramName = null;
            UpdateShareStatistics();
            return;
        }

        shareHistory.Add(new ShareHistoryEntry(
            startedAt,
            currentShareProgramName ?? "未知程序",
            shareSession.FinalDuration,
            reason));
        currentShareStartedAt = null;
        currentShareProgramName = null;
        shareHistoryStore.Save(shareHistory);
        UpdateShareStatistics();
    }

    private void ShareHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var entries = shareHistory
            .OrderByDescending(entry => entry.StartedAt)
            .Select(entry => $"{entry.StartedAt:MM-dd HH:mm}  {entry.ProgramName}  {entry.Duration:hh\\:mm\\:ss}  {entry.StopReason}");
        var total = shareHistory.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration);
        var message = $"累計分享 {total:hh\\:mm\\:ss}，共 {shareHistory.Count} 次。\n\n" +
                      (entries.Any() ? string.Join("\n", entries) : "還沒有完成的分享記錄。");
        FlowCastMessageDialog.Show(this, "分享歷史", message, FlowCastWindowTone.Soft);
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AudioApplicationRow row })
        {
            return;
        }

        var isFavorite = favoritePrograms.Toggle(row.Session.ProcessName);
        SaveFavoritePrograms();
        AddActivity(isFavorite ? $"已將 {row.DisplayName} 置頂。" : $"已取消 {row.DisplayName} 置頂。");
        UpdateApplications(activeSessions);
    }

    private async void HideProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AudioApplicationRow row })
        {
            return;
        }

        if (!FlowCastMessageDialog.Confirm(
                this,
                "不再顯示程序",
                $"確定不再顯示“{row.DisplayName}”嗎？\n之後可在“設置”中恢復。",
                "不再顯示",
                "取消"))
        {
            return;
        }

        preferences = preferences.Exclude(row.Session.ProcessName);
        preferencesStore.Save(preferences);
        await RefreshApplicationsOnlyAsync();
    }

    private static IReadOnlyCollection<string> LoadFavoritePrograms()
    {
        try
        {
            return File.Exists(FavoritesPath)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(FavoritesPath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void SaveFavoritePrograms()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FavoritesPath)!);
        File.WriteAllText(FavoritesPath, JsonSerializer.Serialize(favoritePrograms.Names));
    }

    private static IReadOnlyCollection<string> LoadApplicationOrder()
    {
        try
        {
            return File.Exists(ApplicationOrderPath)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(ApplicationOrderPath)) ?? []
                : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void SaveApplicationOrder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ApplicationOrderPath)!);
        File.WriteAllText(ApplicationOrderPath, JsonSerializer.Serialize(applicationOrder.ProcessNames));
    }

    private async Task<bool> ResetToLocalOnlyAsync(string status, CancellationToken? token = null)
    {
        if (!CanResetToLocalOnly())
        {
            return true;
        }

        isRoutingOperation = true;
        experimentalRoutingStatus = status;
        UpdateRoutingSetupState();
        try
        {
            var resetSucceeded = await StopSharingAndRestoreRoutesAsync(token ?? lifetimeCancellation.Token);
            if (resetSucceeded)
            {
                await RefreshSharingRouteStateAsync();
            }

            return resetSucceeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            experimentalRoutingStatus = "暫時無法確認聲音是否已回到只自己聽。";
            ShowError("FlowCast 沒有完成安全重置，請點擊“停止分享”再試一次。", exception);
            return false;
        }
        finally
        {
            isRoutingOperation = false;
            if (!isClosing)
            {
                UpdateRoutingSetupState();
            }
        }
    }

    private bool SetMainInputSharing(bool shared)
    {
        try
        {
            voicemeeterSharingBusService.SetMainInputShared(shared);
            return true;
        }
        catch (Exception exception)
        {
            requiresAttention = true;
            experimentalRoutingStatus = "暫時無法切換音樂分享通道。請確認 Banana 已打開後點擊“刷新”。";
            ShowError(shared
                ? "這次沒有開始分享，你的聲音不會被自動發送給朋友。"
                : "暫時無法確認已停止分享，請再點一次“停止分享”。", exception);
            return false;
        }
    }

    private void SetVoicemeeterBananaInstalled(bool installed)
    {
        voicemeeterBananaInstalled = installed;
        VoicemeeterRequirementPanel.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var status in ProcessStatuses)
        {
            status.IsInstalled = installed;
        }
    }

    private async void ApplicationRouteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: AudioApplicationRow row } ||
            !row.TryGetRouteChange(out var route) ||
            !row.CanEditRoute)
        {
            return;
        }

        try
        {
            if (route.IsWindowsDefault)
            {
                await routingHelper.ClearRouteAsync(
                    row.Session.ProcessId,
                    row.Session.ProcessStartUtcTicks,
                    row.Session.ProcessName,
                    lifetimeCancellation.Token);
            }
            else
            {
                await routingHelper.SetRouteAsync(
                    row.Session.ProcessId,
                    row.Session.ProcessStartUtcTicks,
                    row.Session.ProcessName,
                    route.DeviceId!,
                    lifetimeCancellation.Token);
            }

            row.MarkRouteApplied();
            experimentalRoutingStatus = $"已為 {row.DisplayName} 選擇“{route.DisplayName}”。暫停後重新播放即可生效；這不會開始分享。";
            AddActivity(experimentalRoutingStatus);
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            row.RevertRouteSelection();
            ShowError($"暫時無法為 {row.DisplayName} 修改播放路徑。請點擊“刷新”後再試。", exception);
        }
    }

    private async void FixToLocalRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AudioApplicationRow row } || !row.CanAdjustRoute || string.IsNullOrWhiteSpace(auxDeviceId))
        {
            return;
        }

        try
        {
            await routingHelper.SetRouteAsync(
                row.Session.ProcessId,
                row.Session.ProcessStartUtcTicks,
                row.Session.ProcessName,
                auxDeviceId,
                lifetimeCancellation.Token);
            row.MarkLocalRouteRequested();
            experimentalRoutingStatus = $"已為 {row.DisplayName} 設為僅自己聽。請暫停後重新播放，讓 Windows 使用 Voicemeeter AUX Input。";
            AddActivity(experimentalRoutingStatus);
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            ShowError($"暫時無法為 {row.DisplayName} 修改播放路徑。請點“刷新”後再試。", exception);
        }
    }

    private void ShowError(string message, Exception? exception)
    {
        ErrorText.Text = $"{message}\n下一步：請點擊“刷新”後再試；如果仍然失敗，請關閉並重新打開 FlowCast。";
        ErrorPanel.Visibility = Visibility.Visible;
    }
}

public sealed class AudioApplicationRow : INotifyPropertyChanged
{
    private bool isSelected;
    private bool localRouteRequested;
    private bool selectionEditingEnabled = true;
    private bool routeEditingEnabled;
    private bool routeChangedByUser;
    private bool isSharing;
    private AudioRouteOption appliedRoute;
    private AudioRouteOption selectedRoute;
    private readonly string? inputDeviceId;
    private readonly string? auxDeviceId;

    public AudioApplicationRow(
        AudioSession session,
        bool isProtected,
        bool isSelected,
        bool isFavorite,
        IReadOnlyList<ExternalAudioDevice> outputDevices,
        string? inputDeviceId,
        string? auxDeviceId)
    {
        Session = session;
        IsProtected = isProtected;
        this.isSelected = isSelected;
        IsFavorite = isFavorite;
        this.inputDeviceId = inputDeviceId;
        this.auxDeviceId = auxDeviceId;
        RouteOptions = CreateRouteOptions(outputDevices, inputDeviceId, auxDeviceId);
        appliedRoute = RouteOptions.FirstOrDefault(option =>
                           string.Equals(option.DeviceId, session.OutputDeviceId, StringComparison.OrdinalIgnoreCase)) ??
                       RouteOptions[0];
        selectedRoute = appliedRoute;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioSession Session { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Session.DisplayName) ? Session.ProcessName : Session.DisplayName;

    public string DetailText => $"{Session.ProcessName}  |  PID {Session.ProcessId}  |  {GetRouteDisplayText()}";

    public bool IsProtected { get; }

    public bool IsFavorite { get; }

    public IReadOnlyList<AudioRouteOption> RouteOptions { get; }

    public AudioRouteOption SelectedRoute
    {
        get => selectedRoute;
        set
        {
            if (value is null || Equals(selectedRoute, value))
            {
                return;
            }

            selectedRoute = value;
            routeChangedByUser = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RouteStatusText));
        }
    }

    public string FavoriteButtonText => IsFavorite ? "已置頂" : "置頂";

    public bool IsSelectable => !IsProtected;

    public bool CanRequestSelection => !IsProtected && selectionEditingEnabled && !IsSelected;

    public bool IsSharing => isSharing && IsSelected;

    public string RouteDisplayText => IsSharing ? "正在分享路徑" : "分享路徑";

    public bool CanEditRoute => !IsProtected && !IsSelected && routeEditingEnabled && RouteOptions.Count > 1;

    public string RouteStatusText => IsProtected
        ? "此程序受保護，不會被 FlowCast 路由。"
        : !Session.HasAudio
            ? "暫時沒有播放聲音；你仍可先選擇播放路徑，下一次播放會使用它。"
            : !routeEditingEnabled
                ? "分享進行中或正在切換，停止分享後才能調整播放路徑。"
                : "可直接選擇播放路徑；修改後暫停並重新播放即可生效，不會自動開始分享。";

    public bool CanAdjustRoute =>
        IsSelectable &&
        !localRouteRequested &&
        !string.IsNullOrWhiteSpace(auxDeviceId) &&
        !IsUsingVoicemeeterRoute();

    public string RouteHint => IsProtected
        ? "此程序受保護，不會被 FlowCast 路由。"
        : localRouteRequested
            ? "已設為僅自己聽；暫停後重新播放即可生效。"
            : CanAdjustRoute
                ? "當前走其他播放路徑。可在這裡改為僅自己聽；開始分享時會自動改到分享路徑。"
            : IsSelected && !Session.HasAudio
                ? "已選中；即使暫時沒有聲音也可以開始分享，下次播放會自動使用分享路徑。"
                : IsSelected
                    ? "已選中；開始分享時會自動切換到分享路徑。"
                    : "勾選後，開始分享會自動切換到分享路徑。";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionStatus));
            OnPropertyChanged(nameof(RouteHint));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(IsSharing));
            OnPropertyChanged(nameof(CanRequestSelection));
            OnPropertyChanged(nameof(CanEditRoute));
            OnPropertyChanged(nameof(RouteDisplayText));
        }
    }

    public string SelectionStatus => IsProtected
        ? "已排除：受保護的應用程序"
        : IsSelected ? "已選中" : "未選中";

    public void MarkLocalRouteRequested()
    {
        localRouteRequested = true;
        OnPropertyChanged(nameof(CanAdjustRoute));
        OnPropertyChanged(nameof(RouteHint));
    }

    public void SetRouteEditingEnabled(bool enabled)
    {
        if (routeEditingEnabled == enabled)
        {
            return;
        }

        routeEditingEnabled = enabled;
        OnPropertyChanged(nameof(CanEditRoute));
        OnPropertyChanged(nameof(RouteStatusText));
    }

    public void SetSelectionEditingEnabled(bool enabled)
    {
        if (selectionEditingEnabled == enabled)
        {
            return;
        }

        selectionEditingEnabled = enabled;
        OnPropertyChanged(nameof(CanRequestSelection));
    }

    public void SetSharingState(bool value)
    {
        if (isSharing == value)
        {
            return;
        }

        isSharing = value;
        OnPropertyChanged(nameof(IsSharing));
        OnPropertyChanged(nameof(DetailText));
    }

    public bool TryGetRouteChange(out AudioRouteOption route)
    {
        route = selectedRoute;
        if (!routeChangedByUser || Equals(appliedRoute, selectedRoute))
        {
            return false;
        }

        routeChangedByUser = false;
        return true;
    }

    public void MarkRouteApplied()
    {
        appliedRoute = selectedRoute;
        routeChangedByUser = false;
        OnPropertyChanged(nameof(RouteStatusText));
    }

    public void RevertRouteSelection()
    {
        selectedRoute = appliedRoute;
        routeChangedByUser = false;
        OnPropertyChanged(nameof(SelectedRoute));
        OnPropertyChanged(nameof(RouteStatusText));
    }

    private static IReadOnlyList<AudioRouteOption> CreateRouteOptions(
        IReadOnlyList<ExternalAudioDevice> outputDevices,
        string? inputDeviceId,
        string? auxDeviceId)
    {
        var options = new List<AudioRouteOption>
        {
            new(null, "跟隨 Windows 默認播放設備"),
        };

        foreach (var device in outputDevices
                     .Where(device => !string.IsNullOrWhiteSpace(device.Id))
                     .Where(device => !AudioRouteVisibilityPolicy.IsHiddenNonSharingDevice(device.Name))
                     // FlowCast owns these internal buses; never expose them as manual destinations.
                     .Where(device => !string.Equals(device.Id, inputDeviceId, StringComparison.OrdinalIgnoreCase))
                     .Where(device => !string.Equals(device.Id, auxDeviceId, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First())
                     .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new AudioRouteOption(device.Id, device.Name));
        }

        return options;
    }

    private bool IsUsingVoicemeeterRoute() =>
        !string.IsNullOrWhiteSpace(Session.OutputDeviceId) &&
        (string.Equals(Session.OutputDeviceId, inputDeviceId, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Session.OutputDeviceId, auxDeviceId, StringComparison.OrdinalIgnoreCase));

    private string GetRouteDisplayText() => IsSelected
        ? IsSharing ? "當前：正在分享路徑" : "當前：分享路徑"
        : $"當前：{GetOutputDeviceName()}";

    private string GetOutputDeviceName() => IsUsingVoicemeeterRoute()
        ? "由 FlowCast 管理"
        : AudioRouteVisibilityPolicy.IsHiddenNonSharingDevice(Session.OutputDeviceName)
        ? "已隱藏的非共享路徑"
            : RouteDisplayFormatter.Summarize(Session);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record HealthChip(string Key, string Title, HealthState State, string Message, string Accent, string Background, string BorderBrush)
{
    public static HealthChip From(HealthItem item) => item.State switch
    {
        HealthState.Ready => new HealthChip(item.Key, GetTitle(item.Key), item.State, item.Message, "#1DBE86", "#B9F7FFF9", "#A3DECF"),
        HealthState.Attention => new HealthChip(item.Key, GetTitle(item.Key), item.State, item.Message, "#E58A2B", "#FFF8E9", "#F0C886"),
        _ => new HealthChip(item.Key, GetTitle(item.Key), item.State, item.Message, "#718499", "#EEF3F7", "#C8D5DF"),
    };

    private static string GetTitle(string key) => key switch
    {
        "A1" => "A1 / 默認播放",
        _ => key,
    };
}

public sealed record AudioRouteOption(string? DeviceId, string DisplayName)
{
    public bool IsWindowsDefault => string.IsNullOrWhiteSpace(DeviceId);
}

public sealed class ProcessStatus : INotifyPropertyChanged
{
    private bool isRunning;
    private bool? isInstalled;

    public ProcessStatus(string displayName, string processName)
    {
        DisplayName = displayName;
        ProcessName = processName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayName { get; }

    public string ProcessName { get; }

    public bool IsRunning
    {
        get => isRunning;
        set
        {
            if (isRunning == value)
            {
                return;
            }

            isRunning = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public bool? IsInstalled
    {
        get => isInstalled;
        set
        {
            if (isInstalled == value)
            {
                return;
            }

            isInstalled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    public string StatusText => IsInstalled switch
    {
        false => "未安裝",
        true when IsRunning => "已安裝，正在運行",
        true => "已安裝，尚未開啟",
        _ => "正在檢查",
    };
}
