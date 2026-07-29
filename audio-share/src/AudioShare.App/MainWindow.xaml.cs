using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.App;

public partial class MainWindow : Window
{
    private readonly IAudioSessionDiscovery discovery = new WasapiAudioSessionDiscovery();
    private readonly RouteCoordinator routeCoordinator = new();
    private readonly ExternalRoutingHelperClient routingHelper = new(Path.Combine(AppContext.BaseDirectory, "router-helper"));
    private readonly IApplicationRouteExecutor routeExecutor;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private IReadOnlyList<AudioSession> activeSessions = [];
    private string? inputDeviceId;
    private string? auxDeviceId;
    private string experimentalRoutingStatus = "Experimental routing is unavailable until refresh completes.";
    private bool isRefreshing;
    private bool isRoutingOperation;
    private bool isClosing;
    private bool experimentalRoutingAvailable;
    private bool hasSuccessfulRoutingTransaction;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        routeExecutor = new ApplicationRouteExecutor(routingHelper);

        ProcessStatuses.Add(new ProcessStatus("Voicemeeter Banana", "voicemeeterpro"));

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        refreshTimer.Tick += RefreshTimer_Tick;
    }

    public ObservableCollection<AudioApplicationRow> Applications { get; } = [];

    public ObservableCollection<ProcessStatus> ProcessStatuses { get; } = [];

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        refreshTimer.Start();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        isClosing = true;
        refreshTimer.Stop();
        lifetimeCancellation.Cancel();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (isRefreshing || isClosing)
        {
            return;
        }

        isRefreshing = true;
        RefreshButton.IsEnabled = false;
        UpdateRoutingSetupState();

        try
        {
            UpdateProcessStatuses();
            var sessions = await discovery.GetActiveSessionsAsync(lifetimeCancellation.Token);
            activeSessions = sessions;
            UpdateApplications(sessions);
            await RefreshExperimentalRoutingAvailabilityAsync();
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            ShowError("無法偵測目前的音訊程式。", exception);
        }
        finally
        {
            isRefreshing = false;
            if (!isClosing)
            {
                RefreshButton.IsEnabled = true;
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

    private void UpdateApplications(IReadOnlyList<AudioSession> sessions)
    {
        routeCoordinator.RemoveSelectionsAbsentFrom(sessions);
        Applications.Clear();

        foreach (var session in sessions)
        {
            var isProtected = AudioRoutingPolicy.IsProtectedProcess(session.ProcessName);
            Applications.Add(new AudioApplicationRow(
                session,
                isProtected,
                !isProtected && routeCoordinator.IsSelected(session)));
        }

        EmptyStateText.Visibility = Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "目前沒有偵測到正在播放音訊的程式。";
        UpdateRoutingSetupState();
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
                ShowError(result.Message ?? "無法更新本機選取狀態。", null);
                return;
            }

            ErrorPanel.Visibility = Visibility.Collapsed;
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
            ShowError("無法更新本機選取或開啟 Windows 音量混音器。", exception);
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
        SetupSelectedButton.IsEnabled = selected.Count > 0;
        InstructionText.Text = AudioRoutingPolicy.GetSetupInstruction(selected);
        ExperimentalRoutingStatusText.Text = experimentalRoutingStatus;
        ApplyRoutingButton.IsEnabled = CanApplyRouting(selected);
        RestoreRoutingButton.IsEnabled = hasSuccessfulRoutingTransaction &&
                                         experimentalRoutingAvailable &&
                                         !isRefreshing &&
                                         !isRoutingOperation;
    }

    private IReadOnlyList<AudioSession> GetSelectedSessions() =>
        routeCoordinator.GetSelectedSessions(Applications.Select(row => row.Session));

    private bool CanApplyRouting(IReadOnlyCollection<AudioSession> selected) =>
        selected.Count > 0 &&
        experimentalRoutingAvailable &&
        !hasSuccessfulRoutingTransaction &&
        !isRefreshing &&
        !isRoutingOperation &&
        !string.IsNullOrWhiteSpace(inputDeviceId) &&
        !string.IsNullOrWhiteSpace(auxDeviceId);

    private async Task RefreshExperimentalRoutingAvailabilityAsync()
    {
        experimentalRoutingAvailable = false;
        inputDeviceId = null;
        auxDeviceId = null;
        experimentalRoutingStatus = "Checking experimental routing helper and output devices...";
        UpdateRoutingSetupState();

        var health = await routingHelper.CheckHealthAsync(lifetimeCancellation.Token);
        if (!health.IsAvailable)
        {
            experimentalRoutingStatus = $"Experimental routing unavailable: {health.Message}";
            UpdateRoutingSetupState();
            return;
        }

        try
        {
            var devices = await routingHelper.ListOutputDevicesAsync(lifetimeCancellation.Token);
            var inputMatches = devices
                .Where(device => string.Equals(device.Name, "Voicemeeter Input", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var auxMatches = devices
                .Where(device => string.Equals(device.Name, "Voicemeeter AUX Input", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (inputMatches.Length != 1 || auxMatches.Length != 1)
            {
                experimentalRoutingStatus = "Experimental routing unavailable: expected exactly one endpoint named " +
                                            $"Voicemeeter Input (found {inputMatches.Length}) and Voicemeeter AUX Input (found {auxMatches.Length}).";
                UpdateRoutingSetupState();
                return;
            }

            inputDeviceId = inputMatches[0].Id;
            auxDeviceId = auxMatches[0].Id;
            experimentalRoutingAvailable = true;
            experimentalRoutingStatus = hasSuccessfulRoutingTransaction
                ? "Experimental routing is active for this app run. Restore this routing before applying another transaction."
                : "Experimental routing is available. Apply remains explicit and reversible.";
        }
        catch (OperationCanceledException) when (isClosing)
        {
            throw;
        }
        catch (Exception exception)
        {
            experimentalRoutingStatus = $"Experimental routing unavailable: output-device discovery failed: {exception.Message}";
        }

        UpdateRoutingSetupState();
    }

    private async void ApplyRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedSessions();
        if (!CanApplyRouting(selected))
        {
            UpdateRoutingSetupState();
            return;
        }

        var routableSessions = activeSessions
            .Where(session => !AudioRoutingPolicy.IsProtectedProcess(session.ProcessName))
            .ToArray();

        ApplicationRoutePlan plan;
        try
        {
            plan = ApplicationRoutePlanner.Create(routableSessions, selected, inputDeviceId!, auxDeviceId!);
        }
        catch (Exception exception)
        {
            hasSuccessfulRoutingTransaction = false;
            SetExperimentalRoutingUnavailable($"Planning failed: {exception.Message}");
            ShowError("Experimental audio routing was not applied.", null);
            return;
        }

        var selectedToInputCount = plan.Commands.Count(command => command.TargetDeviceId == inputDeviceId);
        var unselectedToAuxCount = plan.Commands.Count - selectedToInputCount;
        var confirmationText = AudioRoutingPolicy.GetRouteConfirmationText(selected, selectedToInputCount, unselectedToAuxCount) +
                               $"\n\nExperimental helper status: {experimentalRoutingStatus}\n\nApply this routing now?";
        if (MessageBox.Show(confirmationText, "Apply experimental audio routing", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        isRoutingOperation = true;
        UpdateRoutingSetupState();
        try
        {
            var result = await routeExecutor.ApplyAsync(plan, lifetimeCancellation.Token);
            if (!result.Succeeded)
            {
                hasSuccessfulRoutingTransaction = false;
                SetExperimentalRoutingUnavailable($"Apply failed: {result.Message}");
                ShowError("Experimental audio routing was not applied and has been disabled until the next refresh.", null);
                return;
            }

            hasSuccessfulRoutingTransaction = true;
            experimentalRoutingStatus = "Experimental routing was applied. Restore this routing before applying another transaction.";
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            hasSuccessfulRoutingTransaction = false;
            SetExperimentalRoutingUnavailable($"Apply failed: {exception.Message}");
            ShowError("Experimental audio routing was not applied and has been disabled until the next refresh.", null);
        }
        finally
        {
            isRoutingOperation = false;
            UpdateRoutingSetupState();
        }
    }

    private async void RestoreRoutingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!hasSuccessfulRoutingTransaction || !experimentalRoutingAvailable || isRoutingOperation)
        {
            UpdateRoutingSetupState();
            return;
        }

        var confirmationText = "Restore only the experimental routing transaction created during this app run?\n\n" +
                               $"Experimental helper status: {experimentalRoutingStatus}";
        if (MessageBox.Show(confirmationText, "Restore experimental audio routing", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        isRoutingOperation = true;
        UpdateRoutingSetupState();
        try
        {
            var result = await routeExecutor.RestoreAsync(lifetimeCancellation.Token);
            if (!result.Succeeded)
            {
                SetExperimentalRoutingUnavailable($"Restore failed: {result.Message}");
                ShowError("Experimental audio routing could not be restored and has been disabled until the next refresh.", null);
                return;
            }

            hasSuccessfulRoutingTransaction = false;
            experimentalRoutingStatus = "Experimental routing was restored. No route transaction is retained.";
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            SetExperimentalRoutingUnavailable($"Restore failed: {exception.Message}");
            ShowError("Experimental audio routing could not be restored and has been disabled until the next refresh.", null);
        }
        finally
        {
            isRoutingOperation = false;
            UpdateRoutingSetupState();
        }
    }

    private void SetExperimentalRoutingUnavailable(string reason)
    {
        experimentalRoutingAvailable = false;
        inputDeviceId = null;
        auxDeviceId = null;
        experimentalRoutingStatus = $"Experimental routing unavailable: {reason}";
        UpdateRoutingSetupState();
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
            ShowError("無法開啟 Windows 音量混音器。", exception);
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

    public AudioApplicationRow(AudioSession session, bool isProtected, bool isSelected)
    {
        Session = session;
        IsProtected = isProtected;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioSession Session { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Session.DisplayName) ? Session.ProcessName : Session.DisplayName;

    public string DetailText => $"{Session.ProcessName}  |  PID {Session.ProcessId}";

    public bool IsProtected { get; }

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
        ? "已排除：受保護的應用程式"
        : IsSelected ? "已選取" : "未選取";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ProcessStatus : INotifyPropertyChanged
{
    private bool isRunning;

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

    public string StatusText => IsRunning ? "執行中" : "未偵測到";
}
