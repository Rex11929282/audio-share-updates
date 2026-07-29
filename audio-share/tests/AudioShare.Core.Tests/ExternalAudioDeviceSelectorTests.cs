using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class ExternalAudioDeviceSelectorTests
{
    [Theory]
    [InlineData("Voicemeeter Input")]
    [InlineData("Voicemeeter Input (VB-Audio Voicemeeter VAIO)")]
    [InlineData("VOICEMEETER INPUT (VB-Audio Voicemeeter VAIO)")]
    public void FindMatches_AcceptsCanonicalLabelAndParenthesizedEndpointDescription(string name)
    {
        var device = new ExternalAudioDevice("{0.0.0.00000000}.{INPUT}", name);

        var matches = ExternalAudioDeviceSelector.FindMatches([device], "Voicemeeter Input");

        Assert.Equal([device], matches);
    }

    [Theory]
    [InlineData("Speakers (Voicemeeter Input)")]
    [InlineData("Voicemeeter Input Clone")]
    [InlineData("Voicemeeter Inputting (VB-Audio)")]
    [InlineData("Voicemeeter AUX Input (VB-Audio Voicemeeter AUX VAIO)")]
    public void FindMatches_RejectsSubstringAndLookalikeEndpoints(string name)
    {
        var device = new ExternalAudioDevice("{0.0.0.00000000}.{OTHER}", name);

        var matches = ExternalAudioDeviceSelector.FindMatches([device], "Voicemeeter Input");

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_ReturnsActualDeviceIdForAuxEndpoint()
    {
        var device = new ExternalAudioDevice(
            "{0.0.0.00000000}.{AUX}",
            "Voicemeeter AUX Input (VB-Audio Voicemeeter AUX VAIO)");

        var match = Assert.Single(ExternalAudioDeviceSelector.FindMatches([device], "Voicemeeter AUX Input"));

        Assert.Equal("{0.0.0.00000000}.{AUX}", match.Id);
    }
}
