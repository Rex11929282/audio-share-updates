using System.Configuration;
using System.Data;
using System.Windows;

namespace AudioShare.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly UpdateService updateService = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = CheckForUpdatesAsync(mainWindow);
    }

    private async Task CheckForUpdatesAsync(Window owner)
    {
        try
        {
            var update = await updateService.CheckForUpdateAsync();
            if (update is null || new UpdateAvailableDialog(owner, update.Version).ShowDialog() != true)
            {
                return;
            }

            await updateService.DownloadVerifyAndRestartAsync(update);
            Shutdown();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                owner,
                $"更新失敗，未替換目前程式。{exception.Message}",
                "Audio Share 更新",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}

