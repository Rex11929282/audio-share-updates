using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ApplicationRoutePlannerTests
{
    [Fact]
    public void Create_SendsSelectedSessionsToInputAndUnselectedSessionsToAux()
    {
        var chrome = new AudioSession(11, 100, "chrome.exe", "Chrome", true);
        var music = new AudioSession(12, 200, "cloudmusic.exe", "NetEase", true);

        var plan = ApplicationRoutePlanner.Create([chrome, music], [chrome], "input-id", "aux-id");

        Assert.Equal("input-id", Assert.Single(plan.Commands, x => x.ProcessId == 11).TargetDeviceId);
        Assert.Equal("aux-id", Assert.Single(plan.Commands, x => x.ProcessId == 12).TargetDeviceId);
    }

    [Fact]
    public void Create_SendsAllConcurrentSessionsWithSelectedApplicationIdentityToInput()
    {
        var selectedChrome = new AudioSession(11, 100, "chrome.exe", "Chrome", true);
        var concurrentChrome = new AudioSession(12, 200, "Chrome.EXE", "Chrome", true);
        var music = new AudioSession(13, 300, "cloudmusic.exe", "NetEase", true);

        var plan = ApplicationRoutePlanner.Create(
            [selectedChrome, concurrentChrome, music],
            [selectedChrome],
            "input-id",
            "aux-id");

        Assert.Equal("input-id", Assert.Single(plan.Commands, x => x.ProcessId == 11).TargetDeviceId);
        Assert.Equal("input-id", Assert.Single(plan.Commands, x => x.ProcessId == 12).TargetDeviceId);
        Assert.Equal("aux-id", Assert.Single(plan.Commands, x => x.ProcessId == 13).TargetDeviceId);
    }

    [Fact]
    public void Create_DeduplicatesByPidAndSortsCommandsAscending()
    {
        var later = new AudioSession(20, 200, "later.exe", "Later", true);
        var earlier = new AudioSession(10, 100, "earlier.exe", "Earlier", true);

        var plan = ApplicationRoutePlanner.Create([later, earlier, later], [later], "input-id", "aux-id");

        Assert.Equal([10, 20], plan.Commands.Select(command => command.ProcessId));
        Assert.Equal(2, plan.Commands.Count);
        Assert.Equal("input-id", plan.Commands[1].TargetDeviceId);
    }

    [Theory]
    [InlineData(null, "aux-id")]
    [InlineData("input-id", null)]
    [InlineData(" ", "aux-id")]
    [InlineData("input-id", "\t")]
    public void Create_RejectsInvalidArguments(string? inputDeviceId, string? auxDeviceId)
    {
        var session = new AudioSession(11, 100, "chrome.exe", "Chrome", true);

        Assert.ThrowsAny<ArgumentException>(() =>
            ApplicationRoutePlanner.Create([session], [session], inputDeviceId!, auxDeviceId!));
    }

    [Fact]
    public void Create_RejectsNullCollections()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ApplicationRoutePlanner.Create(null!, [], "input-id", "aux-id"));
        Assert.Throws<ArgumentNullException>(() =>
            ApplicationRoutePlanner.Create([], null!, "input-id", "aux-id"));
    }

    [Theory]
    [InlineData("discord.exe")]
    [InlineData("Voicemod")]
    [InlineData("voicemeeter.exe")]
    [InlineData("voicemeeterpro.exe")]
    [InlineData("DiscordCanary.exe")]
    [InlineData("VoicemodBeta.exe")]
    [InlineData("VoicemeeterPro64.exe")]
    public void Create_RejectsProtectedSessionsBeforeProducingCommands(string processName)
    {
        var protectedSession = new AudioSession(20, 200, processName, processName, true);

        Assert.Throws<ArgumentException>(() =>
            ApplicationRoutePlanner.Create([protectedSession], [], "input-id", "aux-id"));
    }
}
