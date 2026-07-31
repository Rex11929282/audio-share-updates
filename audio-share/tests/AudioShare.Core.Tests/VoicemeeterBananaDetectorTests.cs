using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class VoicemeeterBananaDetectorTests
{
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
}
