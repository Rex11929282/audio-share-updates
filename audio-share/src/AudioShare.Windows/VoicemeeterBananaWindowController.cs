using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioShare.Windows;

public sealed class VoicemeeterBananaWindowController
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint windowHandle, int command);

    public void TryStartAndMinimize()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (!IsRunning() &&
                    VoicemeeterBananaInstallationDetector.TryGetExecutablePath(out var executablePath) &&
                    !string.IsNullOrWhiteSpace(executablePath))
                {
                    Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
                }

                for (var attempt = 0; attempt < 12; attempt++)
                {
                    TryHideMainWindow();
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }
            catch
            {
                // Banana is optional during startup; the health check explains a later share failure.
            }
        });
    }

    public void TryHideMainWindow()
    {
        var processes = Process.GetProcessesByName("voicemeeterpro");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainWindowHandle != nint.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, 0);
                        return;
                    }
                }
                catch
                {
                    // Banana can be starting or exiting; leaving its window visible is safe.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool IsRunning()
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
}
