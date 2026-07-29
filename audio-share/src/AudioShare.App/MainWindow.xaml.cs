using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool isRefreshing;
    private bool isClosing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        ProcessStatuses.Add(new ProcessStatus("Voicemod", "Voicemod"));
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

        try
        {
            UpdateProcessStatuses();
            var sessions = await discovery.GetActiveSessionsAsync(lifetimeCancellation.Token);
            UpdateApplications(sessions);
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
        routeCoordinator.RemoveSelectionsAbsentFrom(sessions.Select(session => session.ProcessId));
        Applications.Clear();

        foreach (var session in sessions)
        {
            var isDiscord = IsDiscord(session);
            Applications.Add(new AudioApplicationRow(
                session,
                isDiscord,
                !isDiscord && routeCoordinator.IsSelected(session.ProcessId)));
        }

        EmptyStateText.Visibility = Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = "目前沒有偵測到正在播放音訊的程式。";
    }

    private async void ApplicationSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.DataContext is not AudioApplicationRow row || !row.IsSelectable)
        {
            return;
        }

        var shouldShare = checkBox.IsChecked == true;
        if (routeCoordinator.IsSelected(row.Session.ProcessId) == shouldShare)
        {
            return;
        }

        checkBox.IsEnabled = false;
        try
        {
            var result = shouldShare
                ? await routeCoordinator.ShareAsync(row.Session, lifetimeCancellation.Token)
                : await routeCoordinator.UnshareAsync(row.Session.ProcessId, lifetimeCancellation.Token);

            row.IsSelected = routeCoordinator.IsSelected(row.Session.ProcessId);
            if (!result.Succeeded)
            {
                ShowError(result.Message ?? "無法更新本機選取狀態。", null);
                return;
            }

            ErrorPanel.Visibility = Visibility.Collapsed;
            if (!shouldShare)
            {
                InstructionText.Text = "已取消此程式的本機選取。";
                return;
            }

            VolumeMixerLauncher.Open();
            InstructionText.Text = "在 Windows 音量混音器中，將此程式的輸出選為 Voicemeeter Input.";
        }
        catch (OperationCanceledException) when (isClosing)
        {
        }
        catch (Exception exception)
        {
            row.IsSelected = routeCoordinator.IsSelected(row.Session.ProcessId);
            ShowError("無法更新本機選取或開啟 Windows 音量混音器。", exception);
        }
        finally
        {
            if (!isClosing)
            {
                checkBox.IsEnabled = true;
            }
        }
    }

    private void OpenVolumeMixerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            VolumeMixerLauncher.Open();
            ErrorPanel.Visibility = Visibility.Collapsed;
            InstructionText.Text = "請在 Windows 音量混音器中手動將程式輸出選為 Voicemeeter Input。";
        }
        catch (Exception exception)
        {
            ShowError("無法開啟 Windows 音量混音器。", exception);
        }
    }

    private static bool IsDiscord(AudioSession session) =>
        IsDiscordName(session.ProcessName) || IsDiscordName(session.DisplayName);

    private static bool IsDiscordName(string value) =>
        string.Equals(value, "discord", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "discord.exe", StringComparison.OrdinalIgnoreCase);

    private void ShowError(string message, Exception? exception)
    {
        ErrorText.Text = exception is null ? message : $"{message} {exception.Message}";
        ErrorPanel.Visibility = Visibility.Visible;
    }
}

public sealed class AudioApplicationRow : INotifyPropertyChanged
{
    private bool isSelected;

    public AudioApplicationRow(AudioSession session, bool isDiscord, bool isSelected)
    {
        Session = session;
        IsDiscord = isDiscord;
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioSession Session { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Session.DisplayName) ? Session.ProcessName : Session.DisplayName;

    public string DetailText => $"{Session.ProcessName}  |  PID {Session.ProcessId}";

    public bool IsDiscord { get; }

    public bool IsSelectable => !IsDiscord;

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

    public string SelectionStatus => IsDiscord
        ? "已排除：Discord"
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
