using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioRoutingPolicyTests
{
    [Theory]
    [InlineData("input", "aux")]
    [InlineData("aux", "input")]
    public void SharingRouteState_Classify_ReturnsSharingWhenAnyRoleUsesInput(
        string consoleDeviceId,
        string multimediaDeviceId)
    {
        var state = SharingRouteState.Classify(
            new ApplicationRouteState(consoleDeviceId, multimediaDeviceId),
            "input",
            "aux");

        Assert.Equal(SharingRouteState.Sharing, state);
    }

    [Fact]
    public void SharingRouteState_Classify_ReturnsLocalOnlyWhenBothRolesUseAux()
    {
        var state = SharingRouteState.Classify(
            new ApplicationRouteState("aux", "aux"),
            "input",
            "aux");

        Assert.Equal(SharingRouteState.LocalOnly, state);
    }

    [Theory]
    [InlineData(null, "input", "aux")]
    [InlineData("aux", null, "aux")]
    [InlineData("aux", "input", null)]
    public void SharingRouteState_Classify_ReturnsUnknownWhenRouteOrDeviceIdsAreMissing(
        string? routeKind,
        string? inputDeviceId,
        string? auxDeviceId)
    {
        var routes = routeKind is null
            ? null
            : new ApplicationRouteState(routeKind, routeKind);

        var state = SharingRouteState.Classify(routes, inputDeviceId, auxDeviceId);

        Assert.Equal(SharingRouteState.Unknown, state);
    }

    [Theory]
    [InlineData(null, "aux")]
    [InlineData("aux", null)]
    [InlineData("aux", "other")]
    public void SharingRouteState_Classify_ReturnsUnknownWhenRouteDataIsMissingOrMixed(
        string? consoleDeviceId,
        string? multimediaDeviceId)
    {
        var state = SharingRouteState.Classify(
            new ApplicationRouteState(consoleDeviceId, multimediaDeviceId),
            "input",
            "aux");

        Assert.Equal(SharingRouteState.Unknown, state);
    }

    [Fact]
    public void SharingRouteState_Aggregate_ReturnsSharingWhenAnyProgramIsSharing()
    {
        var state = SharingRouteState.Aggregate(
        [
            SharingRouteState.LocalOnly,
            SharingRouteState.Sharing,
        ]);

        Assert.Equal(SharingRouteState.Sharing, state);
    }

    [Fact]
    public void SharingRouteState_Aggregate_ReturnsUnknownWhenNoProgramIsSharingAndAnyProgramIsUnknown()
    {
        var state = SharingRouteState.Aggregate(
        [
            SharingRouteState.LocalOnly,
            SharingRouteState.Unknown,
        ]);

        Assert.Equal(SharingRouteState.Unknown, state);
    }

    [Fact]
    public void SharingRouteState_Aggregate_ReturnsLocalOnlyWhenAllProgramsAreLocalOnly()
    {
        var state = SharingRouteState.Aggregate(
        [
            SharingRouteState.LocalOnly,
            SharingRouteState.LocalOnly,
        ]);

        Assert.Equal(SharingRouteState.LocalOnly, state);
    }

    [Fact]
    public void SharingRouteState_Aggregate_ReturnsLocalOnlyWhenNoProgramsAreActive()
    {
        Assert.Equal(SharingRouteState.LocalOnly, SharingRouteState.Aggregate([]));
    }

    [Theory]
    [InlineData("discord")]
    [InlineData("Discord.EXE")]
    [InlineData("DiscordCanary.exe")]
    [InlineData("Voicemod")]
    [InlineData("VoicemodBeta.exe")]
    [InlineData("voicemeeterpro.exe")]
    [InlineData("VoicemeeterPro64.exe")]
    public void IsProtectedProcess_RecognizesExcludedPrograms(string processName)
    {
        Assert.True(AudioRoutingPolicy.IsProtectedProcess(processName));
    }

    [Fact]
    public void GetSetupInstruction_WithoutSelections_ExplainsThatNothingWillBeSharedInSimplifiedChinese()
    {
        var instruction = AudioRoutingPolicy.GetSetupInstruction([]);

        Assert.Contains("未勾選任何程序", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSetupInstruction_WithSelection_NamesTheManualTargetDeviceInSimplifiedChinese()
    {
        var instruction = AudioRoutingPolicy.GetSetupInstruction(
        [new AudioSession(10, 100, "chrome.exe", "Chrome", true)]);

        Assert.Contains("Voicemeeter Input", instruction, StringComparison.Ordinal);
        Assert.Contains("Windows 音量混音器", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSetupInstruction_WithProtectedOnlySessions_Throws()
    {
        var discord = new AudioSession(20, 200, "discord.exe", "Discord", true);

        Assert.Throws<ArgumentException>(() => AudioRoutingPolicy.GetSetupInstruction([discord]));
    }

    [Fact]
    public void GetRouteConfirmationText_NamesSelectedAppsAndExplainsIdentityScopeAndExclusionsInSimplifiedChinese()
    {
        var selected = new AudioSession(10, 100, "chrome.exe", "Chrome", true);
        var text = AudioRoutingPolicy.GetRouteConfirmationText([selected], 1, 2);

        Assert.Contains("Chrome", text, StringComparison.Ordinal);
        Assert.Contains("同一程序名稱的全部活動進程", text, StringComparison.Ordinal);
        Assert.Contains("Discord", text, StringComparison.Ordinal);
        Assert.Contains("Voicemeeter", text, StringComparison.Ordinal);
        Assert.Contains("1", text, StringComparison.Ordinal);
        Assert.Contains("2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRouteConfirmationText_WithoutSelections_StatesThatNoAppWillBeSharedInSimplifiedChinese()
    {
        var text = AudioRoutingPolicy.GetRouteConfirmationText([], 0, 2);

        Assert.Contains("未勾選任何程序", text, StringComparison.Ordinal);
        Assert.Contains("0", text, StringComparison.Ordinal);
        Assert.Contains("2", text, StringComparison.Ordinal);
    }
}
