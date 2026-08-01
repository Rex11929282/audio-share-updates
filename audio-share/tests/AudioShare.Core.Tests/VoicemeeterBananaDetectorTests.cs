using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class VoicemeeterBananaDetectorTests
{
    [Fact]
    public void InstallationDetector_ReportsInstalledWhenBananaExecutableExists()
    {
        var installed = VoicemeeterBananaInstallationDetector.IsInstalled(path =>
            path.EndsWith("voicemeeterpro.exe", StringComparison.OrdinalIgnoreCase));

        Assert.True(installed);
    }

    [Fact]
    public void InstallationDetector_DoesNotTreatMissingExecutableAsInstalled()
    {
        Assert.False(VoicemeeterBananaInstallationDetector.IsInstalled(_ => false));
    }

    [Fact]
    public void InstallationDetector_ReturnsExecutablePathWhenBananaIsInstalled()
    {
        var found = VoicemeeterBananaInstallationDetector.TryGetExecutablePath(
            out var executablePath,
            path => path.EndsWith("voicemeeterpro.exe", StringComparison.OrdinalIgnoreCase));

        Assert.True(found);
        Assert.EndsWith("voicemeeterpro.exe", executablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryFindEndpoints_ReturnsBananaInputAndAuxWhenBothAreInstalled()
    {
        var devices = new[]
        {
            new ExternalAudioDevice("input-id", "Voicemeeter Input (VB-Audio Voicemeeter VAIO)"),
            new ExternalAudioDevice("aux-id", "Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)"),
        };

        var found = VoicemeeterBananaDetector.TryFindEndpoints(devices, out var endpoints);

        Assert.True(found);
        Assert.Equal("input-id", endpoints!.Input.Id);
        Assert.Equal("aux-id", endpoints.AuxInput.Id);
    }

    [Fact]
    public void TryFindEndpoints_ReturnsFalseWhenAuxInputIsMissing()
    {
        var devices = new[]
        {
            new ExternalAudioDevice("input-id", "Voicemeeter Input (VB-Audio Voicemeeter VAIO)"),
        };

        var found = VoicemeeterBananaDetector.TryFindEndpoints(devices, out var endpoints);

        Assert.False(found);
        Assert.Null(endpoints);
    }

    [Fact]
    public void GetEndpointHealth_ReportsMissingInputWithoutHidingAuxHealth()
    {
        var health = VoicemeeterBananaDetector.GetEndpointHealth(
        [
            new ExternalAudioDevice("aux-id", "Voicemeeter AUX Input (VB-Audio Voicemeeter AUX VAIO)"),
        ]);

        Assert.False(health.HasInput);
        Assert.True(health.HasAux);
    }

    [Fact]
    public void GetEndpointHealth_ReportsMissingAuxWithoutHidingInputHealth()
    {
        var health = VoicemeeterBananaDetector.GetEndpointHealth(
        [
            new ExternalAudioDevice("input-id", "Voicemeeter Input (VB-Audio Voicemeeter VAIO)"),
        ]);

        Assert.True(health.HasInput);
        Assert.False(health.HasAux);
    }
}
