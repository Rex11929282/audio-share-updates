using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioRouteVisibilityPolicyTests
{
    [Theory]
    [InlineData("Voicemeeter In 1 (VB-Audio Voicemeeter VAIO)")]
    [InlineData("Voicemeeter In 5 (VB-Audio Voicemeeter VAIO)")]
    [InlineData("Voicemeeter VAIO3 Input (VB-Audio Voicemeeter VAIO)")]
    public void HidesOnlyTheRequestedNonSharingDevices(string deviceName) =>
        Assert.True(AudioRouteVisibilityPolicy.IsHiddenNonSharingDevice(deviceName));

    [Theory]
    [InlineData("Voicemeeter Input (VB-Audio Voicemeeter VAIO)")]
    [InlineData("Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)")]
    [InlineData("Speakers")]
    public void KeepsSharingAndLocalPlaybackDevicesVisible(string deviceName) =>
        Assert.False(AudioRouteVisibilityPolicy.IsHiddenNonSharingDevice(deviceName));
}
