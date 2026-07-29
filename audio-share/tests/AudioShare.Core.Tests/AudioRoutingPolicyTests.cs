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
}
