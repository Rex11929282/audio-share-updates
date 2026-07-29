using System.Diagnostics;

namespace AudioShare.Windows;

public static class VolumeMixerLauncher
{
    public const string Uri = "ms-settings:apps-volume";

    public static void Open()
    {
        Process.Start(new ProcessStartInfo(Uri)
        {
            UseShellExecute = true,
        });
    }
}
