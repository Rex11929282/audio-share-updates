using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class VolumeMixerLauncherTests
{
    [Fact]
    public void VolumeMixerUri_IsTheWindowsAppsVolumeSettingsUri()
    {
        Assert.Equal("ms-settings:apps-volume", VolumeMixerLauncher.Uri);
    }
}
