using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.App;

public partial class MainWindow : Window
{
    private const string VoicemeeterBananaDownloadUrl = "https://vb-audio.com/Voicemeeter/banana.htm";
    private const string DiscordVoiceVideoSettingsUri = "discord://-/settings/voice";
    private static readonly TimeSpan ResetRecoveryRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PassiveRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SharingRefreshInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SignalRefreshInterval = TimeSpan.FromMilliseconds(500);
    private static readonly string FavoritesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowCast",
        "favorites.json");

    private readonly IAudioSessionDiscovery discovery = new WasapiAudioSessionDiscovery();
    private readonly RouteCoordinator routeCoordinator = new();
    private readonly SharingRecoveryScope sharingRecoveryScope = new();
    private readonly ExternalRoutingHelperClient routingHelper = new(Path.Combine(AppContext.BaseDirectory, "router-helper"));
    private readonly IApplicationRouteExecutor routeExecutor;
    private readonly DefaultPlaybackDeviceService defaultPlaybackDeviceService = new();
    private readonly VoicemeeterSharingBusService voicemeeterSharingBusService = new();
    private readonly FlowCastHealthProbe healthProbe;
    private readonly DispatcherTimer refreshTimer = new() { Interval = PassiveRefreshInterval };
    private readonly DispatcherTimer signalTimer = new() { Interval = SignalRefreshInterval };
    private readonly DispatcherTimer scheduleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ShareStopSchedule stopSchedule = new();
    private readonly FavoritePrograms favoritePrograms;
    private readonly FlowCastPreferencesStore preferencesStore = new();
    private FlowCastPreferences preferences;
    private readonly MotionController motionController;
    private readonly List<string> activityLog = [];
    private IReadOnlyList<AudioSession> activeSessions = [];
    private string? inputDeviceId;
    private string? auxDeviceId;
    private string experimentalRoutingStatus = "正在检查音频路由组件和输出设备。";
    private bool isRefreshing;
    private bool isRoutingOperation;
    private bool isClosing;
    private bool isUpdateStaging;
    private bool closeAfterRouting;
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
    private HealthSummary healthSummary = HealthSummary.Create(false, false, false, false, false);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        routeExecutor = new ApplicationRouteExecutor(routingHelper);
        healthProbe = new FlowCastHealthProbe(routingHelper);
        favoritePrograms = new FavoritePrograms(LoadFavoritePrograms());
        preferences = preferencesStore.Load();
        motionController = new MotionController(
            this,
            LogoMark,
            TopStatusCard,
            StatusPulse,
            B1MeterFill,
            B1ActivityBars,
            RouteFlowPath,
            RouteBeaconOne,
            RouteBeaconTwo,
            ApplicationListPanel,
            TimerProgressGlow,
            preferences.ReduceMotion);

        ProcessStatuses.Add(new ProcessStatus("Voicemeeter Banana", "voicemeeterpro"));

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        refreshTimer.Tick += RefreshTimer_Tick;
        signalTimer.Tick += SignalTimer_Tick;
        scheduleTimer.Tick += ScheduleTimer_Tick;
    }

    public ObservableCollection<AudioApplicationRow> Applications { get; } = [];

    public ObservableCollection<ProcessStatus> ProcessStatuses { get; } = [];

    public ObservableCollection<HealthChip> HealthItems { get; } = [];

    public void SetUpdateAvailable(bool isAvailable) =>
        UpdateButton.Visibility = isAvailable ? Visibility.Visible : Visibility.Collapsed;

    public void SetUpdateStaging(bool isStaging) => isUpdateStaging = isStaging;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await StartVoicemeeterBananaIfInstalledAsync();
        await RefreshAsync();
        motionController.PlayLaunch();
        SetDefaultPlaybackToAux();
        SetMainInputSharing(false);
        await ResetToLocalOnlyAsync("已打开 FlowCast，正在重置为只自己听。");
        AddActivity("FlowCast 已启动，当前为只自己听。");
        refreshTimer.Start();
        signalTimer.Start();
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

        if (isRoutingOperation)
        {
            e.Cancel = true;
            if (!closeAfterRouting)
            {
                closeAfterRouting = true;
                _ = CloseAfterRoutingCompletesAsync();
            }

            experimentalRoutingStatus = "正在完成音频回复，完成后会自动关闭。";
            UpdateRoutingSetupState();
            return;
        }

        if (CanResetToLocalOnly())
        {
            e.Cancel = true;
            experimentalRoutingStatus = "正在重置为只自己听，然后关闭窗口...";
            UpdateRoutingSetupState();
            try
            {
                if (!await ResetToLocalOnlyAsync("正在重置为只自己听，然后关闭窗口...", CancellationToken.None))
                {
                    return;
                }

                ErrorPanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                experimentalRoutingStatus = $"停止分享失败，已取消关闭：{exception.Message}";
                ShowError("未能让声音回到仅本机收听。请重试“停止分享，只自己听”。", null);
                return;
            }
            finally
            {
                isRoutingOperation = false;
                UpdateRoutingSetupState();
            }

            PrepareForClose();
            Close();
            return;
        }

        PrepareForClose();
    }

    private void PrepareForClose()
    {
        isClosing = true;
        motionController.Suspend();
        refreshTimer.Stop();
        signalTimer.Stop();
        scheduleTimer.Stop();
        lifetimeCancellation.Cancel();
    }

    private async void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            wasMinimized = true;
            motionController.Suspend();
        }
        else
        {
            motionController.Resume();
            if (wasMinimized)
            {
                wasMinimized = false;
                motionController.PlayWindowResume();
            }
        }

        if (WindowState == WindowState.Minimized && !CanStopSharing() && !stopSchedule.IsScheduled)
        {
            refreshTimer.Stop();
            return;
        }

        if (!isClosing && !refreshTimer.IsEnabled)
        {
            refreshTimer.Start();
            await RefreshAsync(refreshRouting: false);
        }
    }

    private async Task CloseAfterRoutingCompletesAsync()
    {
        while (isRoutingOperation && !isClosing)
        {
            await Task.Delay(100);
        }

        if (closeAfterRouting && !isClosing)
        {
            closeAfterRouting = false;
            if (IsConfirmedLocalOnly())
            {
                PrepareForClose();
            }

            Close();
        }
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshAsync(refreshRouting: false);
        await StopWhenScheduleExpiresAsync();
    }

    private async void ScheduleTimer_Tick(object? sender, EventArgs e)
    {
        await StopWhenScheduleExpiresAsync();
        if (!stopSchedule.IsScheduled)
        {
            scheduleTimer.Stop();
        }

        UpdateRoutingSetupState();
    }

    private void SignalTimer_Tick(object? sender, EventArgs e)
    {
        if (isClosing)
        {
            return;
        }

        try
        {
            sharingBusStatus = voicemeeterSharingBusService.GetStatus();
            ReconcileSharingStateWithB1();
            if (sharingRouteState == SharingRouteState.Sharing && sharingBusStatus.IsMainInputShared)
            {
                motionController.PlayAudioLevelPulse(Math.Clamp(sharingBusStatus.B1Level * 100f, 0f, 100f));
            }
            UpdateRoutingSetupState();
        }
        catch
        {
            sharingBusStatus = null;
            UpdateRoutingSetupState();
        }
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

    private async Task StartVoicemeeterBananaIfInstalledAsync()
    {
        if (IsVoicemeeterBananaRunning() ||
            !VoicemeeterBananaInstallationDetector.TryGetExecutablePath(out var executablePath) ||
            string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            for (var attempt = 0; attempt < 20 && !IsVoicemeeterBananaRunning(); attempt++)
            {
                await Task.Delay(250);
            }
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable($"Voicemeeter Banana 已安裝但無法自動啟動：{exception.Message}");
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
        await RefreshApplicationsOnlyAsync();
    }

    private async void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(await BuildDiagnosticsReportAsync());
            AddActivity("已复制诊断信息。");
        }
        catch (Exception exception)
        {
            ShowError("无法复制诊断信息。", exception);
        }
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
            ShowError("无法打开 Voicemeeter Banana 官方下载页面。", exception);
        }
    }

    private void OpenDiscordVoiceVideoSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DiscordVoiceVideoSettingsUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError("无法打开 Discord 的“语音和视频”设置。", exception);
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
            var refreshRouteState = refreshRouting || sharingRouteState == SharingRouteState.Sharing;
            if (refreshRouteState)
            {
                await RefreshSharingRouteStateAsync();
            }

            var shouldRecoverPendingReset = pendingLocalOnlyReset && GetRoutableActiveSessions().Count > 0;
            if (selectedProgramClosed || shouldRecoverPendingReset ||
                RouteSafetyPolicy.ShouldStopSharing(wasSharing, endpointsChanged))
            {
                await StopSharingForSafetyAsync(endpointsChanged
                    ? "Voicemeeter 音频设备已变化，已停止分享。"
                    : selectedProgramClosed
                        ? "分享程序已关闭，已停止分享。"
                        : "分享路由异常，已停止分享。");
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
            ShowError("无法检测当前正在播放音频的程序。", exception);
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

        foreach (var session in favoritePrograms.Order(eligibleSessions.Where(session => session.HasAudio)))
        {
            var isProtected = AudioRoutingPolicy.IsProtectedProcess(session.ProcessName);
            Applications.Add(new AudioApplicationRow(
                session,
                isProtected,
                !isProtected && routeCoordinator.IsSelected(session),
                favoritePrograms.Contains(session.ProcessName)));
        }

        EmptyStateText.Visibility = Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "当前没有检测到正在播放音频的程序。";
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
            ShowError("无法刷新程序列表。", exception);
        }
    }

    private async void ApplicationSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not AudioApplicationRow row || !row.IsSelectable)
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
            var result = shouldShare
                ? await routeCoordinator.ShareAsync(row.Session, lifetimeCancellation.Token)
                : await routeCoordinator.UnshareAsync(row.Session, lifetimeCancellation.Token);

            row.IsSelected = routeCoordinator.IsSelected(row.Session);
            if (!result.Succeeded)
            {
                ShowError(result.Message ?? "无法更新本地勾选状态。", null);
                return;
            }

            ErrorPanel.Visibility = Visibility.Collapsed;
            motionController.PlaySelectionConfirmed(checkBox);
            if (!shouldShare)
            {
                return;
            }
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            row.IsSelected = routeCoordinator.IsSelected(row.Session);
            ShowError("无法更新本地勾选状态或打开 Windows 音量混音器。", exception);
        }
        finally
        {
            if (!isClosing)
            {
                checkBox.IsEnabled = true;
                UpdateRoutingSetupState();
            }
        }
    }

    private void UpdateRoutingSetupState()
    {
        var selected = GetSelectedSessions();
        motionController.SetRouteActive(sharingRouteState == SharingRouteState.Sharing);
        SetupSelectedButton.IsEnabled = voicemeeterBananaInstalled && selected.Count > 0;
        InstructionText.Text = AudioRoutingPolicy.GetSetupInstruction(selected);
        ExperimentalRoutingStatusText.Text = experimentalRoutingStatus;
        B1StatusText.Text = GetB1StatusText();
        var b1Percent = Math.Clamp((sharingBusStatus?.B1Level ?? 0f) * 100f, 0f, 100f);
        B1MeterFill.Width = b1Percent;
        B1MeterValueText.Text = $"{b1Percent:0}%";
        UpdateHealthChips();
        (FlowCastStatusText.Text, FlowCastStatusHintText.Text) = requiresAttention
            ? ("需要处理", experimentalRoutingStatus)
            : sharingRouteState switch
        {
            var state when state == SharingRouteState.Sharing => ("正在分享", "至少一个正在播放的程序会发送给朋友。"),
            var state when state == SharingRouteState.LocalOnly => ("只自己听到", "当前所有正在播放的程序都只在本机播放。"),
            _ => ("未确认", "正在检查程序的分享状态；确认前不会启用停止分享。"),
        };
        ApplyRoutingButton.IsEnabled = CanApplyRouting();
        RestoreRoutingButton.IsEnabled = CanStopSharing();
        ScheduleStopButton.IsEnabled = true;
        StartStopTimerButton.IsEnabled = CanStopSharing();
        TimerPanel.Visibility = stopSchedule.IsScheduled || TimerPanel.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        TimerStatusText.Text = stopSchedule.Remaining(DateTimeOffset.Now) is { } remaining
            ? $"将在 {Math.Ceiling(remaining.TotalMinutes)} 分钟后自动停止分享。"
            : CanStopSharing()
                ? "未设置定时停止。"
                : "请先开始分享，再启动计时。";
        refreshTimer.Interval = pendingLocalOnlyReset
            ? ResetRecoveryRefreshInterval
            : sharingRouteState == SharingRouteState.Sharing
                ? SharingRefreshInterval
                : PassiveRefreshInterval;

        if (requiresAttention && !wasAttention)
        {
            motionController.PlayAttention();
        }

        wasAttention = requiresAttention;
    }

    private IReadOnlyList<AudioSession> GetSelectedSessions() =>
        routeCoordinator.GetSelectedSessions(activeSessions)
            .Where(session => !preferences.IsExcluded(session.ProcessName))
            .ToArray();

    private IReadOnlyList<AudioSession> GetRoutableActiveSessions() =>
        activeSessions
            .Where(session => !AudioRoutingPolicy.IsProtectedProcess(session.ProcessName))
            .Where(session => !preferences.IsExcluded(session.ProcessName))
            .ToArray();

    private IReadOnlyList<AudioSession> GetRecoverySessions() =>
        sharingRecoveryScope.IncludeCurrent(
            activeSessions.Where(session => !AudioRoutingPolicy.IsProtectedProcess(session.ProcessName)));

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
            !string.IsNullOrWhiteSpace(auxDeviceId),
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
    }

    private string GetB1StatusText()
    {
        if (sharingBusStatus is null)
        {
            return "B1：无法读取 Banana 状态";
        }

        var signal = $"Input 音量 {(sharingBusStatus.InputLevel * 100):0}%";
        if (sharingBusStatus.IsMainInputShared)
        {
            return $"B1：正在分享 · {signal}";
        }

        return $"B1：仅自己听 · {signal}";
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
                experimentalRoutingStatus = "检测到 Windows 音频路由已变更，无法确认分享。请重新开始分享或停止分享。";
                AddActivity("分享路由已在外部变更，等待你确认处理。");
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
                experimentalRoutingStatus = "无法重新确认分享路由。请停止分享以恢复只自己听。";
                AddActivity("分享路由暂时无法读取，已保留停止分享入口。");
            }
        }
    }

    private void ReconcileSharingStateWithB1()
    {
        if (sharingRouteState != SharingRouteState.Sharing ||
            sharingBusStatus is { IsMainInputShared: true, IsAuxShared: false })
        {
            return;
        }

        sharingRouteState = SharingRouteState.Unknown;
        requiresAttention = true;
        experimentalRoutingStatus = sharingBusStatus is { IsAuxShared: true }
            ? "检测到 AUX 也已发送到 B1。为避免误分享，请停止分享。"
            : "检测到 Banana 的 B1 已关闭，无法确认分享。请重新开始分享或停止分享。";
        AddActivity("Banana 的 B1 状态已变更，分享状态需要确认。");
    }

    private async Task<bool> RefreshExperimentalRoutingAvailabilityAsync()
    {
        if (RoutingRefreshDisplay.ShouldShowCheckingMessage(experimentalRoutingAvailable))
        {
            experimentalRoutingStatus = "正在检查音频路由组件和输出设备。";
            UpdateRoutingSetupState();
        }

        var probe = await healthProbe.CheckAsync(lifetimeCancellation.Token);
        healthSummary = probe.Summary;
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
                    ? "Voicemeeter Banana 已安装，但必要的音频设备尚未就绪。请重新启动电脑后再刷新。"
                    : "未检测到 Voicemeeter Banana。请安装后重启电脑，再重新检测。");
                return false;
            }

            var endpointsChanged = !string.IsNullOrWhiteSpace(inputDeviceId) &&
                                   (!string.Equals(inputDeviceId, probe.Endpoints.Input.Id, StringComparison.OrdinalIgnoreCase) ||
                                    !string.Equals(auxDeviceId, probe.Endpoints.AuxInput.Id, StringComparison.OrdinalIgnoreCase));
            if (!healthSummary.IsReady("Banana"))
            {
                SetExperimentalRoutingUnavailable(probe.BananaInstalled
                    ? "Voicemeeter Banana 已安装但尚未运行。请打开 Banana 后重新检测。"
                    : "未检测到 Voicemeeter Banana。请安装后重启电脑，再重新检测。");
                return false;
            }

            inputDeviceId = probe.Endpoints.Input.Id;
            auxDeviceId = probe.Endpoints.AuxInput.Id;
            experimentalRoutingAvailable = true;
            experimentalRoutingStatus = hasOwnedRoutingTransaction
                ? "本次音频路由已生效。再次应用会按当前勾选替换；停止分享会让声音只在本机播放。"
                : "音频路由已就绪。点击应用并确认后才会更改 Windows 路由；停止分享会让声音只在本机播放。";
            requiresAttention = false;
            return endpointsChanged;
        }
        catch (OperationCanceledException) when (isClosing)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable($"检测输出设备失败：{exception.Message}");
            return false;
        }

    }

    private async void ApplyRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSessions();
        if (!CanApplyRouting())
        {
            UpdateRoutingSetupState();
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
            experimentalRoutingStatus = "有未选中的程序无法由 Windows 路由。为避免误分享，未开启 B1。";
            ShowError("请先关闭未选中的音频程序，再开始分享。", null);
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
            SetExperimentalRoutingUnavailable($"生成路由计划失败：{exception.Message}");
            ShowError("未应用音频路由。", null);
            return;
        }

        var confirmation = ShareConfirmation.Create(routableSessions, selected);
        if (new ShareConfirmationWindow(this, confirmation).ShowDialog() != true)
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
                        $"无法替换路由：{restoreResult.Message}",
                        null);
                    return;
                }

                hasOwnedRoutingTransaction = false;
            }

            if (plan.Commands.Count > 0)
            {
                sharingRecoveryScope.Track(routeableSessions);
                var result = await routeExecutor.ApplyAsync(plan, lifetimeCancellation.Token);
                if (!result.Succeeded)
                {
                    hasOwnedRoutingTransaction = result.HasPendingTransaction;
                    await RecoverFromShareStartFailureAsync($"应用失败：{result.Message}", null);
                    return;
                }

                hasOwnedRoutingTransaction = result.HasPendingTransaction;
                if (!await VerifyRoutePlanAsync(plan, lifetimeCancellation.Token))
                {
                    await RecoverFromShareStartFailureAsync("分享路由未完全写入", null);
                    return;
                }
            }

            if (!SetMainInputSharing(true))
            {
                await RecoverFromShareStartFailureAsync("无法开启 B1 分享通道", null);
                return;
            }

            sharingRouteState = SharingRouteState.Sharing;
            motionController.PlaySharingConfirmed();
            experimentalRoutingStatus = "音频路由已应用。更改勾选后再次应用即可替换；停止分享会让声音只在本机播放。";
            AddActivity("分享已确认。");
            ErrorPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(this, "分享已确认", "FlowCast", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            await RecoverFromShareStartFailureAsync($"应用失败：{exception.Message}", exception);
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
            recovered = await StopSharingAndKeepLocalOnlyAsync(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (isClosing)
        {
            throw;
        }
        catch (Exception recoveryException)
        {
            requiresAttention = true;
            SetExperimentalRoutingUnavailable($"{failureReason}；恢复只自己听时出错：{recoveryException.Message}");
            ShowError("未开始分享，无法恢复只自己听，请重试。", recoveryException);
            return;
        }

        var outcome = ShareStartRecoveryPolicy.FromRecoveryResult(recovered);
        requiresAttention = outcome.RequiresAttention;
        if (outcome.LocalOnlyRecovered)
        {
            experimentalRoutingStatus = "未开始分享，已恢复只自己听。";
            ShowError("未开始分享，已恢复只自己听。", null);
            return;
        }

        SetExperimentalRoutingUnavailable($"{failureReason}；无法恢复只自己听。");
        ShowError("未开始分享，无法恢复只自己听，请重试。", exception);
    }

    private async void RestoreRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStopSharing())
        {
            UpdateRoutingSetupState();
            return;
        }

        var confirmationText = "停止分享后，所有当前检测到的程序都会改为只在你的耳机播放，不会送到 B1。\n\n" +
                               "现在停止分享吗？";
        if (MessageBox.Show(confirmationText, "停止分享，只自己听", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        isRoutingOperation = true;
        UpdateRoutingSetupState();
        try
        {
            await StopSharingAndKeepLocalOnlyAsync(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable($"停止分享失败：{exception.Message}");
            ShowError("无法停止分享。请重新检测后再试。", null);
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

    private void SetExperimentalRoutingUnavailable(string reason)
    {
        experimentalRoutingAvailable = false;
        inputDeviceId = null;
        auxDeviceId = null;
        sharingRouteState = SharingRouteState.Unknown;
        requiresAttention = true;
        experimentalRoutingStatus = $"音频路由不可用：{reason}";
        UpdateRoutingSetupState();
    }

    private async Task<bool> StopSharingAndKeepLocalOnlyAsync(CancellationToken token)
    {
        if (!voicemeeterBananaInstalled || !experimentalRoutingAvailable ||
            string.IsNullOrWhiteSpace(inputDeviceId) || string.IsNullOrWhiteSpace(auxDeviceId))
        {
            ShowError("无法停止分享：Voicemeeter Banana 尚未就绪。", null);
            return false;
        }

        if (!SetDefaultPlaybackToAux())
        {
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
                experimentalRoutingStatus = $"停止分享失败：{restoreResult.Message}";
                ShowError("无法先清除上一笔路由。请重试。", null);
                return false;
            }
        }

        var routableSessions = GetRecoverySessions();
        var routeableSessions = await GetRouteableSessionsAsync(routableSessions, token);
        var routesVerified = true;
        if (routeableSessions.Count > 0)
        {
            var localOnlyPlan = ApplicationRoutePlanner.Create(routeableSessions, [], inputDeviceId, auxDeviceId);
            var applyResult = await routeExecutor.ApplyAsync(localOnlyPlan, token);
            hasOwnedRoutingTransaction = applyResult.HasPendingTransaction;
            if (!applyResult.Succeeded)
            {
                experimentalRoutingStatus = $"停止分享失败：{applyResult.Message}";
                ShowError("无法将所有程序改为仅本机收听。", null);
                return false;
            }

            routesVerified = await VerifyRoutePlanAsync(localOnlyPlan, token);
            if (!routesVerified)
            {
                requiresAttention = true;
                experimentalRoutingStatus = "未能确认所有程序已回到本机收听。";
                ShowError("停止分享后无法验证音频路由。", null);
                return false;
            }

        }

        SharingBusStatus status;
        try
        {
            status = voicemeeterSharingBusService.GetStatus();
            sharingBusStatus = status;
        }
        catch (Exception exception)
        {
            requiresAttention = true;
            experimentalRoutingStatus = $"停止分享失败：无法确认 B1 状态：{exception.Message}";
            ShowError("停止分享后无法确认 B1 已关闭。请重试“停止分享”。", null);
            return false;
        }

        var resultingRouteState = StopVerificationPolicy.ResultingRouteState(status, routesVerified);
        if (resultingRouteState != SharingRouteState.LocalOnly)
        {
            requiresAttention = true;
            sharingRouteState = SharingRouteState.Sharing;
            experimentalRoutingStatus = "停止分享尚未确认。请重试“停止分享”。";
            ShowError("停止分享未完成：请确认 B1 已关闭且程序已回到仅自己听，然后重试。", null);
            return false;
        }

        routeExecutor.CompletePersistentRouting();
        sharingRecoveryScope.Clear();

        hasOwnedRoutingTransaction = false;
        sharingRouteState = resultingRouteState;
        foreach (var session in GetRecoverySessions())
        {
            await routeCoordinator.UnshareAsync(session, token);
        }

        UpdateApplications(activeSessions);

        experimentalRoutingStatus = "已停止分享。所有当前检测到的程序只会在本机播放。";
        requiresAttention = false;
        motionController.PlayLocalOnlyConfirmed();
        stopSchedule.Cancel();
        AddActivity("已停止分享并取消勾选。");
        ErrorPanel.Visibility = Visibility.Collapsed;
        return true;
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
                // The app already sends audio directly to Voicemeeter Input, so B1 is its safe gate.
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
        if (!CanResetToLocalOnly())
        {
            if (hasOwnedRoutingTransaction && experimentalRoutingAvailable &&
                GetRecoverySessions().Count == 0)
            {
                pendingLocalOnlyReset = true;
                requiresAttention = false;
                experimentalRoutingStatus = "已检测到分享程序关闭。下次检测到音频时会自动恢复为只自己听。";
                AddActivity("分享程序已关闭，等待自动恢复本机播放。");
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
            if (!await StopSharingAndKeepLocalOnlyAsync(lifetimeCancellation.Token))
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

    private void ScheduleStopButton_Click(object sender, RoutedEventArgs e)
    {
        TimerPanel.Visibility = Visibility.Visible;
        TimerMinutesText.Focus();
    }

    private void TimerPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var minutes) && ShareTimerPresets.Minutes.Contains(minutes))
        {
            TimerMinutesText.Text = minutes.ToString();
            TimerPanel.Visibility = Visibility.Visible;
            TimerStatusText.Text = $"已选择 {minutes} 分钟，点击“开始计时”后生效。";
        }
    }

    private void StartStopTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStopSharing() || !int.TryParse(TimerMinutesText.Text, out var minutes) || minutes < 1 || minutes > 720)
        {
            ShowError("请输入 1 到 720 分钟的定时停止时间。", null);
            return;
        }

        stopSchedule.Schedule(TimeSpan.FromMinutes(minutes), DateTimeOffset.Now);
        scheduleTimer.Start();
        motionController.PlayTimerScheduled();
        AddActivity($"已设置 {minutes} 分钟后自动停止分享。");
        ErrorPanel.Visibility = Visibility.Collapsed;
        UpdateRoutingSetupState();
    }

    private void CancelStopTimerButton_Click(object sender, RoutedEventArgs e)
    {
        stopSchedule.Cancel();
        scheduleTimer.Stop();
        TimerPanel.Visibility = Visibility.Collapsed;
        AddActivity("已取消定时停止。");
        UpdateRoutingSetupState();
    }

    private async Task StopWhenScheduleExpiresAsync()
    {
        if (!stopSchedule.IsDue(DateTimeOffset.Now) || isClosing || isRoutingOperation || isRefreshing)
        {
            return;
        }

        AddActivity("定时停止时间已到。");
        if (!CanResetToLocalOnly())
        {
            requiresAttention = true;
            experimentalRoutingStatus = "定时已到，正在等待安全路由状态后停止分享。";
            return;
        }

        isRoutingOperation = true;
        try
        {
            if (!await StopSharingAndKeepLocalOnlyAsync(lifetimeCancellation.Token))
            {
                requiresAttention = true;
                experimentalRoutingStatus = "定时停止未完成，将继续重试。";
            }
        }
        finally
        {
            isRoutingOperation = false;
        }

        UpdateRoutingSetupState();
    }

    private async Task<string> BuildDiagnosticsReportAsync()
    {
        var lines = new List<string>
        {
            "FlowCast 诊断报告",
            $"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Banana：{(voicemeeterBananaInstalled ? "已检测到" : "未检测到")}",
            $"状态：{FlowCastStatusText.Text}",
            $"Input：{inputDeviceId ?? "未检测到"}",
            $"AUX：{auxDeviceId ?? "未检测到"}",
            "音频程序：",
        };

        foreach (var session in GetRoutableActiveSessions())
        {
            try
            {
                var route = await routingHelper.GetRouteAsync(
                    session.ProcessId,
                    session.ProcessStartUtcTicks,
                    session.ProcessName,
                    lifetimeCancellation.Token);
                lines.Add($"- {session.DisplayName} ({session.ProcessName}, PID {session.ProcessId}): {SharingRouteState.Classify(route, inputDeviceId, auxDeviceId) switch { var state when state == SharingRouteState.Sharing => "分享", var state when state == SharingRouteState.LocalOnly => "仅本机", _ => "未确认" }}");
            }
            catch
            {
                lines.Add($"- {session.DisplayName} ({session.ProcessName}, PID {session.ProcessId}): 无法读取路由");
            }
        }

        return string.Join(Environment.NewLine, lines);
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

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AudioApplicationRow row })
        {
            return;
        }

        var isFavorite = favoritePrograms.Toggle(row.Session.ProcessName);
        SaveFavoritePrograms();
        AddActivity(isFavorite ? $"已将 {row.DisplayName} 置顶。" : $"已取消 {row.DisplayName} 置顶。");
        UpdateApplications(activeSessions);
    }

    private async void HideProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AudioApplicationRow row })
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定不再显示“{row.DisplayName}”吗？\n之后可在“设置”中恢复。",
            "不再显示程序",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
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
            var resetSucceeded = await StopSharingAndKeepLocalOnlyAsync(token ?? lifetimeCancellation.Token);
            if (resetSucceeded)
            {
                await RefreshSharingRouteStateAsync();
            }

            return resetSucceeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            experimentalRoutingStatus = $"重置为只自己听失败：{exception.Message}";
            ShowError("无法重置音频路由。", exception);
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

    private bool SetDefaultPlaybackToAux()
    {
        if (string.IsNullOrWhiteSpace(auxDeviceId))
        {
            return false;
        }

        try
        {
            defaultPlaybackDeviceService.SetDefaultForAllRoles(auxDeviceId);
            return true;
        }
        catch (Exception exception)
        {
            requiresAttention = true;
            experimentalRoutingStatus = $"无法将 Windows 默认播放设备设为 AUX：{exception.Message}";
            ShowError("无法完成本机收听保护。请重新检测后重试。", exception);
            return false;
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
            experimentalRoutingStatus = $"无法更新 Voicemeeter B1：{exception.Message}";
            ShowError("无法更新音乐分享通道。请重新检测后重试。", exception);
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

    private void SetupSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSessions();
        if (selected.Count == 0)
        {
            UpdateRoutingSetupState();
            return;
        }

        try
        {
            VolumeMixerLauncher.Open();
            ErrorPanel.Visibility = Visibility.Collapsed;
            InstructionText.Text = AudioRoutingPolicy.GetSetupInstruction(selected);
        }
        catch (Exception exception)
        {
            ShowError("无法打开 Windows 音量混音器。", exception);
        }
    }

    private void ShowError(string message, Exception? exception)
    {
        ErrorText.Text = exception is null ? message : $"{message} {exception.Message}";
        ErrorPanel.Visibility = Visibility.Visible;
    }
}

public sealed class AudioApplicationRow : INotifyPropertyChanged
{
    private bool isSelected;

    public AudioApplicationRow(AudioSession session, bool isProtected, bool isSelected, bool isFavorite)
    {
        Session = session;
        IsProtected = isProtected;
        this.isSelected = isSelected;
        IsFavorite = isFavorite;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioSession Session { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Session.DisplayName) ? Session.ProcessName : Session.DisplayName;

    public string DetailText => $"{Session.ProcessName}  |  PID {Session.ProcessId}";

    public bool IsProtected { get; }

    public bool IsFavorite { get; }

    public string FavoriteButtonText => IsFavorite ? "已置顶" : "置顶";

    public bool IsSelectable => !IsProtected;

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
        }
    }

    public string SelectionStatus => IsProtected
        ? "已排除：受保护的应用程序"
        : IsSelected ? "已选中" : "未选中";

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
        "A1" => "A1 / 默认播放",
        _ => key,
    };
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
        false => "未安装",
        true when IsRunning => "已安装，正在运行",
        true => "已安装，尚未开启",
        _ => "正在检查",
    };
}
