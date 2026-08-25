using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioShare.Windows;

public sealed class VoicemeeterBananaWindowController
{
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(250);
    private const int StartupPollAttempts = 24;
    private readonly Func<bool> isRunning;
    private readonly Func<string?> findExecutable;
    private readonly Action<string> start;
    private readonly Func<bool> isRemoteReady;
    private readonly Action hideMainWindow;
    private readonly TimeSpan startupPollInterval;
    private readonly int startupPollAttempts;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint windowHandle, int command);

    public VoicemeeterBananaWindowController()
        : this(
            IsRunning,
            FindExecutable,
            Start,
            IsRemoteReady,
            HideMainWindow,
            StartupPollInterval,
            StartupPollAttempts)
    {
    }

    public VoicemeeterBananaWindowController(
        Func<bool> isRunning,
        Func<string?> findExecutable,
        Action<string> start,
        Func<bool> isRemoteReady,
        Action hideMainWindow,
        TimeSpan startupPollInterval,
        int maxAttempts)
    {
        this.isRunning = isRunning ?? throw new ArgumentNullException(nameof(isRunning));
        this.findExecutable = findExecutable ?? throw new ArgumentNullException(nameof(findExecutable));
        this.start = start ?? throw new ArgumentNullException(nameof(start));
        this.isRemoteReady = isRemoteReady ?? throw new ArgumentNullException(nameof(isRemoteReady));
        this.hideMainWindow = hideMainWindow ?? throw new ArgumentNullException(nameof(hideMainWindow));
        if (startupPollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startupPollInterval));
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        }

        this.startupPollInterval = startupPollInterval;
        startupPollAttempts = maxAttempts;
    }

    public async Task<bool> EnsureReadyAndMinimizeAsync(CancellationToken token)
    {
        try
        {
            if (!isRunning())
            {
                var executablePath = findExecutable();
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    return false;
                }

                start(executablePath);
            }

            for (var attempt = 0; attempt < startupPollAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (isRunning() && isRemoteReady())
                {
                    hideMainWindow();
                    return true;
                }

                if (attempt < startupPollAttempts - 1)
                {
                    await Task.Delay(startupPollInterval, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Startup remains non-blocking when Banana is absent or still unavailable.
        }

        return false;
    }

    public void TryStartAndMinimize()
    {
        _ = EnsureReadyAndMinimizeAsync(CancellationToken.None);
    }

    public void TryHideMainWindow()
    {
        HideMainWindow();
    }

    private static void HideMainWindow()
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

    private static string? FindExecutable()
    {
        VoicemeeterBananaInstallationDetector.TryGetExecutablePath(out var executablePath);
        return executablePath;
    }

    private static void Start(string executablePath) =>
        Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });

    private static bool IsRemoteReady()
    {
        try
        {
            _ = new VoicemeeterSharingBusService().GetStatus();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
