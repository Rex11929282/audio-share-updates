using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioSessionFilteringTests
{
    [Fact]
    public void GetActiveProcessSessions_ExcludesSystemAndNoProcessSessions()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(0, "", "System Sounds", true, true),
            new AudioSessionCandidate(0, "", "No process", true, false),
            new AudioSessionCandidate(10, "inactive.exe", "Inactive", false, false),
            new AudioSessionCandidate(20, "chrome.exe", "Chrome", true, false),
        ]);

        var session = Assert.Single(sessions);
        Assert.Equal(20, session.ProcessId);
        Assert.Equal("chrome.exe", session.ProcessName);
    }

    [Fact]
    public void GetActiveProcessSessions_DeduplicatesMultipleSessionsFromOneProcess()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [
            new AudioSessionCandidate(30, "cloudmusic.exe", "NetEase Cloud Music", true, false),
            new AudioSessionCandidate(30, "cloudmusic.exe", "NetEase Cloud Music", true, false),
            new AudioSessionCandidate(40, "chrome.exe", "Chrome", true, false),
        ]);

        Assert.Equal(2, sessions.Count);
        Assert.Equal([30, 40], sessions.Select(session => session.ProcessId));
        Assert.All(sessions, session => Assert.True(session.HasAudio));
    }

    [Fact]
    public void GetActiveProcessSessions_UsesProcessNameWhenTheSessionHasNoDisplayName()
    {
        var sessions = AudioSessionFilter.GetActiveProcessSessions(
        [new AudioSessionCandidate(50, "chrome.exe", "", true, false)]);

        Assert.Equal("chrome.exe", Assert.Single(sessions).DisplayName);
    }

    [Fact]
    public void GetActiveProcessSessions_SelectsTheSameRepresentativeRegardlessOfInputOrder()
    {
        var candidates = new[]
        {
            new AudioSessionCandidate(60, "zeta.exe", "", true, false),
            new AudioSessionCandidate(60, "beta.exe", "Browser", true, false),
            new AudioSessionCandidate(60, "alpha.exe", "browser", true, false),
            new AudioSessionCandidate(60, "gamma.exe", "Chrome", true, false),
        };

        var forward = Assert.Single(AudioSessionFilter.GetActiveProcessSessions(candidates));
        var reverse = Assert.Single(AudioSessionFilter.GetActiveProcessSessions(candidates.Reverse()));

        Assert.Equal("browser", forward.DisplayName);
        Assert.Equal("alpha.exe", forward.ProcessName);
        Assert.Equal(forward, reverse);
    }
}
