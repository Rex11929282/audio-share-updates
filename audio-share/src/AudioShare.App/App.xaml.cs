using System.Configuration;
using System.Data;
using System.Windows;
using AudioShare.Core;
using AudioShare.Windows;

namespace AudioShare.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string InstanceMutexName = @"Local\FlowCast.SingleInstance";
    private const string ActivateInstanceEventName = @"Local\FlowCast.ActivateInstance";
    private readonly UpdateService updateService = new();
    private StagedUpdate? stagedUpdate;
    private ReleaseUpdate? availableUpdate;
    private bool isCheckingForUpdates;
    private Mutex? instanceMutex;
    private EventWaitHandle? activateInstanceEvent;
    private FlowCastRuntimeHost? runtimeHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        activateInstanceEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateInstanceEventName);
        _ = Task.Run(WaitForExistingInstanceActivation);
        base.OnStartup(e);
        runtimeHost = FlowCastRuntimeHost.CreateDefault();
        var runtimeInitialization = runtimeHost.InitializeAsync(CancellationToken.None);
        var mainWindow = new MainWindow(runtimeInitialization);
        MainWindow = mainWindow;
        mainWindow.Show();
        if (UpdateService.IsUpdateFailedRestart(e.Args))
        {
            FlowCastMessageDialog.Show(
                mainWindow,
                "FlowCast 更新",
                UpdateService.UpdateFailedRestartNotice,
                FlowCastWindowTone.Error);
        }

        _ = CheckForUpdatesAsync(mainWindow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        runtimeHost = null;
        activateInstanceEvent?.Dispose();
        if (instanceMutex is not null)
        {
            try
            {
                instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process may already have released the mutex during shutdown.
            }

            instanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private void WaitForExistingInstanceActivation()
    {
        try
        {
            while (activateInstanceEvent?.WaitOne() == true)
            {
                Dispatcher.BeginInvoke(ActivateMainWindow);
            }
        }
        catch (ObjectDisposedException)
        {
            // Application shutdown ends the activation listener.
        }
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        if (!MainWindow.IsVisible)
        {
            MainWindow.Show();
        }

        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
        MainWindow.Topmost = true;
        MainWindow.Topmost = false;
        MainWindow.Focus();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ActivateInstanceEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance is still starting or has already exited.
        }
    }

    public Task CheckForUpdatesFromUserAsync(Window owner) => CheckForUpdatesAsync(owner);

    private async Task CheckForUpdatesAsync(Window owner)
    {
        if (isCheckingForUpdates)
        {
            return;
        }

        if (stagedUpdate is not null)
        {
            FlowCastMessageDialog.Show(owner, "FlowCast 更新", "更新已下載並驗證完成。請關閉並重新打開 FlowCast 完成更新。", FlowCastWindowTone.Update);
            return;
        }

        isCheckingForUpdates = true;
        UpdateProgressWindow? progressWindow = null;
        var ownerWasEnabled = owner.IsEnabled;
        var restartStarted = false;

        void ReleaseOwner()
        {
            owner.IsEnabled = ownerWasEnabled;
        }

        try
        {
            var update = availableUpdate ?? await updateService.CheckForUpdateAsync();
            if (update is null)
            {
                SetUpdateAvailable(owner, false);
                return;
            }

            availableUpdate = update;
            SetUpdateAvailable(owner, true);
            if (new UpdateAvailableDialog(owner, UpdateService.CurrentVersion, update).ShowDialog() != true)
            {
                return;
            }

            if (owner is MainWindow mainWindow && mainWindow.IsSharingActive)
            {
                if (!FlowCastMessageDialog.Confirm(
                        owner,
                        "FlowCast 更新",
                        "更新會停止當前分享並恢復分享前的播放路徑。要繼續嗎？",
                        "繼續更新",
                        "取消",
                        FlowCastWindowTone.Update))
                {
                    return;
                }

                if (!await mainWindow.StopSharingWithResetAsync())
                {
                    FlowCastMessageDialog.Show(
                        owner,
                        "FlowCast 更新",
                        "為了保護你的聲音，FlowCast 還不能確認分享已停止。請先停止分享後再更新。",
                        FlowCastWindowTone.Error);
                    return;
                }
            }

            progressWindow = new UpdateProgressWindow(owner);
            SetUpdateStaging(owner, true);
            owner.IsEnabled = false;
            progressWindow.Show();
            await progressWindow.UpdateAndRenderAsync(new UpdateProgress(UpdateStage.Downloading, "正在準備更新", null));
            var updateToRestart = await updateService.DownloadAndStageAsync(
                update,
                progressWindow.UpdateAndRenderAsync);
            await progressWindow.UpdateAndRenderAsync(new UpdateProgress(UpdateStage.ReadyToRestart, "正在重新啟動 FlowCast", 100));
            updateService.BeginStagedReplacementAndRestart(updateToRestart);
            stagedUpdate = updateToRestart;
            restartStarted = true;
            SetUpdateStaging(owner, false);
            ReleaseOwner();
            progressWindow.CloseFromApplication();
            if (owner is MainWindow updateMainWindow)
            {
                updateMainWindow.PrepareForUpdateRestart();
            }

            owner.Close();
        }
        catch (Exception exception)
        {
            UpdateService.RecordUpdateError(exception);
            progressWindow?.CloseFromApplication();
            SetUpdateStaging(owner, false);
            ReleaseOwner();
            FlowCastMessageDialog.Show(
                owner,
                "FlowCast 更新",
                "這次更新沒有完成，FlowCast 仍會使用當前版本。\n\n下一步：請關閉其他 FlowCast 窗口後重試；如果仍然失敗，請重新打開 FlowCast 後再試。",
                FlowCastWindowTone.Error);
        }
        finally
        {
            if (!restartStarted)
            {
                SetUpdateStaging(owner, false);
                ReleaseOwner();
            }

            isCheckingForUpdates = false;
        }
    }

    private static void SetUpdateAvailable(Window owner, bool isAvailable)
    {
        if (owner is MainWindow mainWindow)
        {
            mainWindow.SetUpdateAvailable(isAvailable);
        }
    }

    private static void SetUpdateStaging(Window owner, bool isStaging)
    {
        if (owner is MainWindow mainWindow)
        {
            mainWindow.SetUpdateStaging(isStaging);
        }
    }
}

