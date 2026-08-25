using System.Diagnostics;

namespace AudioShare.Windows;

public sealed class OptionalVoicemodLauncher
{
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartupSettleDelay = TimeSpan.FromMilliseconds(1500);
    private const int StartupPollAttempts = 40;
    private readonly Func<bool> isRunning;
    private readonly Func<string?> findExecutable;
    private readonly Action<string> start;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly TimeSpan startupPollInterval;
    private readonly int startupPollAttempts;
    private readonly TimeSpan startupSettleDelay;

    public OptionalVoicemodLauncher()
        : this(
            IsRunning,
            FindExecutable,
            Start,
            Task.Delay,
            StartupPollInterval,
            StartupPollAttempts,
            StartupSettleDelay)
    {
    }

    public OptionalVoicemodLauncher(Func<bool> isRunning, Func<string?> findExecutable, Action<string> start)
        : this(
            isRunning,
            findExecutable,
            start,
            Task.Delay,
            StartupPollInterval,
            StartupPollAttempts,
            StartupSettleDelay)
    {
    }

    public OptionalVoicemodLauncher(
        Func<bool> isRunning,
        Func<string?> findExecutable,
        Action<string> start,
        Func<TimeSpan, CancellationToken, Task> delay,
        TimeSpan startupPollInterval,
        int startupPollAttempts,
        TimeSpan startupSettleDelay)
    {
        this.isRunning = isRunning;
        this.findExecutable = findExecutable;
        this.start = start;
        this.delay = delay;
        this.startupPollInterval = startupPollInterval;
        this.startupPollAttempts = startupPollAttempts;
        this.startupSettleDelay = startupSettleDelay;
    }

    public async Task<bool> EnsureReadyAsync(CancellationToken token)
    {
        try
        {
            if (isRunning())
            {
                return true;
            }

            var executablePath = findExecutable();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            start(executablePath);
            for (var attempt = 0; attempt < startupPollAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (isRunning())
                {
                    await delay(startupSettleDelay, token).ConfigureAwait(false);
                    return true;
                }

                if (attempt < startupPollAttempts - 1)
                {
                    await delay(startupPollInterval, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Voicemod is optional, so startup must remain silent on any failure.
        }

        return false;
    }

    public void TryStart() => _ = EnsureReadyAsync(CancellationToken.None);

    private static bool IsRunning()
    {
        foreach (var processName in new[] { "Voicemod", "VoicemodDesktop" })
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                if (processes.Length > 0)
                {
                    return true;
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

        return false;
    }

    private static string? FindExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, "VoicemodV3", "app", "last", "Voicemod.exe"),
            Path.Combine(localAppData, "Voicemod", "VoicemodDesktop.exe"),
            Path.Combine(localAppData, "Programs", "Voicemod", "VoicemodDesktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Voicemod", "VoicemodDesktop.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Voicemod", "VoicemodDesktop.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Start(string executablePath) =>
        Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
}
