using System.Configuration;
using System.Data;
using System.Windows;
using AudioShare.Core;

namespace AudioShare.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly UpdateService updateService = new();
    private StagedUpdate? stagedUpdate;
    private ReleaseUpdate? availableUpdate;
    private bool isCheckingForUpdates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        if (UpdateService.IsUpdateFailedRestart(e.Args))
        {
            MessageBox.Show(
                mainWindow,
                UpdateService.UpdateFailedRestartNotice,
                "FlowCast 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _ = CheckForUpdatesAsync(mainWindow, showDialog: false);
    }

    public Task CheckForUpdatesFromUserAsync(Window owner) => CheckForUpdatesAsync(owner, showDialog: true);

    private async Task CheckForUpdatesAsync(Window owner, bool showDialog)
    {
        if (isCheckingForUpdates)
        {
            return;
        }

        if (stagedUpdate is not null)
        {
            MessageBox.Show(owner, "更新已下载并验证完成。请关闭并重新打开 FlowCast 完成更新。", "FlowCast 更新", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        isCheckingForUpdates = true;
        UpdateProgressWindow? progressWindow = null;
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
            if (!showDialog || new UpdateAvailableDialog(owner, update.Version).ShowDialog() != true)
            {
                return;
            }

            progressWindow = new UpdateProgressWindow(owner);
            progressWindow.Show();
            stagedUpdate = await updateService.DownloadAndStageAsync(
                update,
                new Progress<UpdateProgress>(progressWindow.Update));
            progressWindow.Update(new UpdateProgress(UpdateStage.ReadyToRestart, "正在重新启动 FlowCast", 100));
            progressWindow.CloseFromApplication();
            updateService.BeginStagedReplacementAndRestart(stagedUpdate);
            owner.Close();
        }
        catch (Exception exception)
        {
            progressWindow?.CloseFromApplication();
            MessageBox.Show(
                owner,
                $"更新失败，未替换当前程序。{exception.Message}",
                "FlowCast 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
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
}

