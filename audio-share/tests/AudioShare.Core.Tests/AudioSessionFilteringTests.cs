using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioSessionFilteringTests
{
    [Fact]
    public void GetActiveProcessSessions_ExcludesSystemAndNoProcessSessions()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(0, 0, "", "System Sounds", true, true),
            new AudioSessionCandidate(0, 0, "", "No process", true, false),
            new AudioSessionCandidate(10, 100, "inactive.exe", "Inactive", false, false),
            new AudioSessionCandidate(20, 200, "chrome.exe", "Chrome", true, false),
        ]);

        var session = Assert.Single(sessions);
        Assert.Equal(20, session.ProcessId);
        Assert.Equal("chrome.exe", session.ProcessName);
    }

    [Fact]
    public void GetActiveProcessSessions_ExcludesVoicemodAndVoicemeeterBanana()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(21, 210, "Chrome.EXE", "Chrome", true, false),
            new AudioSessionCandidate(22, 220, "Voicemod", "Voicemod", true, false),
            new AudioSessionCandidate(23, 230, "voicemod.exe", "Voicemod", true, false),
            new AudioSessionCandidate(24, 240, "VoicemeeterPro", "Voicemeeter Banana", true, false),
            new AudioSessionCandidate(25, 250, "voicemeeterpro.exe", "Voicemeeter Banana", true, false),
        ]);

        var session = Assert.Single(sessions);
        Assert.Equal("Chrome.EXE", session.ProcessName);
    }

    [Fact]
    public void GetActiveProcessSessions_ExcludesDiscordAndAllProtectedPrograms()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(1, 10, "discord.exe", "Discord", true, false),
            new AudioSessionCandidate(2, 20, "Voicemod.exe", "Voicemod", true, false),
            new AudioSessionCandidate(3, 30, "Voicemeeter.exe", "Voicemeeter", true, false),
            new AudioSessionCandidate(4, 40, "wallpaper64.exe", "wallpaper64", true, false),
            new AudioSessionCandidate(5, 50, "chrome.exe", "Chrome", true, false),
        ]);

        var session = Assert.Single(sessions);
        Assert.Equal("chrome.exe", session.ProcessName);
    }

    [Fact]
    public void GetActiveProcessSessions_DeduplicatesMultipleSessionsFromOneProcess()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(30, 300, "cloudmusic.exe", "NetEase Cloud Music", true, false),
            new AudioSessionCandidate(30, 300, "cloudmusic.exe", "NetEase Cloud Music", true, false),
            new AudioSessionCandidate(40, 400, "chrome.exe", "Chrome", true, false),
        ]);

        Assert.Equal(2, sessions.Count);
        Assert.Equal([30, 40], sessions.Select(session => session.ProcessId));
        Assert.All(sessions, session => Assert.True(session.HasAudio));
    }

    [Fact]
    public void GetActiveProcessSessions_UsesProcessNameWhenTheSessionHasNoDisplayName()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [new AudioSessionCandidate(50, 500, "chrome.exe", "", true, false)]);

        Assert.Equal("chrome.exe", Assert.Single(sessions).DisplayName);
    }

    [Fact]
    public void GetActiveProcessSessions_SelectsTheSameRepresentativeRegardlessOfInputOrder()
    {
        var candidates = new[]
        {
            new AudioSessionCandidate(60, 600, "zeta.exe", "", true, false),
            new AudioSessionCandidate(60, 600, "beta.exe", "Browser", true, false),
            new AudioSessionCandidate(60, 600, "alpha.exe", "browser", true, false),
            new AudioSessionCandidate(60, 600, "gamma.exe", "Chrome", true, false),
        };

        var forward = Assert.Single(AudioSessionFilter.GetActiveProcessSessions(candidates));
        var reverse = Assert.Single(AudioSessionFilter.GetActiveProcessSessions(candidates.Reverse()));

        Assert.Equal("browser", forward.DisplayName);
        Assert.Equal("alpha.exe", forward.ProcessName);
        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void GetActiveProcessSessions_MarksSilentProcessesAsNotAudible()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [new AudioSessionCandidate(70, 700, "idleplayer.exe", "idleplayer", true, false, HasAudio: false)]);

        Assert.False(Assert.Single(sessions).HasAudio);
    }

    [Fact]
    public void GetActiveProcessSessions_MarksAProcessAudibleWhenAnyOfItsSessionsHasSound()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(80, 800, "chrome.exe", "Chrome", true, false, HasAudio: false),
            new AudioSessionCandidate(80, 800, "chrome.exe", "Chrome", true, false, HasAudio: true),
        ]);

        Assert.True(Assert.Single(sessions).HasAudio);
    }

    [Fact]
    public void GetActiveProcessSessions_RetainsTheCurrentOutputDevice()
    {
        var session = Assert.Single(AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(
                90,
                900,
                "chrome.exe",
                "Chrome",
                true,
                false,
                OutputDeviceId: "speaker-device",
                OutputDeviceName: "Speakers (High Definition Audio Device)"),
        ]));

        Assert.Equal("speaker-device", session.OutputDeviceId);
        Assert.Equal("Speakers (High Definition Audio Device)", session.OutputDeviceName);
    }
}
