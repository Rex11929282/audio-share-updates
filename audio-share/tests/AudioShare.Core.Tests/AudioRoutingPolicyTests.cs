using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioRoutingPolicyTests
{
    [Theory]
    [InlineData("discord")]
    [InlineData("Discord.EXE")]
    [InlineData("Voicemod")]
    [InlineData("voicemeeterpro.exe")]
    public void IsProtectedProcess_RecognizesExcludedPrograms(string processName)
    {
        Assert.True(AudioRoutingPolicy.IsProtectedProcess(processName));
    }

    [Fact]
    public void GetSetupInstruction_WithoutSelections_ExplainsThatNothingWillBeShared()
    {
        var instruction = AudioRoutingPolicy.GetSetupInstruction([]);

        Assert.Contains("No application is selected", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSetupInstruction_WithSelection_NamesTheManualTargetDevice()
    {
        var instruction = AudioRoutingPolicy.GetSetupInstruction(
        [new AudioSession(10, 100, "chrome.exe", "Chrome", true)]);

        Assert.Contains("Voicemeeter Input", instruction, StringComparison.Ordinal);
        Assert.Contains("Windows Volume Mixer", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSetupInstruction_WithProtectedOnlySessions_Throws()
    {
        var discord = new AudioSession(20, 200, "discord.exe", "Discord", true);

        Assert.Throws<ArgumentException>(() => AudioRoutingPolicy.GetSetupInstruction([discord]));
    }

    [Fact]
    public void GetRouteConfirmationText_NamesSelectedAppsAndExplainsIdentityScopeAndExclusions()
    {
        var selected = new AudioSession(10, 100, "chrome.exe", "Chrome", true);
        var text = AudioRoutingPolicy.GetRouteConfirmationText([selected], 1, 2);

        Assert.Contains("Chrome", text, StringComparison.Ordinal);
        Assert.Contains("all active processes of the same application identity", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Discord", text, StringComparison.Ordinal);
        Assert.Contains("Voicemod", text, StringComparison.Ordinal);
        Assert.Contains("Voicemeeter", text, StringComparison.Ordinal);
        Assert.Contains("1", text, StringComparison.Ordinal);
        Assert.Contains("2", text, StringComparison.Ordinal);
    }
}
