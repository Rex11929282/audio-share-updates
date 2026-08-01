namespace AudioShare.Windows;

public static class VoicemeeterBananaInstallationDetector
{
    private static readonly string[] ExecutablePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VB", "Voicemeeter", "voicemeeterpro.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VB", "Voicemeeter", "voicemeeterpro.exe"),
    ];

    public static bool IsInstalled(Func<string, bool>? fileExists = null)
    {
        return TryGetExecutablePath(out _, fileExists);
    }

    public static bool TryGetExecutablePath(out string? executablePath, Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;
        executablePath = ExecutablePaths.FirstOrDefault(exists);
        return executablePath is not null;
    }
}
