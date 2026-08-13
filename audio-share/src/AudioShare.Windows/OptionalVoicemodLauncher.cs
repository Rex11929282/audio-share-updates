using System.Diagnostics;

namespace AudioShare.Windows;

public sealed class OptionalVoicemodLauncher
{
    private readonly Func<bool> isRunning;
    private readonly Func<string?> findExecutable;
    private readonly Action<string> start;

    public OptionalVoicemodLauncher()
        : this(IsRunning, FindExecutable, Start)
    {
    }

    public OptionalVoicemodLauncher(Func<bool> isRunning, Func<string?> findExecutable, Action<string> start)
    {
        this.isRunning = isRunning;
        this.findExecutable = findExecutable;
        this.start = start;
    }

    public void TryStart()
    {
        try
        {
            if (isRunning())
            {
                return;
            }

            var executablePath = findExecutable();
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                start(executablePath);
            }
        }
        catch
        {
            // Voicemod is optional, so startup must remain silent on any failure.
        }
    }

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
