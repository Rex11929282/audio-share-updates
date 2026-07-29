using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class RouteCoordinatorTests
{
    [Theory]
    [InlineData("discord")]
    [InlineData("DISCORD.EXE")]
    public async Task ShareAsync_RejectsDiscordWithoutSelectingIt(string processName)
    {
        var coordinator = new RouteCoordinator();
        var discord = new AudioSession(100, 1000, processName, "Discord", true);

        var result = await coordinator.ShareAsync(discord, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Discord cannot be shared.", result.Message);
        Assert.False(coordinator.IsSelected(discord));
    }

    [Fact]
    public async Task ShareAsync_SelectsANormalAppLocally()
    {
        var coordinator = new RouteCoordinator();
        var session = new AudioSession(200, 2000, "cloudmusic.exe", "NetEase Cloud Music", true);

        var result = await coordinator.ShareAsync(session, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Message);
        Assert.True(coordinator.IsSelected(session));
    }

    [Fact]
    public async Task GetSelectedSessions_ReturnsOnlyActiveNonDiscordSelections()
    {
        var coordinator = new RouteCoordinator();
        var chrome = new AudioSession(10, 100, "chrome.exe", "Chrome", true);
        var discord = new AudioSession(11, 101, "discord.exe", "Discord", true);
        var silent = new AudioSession(12, 102, "silent.exe", "Silent App", false);

        await coordinator.ShareAsync(chrome, CancellationToken.None);
        await coordinator.ShareAsync(discord, CancellationToken.None);
        await coordinator.ShareAsync(silent, CancellationToken.None);

        var selected = coordinator.GetSelectedSessions([chrome, discord, silent]);

        Assert.Equal([chrome], selected);
    }

    [Fact]
    public void GetSelectedSessions_RejectsNullInput()
    {
        var coordinator = new RouteCoordinator();

        Assert.Throws<ArgumentNullException>(() => coordinator.GetSelectedSessions(null!));
    }

    [Fact]
    public void GetSelectedSessions_RejectsNullElements()
    {
        var coordinator = new RouteCoordinator();

        Assert.Throws<ArgumentException>(() => coordinator.GetSelectedSessions([null!]));
    }

    [Fact]
    public async Task GetSelectedSessions_DoesNotReturnPidReusedProcess()
    {
        var coordinator = new RouteCoordinator();
        var selected = new AudioSession(400, 4000, "app.exe", "Original App", true);
        var reused = new AudioSession(400, 5000, "app.exe", "Replacement App", true);

        await coordinator.ShareAsync(selected, CancellationToken.None);

        var result = coordinator.GetSelectedSessions([reused]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task UnshareAsync_RemovesOnlyTheLocalSelection()
    {
        var coordinator = new RouteCoordinator();
        var first = new AudioSession(300, 3000, "chrome.exe", "Chrome", true);
        var second = new AudioSession(301, 3010, "cloudmusic.exe", "NetEase Cloud Music", true);

        await coordinator.ShareAsync(first, CancellationToken.None);
        await coordinator.ShareAsync(second, CancellationToken.None);
        var result = await coordinator.UnshareAsync(first, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(coordinator.IsSelected(first));
        Assert.True(coordinator.IsSelected(second));
    }

    [Fact]
    public async Task RemoveSelectionsAbsentFrom_RemovesOnlySelectedProcessesMissingFromActiveSessions()
    {
        var coordinator = new RouteCoordinator();
        var inactive = new AudioSession(400, 4000, "chrome.exe", "Chrome", true);
        var active = new AudioSession(401, 4010, "cloudmusic.exe", "NetEase Cloud Music", true);

        await coordinator.ShareAsync(inactive, CancellationToken.None);
        await coordinator.ShareAsync(active, CancellationToken.None);

        coordinator.RemoveSelectionsAbsentFrom([active]);

        Assert.False(coordinator.IsSelected(inactive));
        Assert.True(coordinator.IsSelected(active));
    }

    [Fact]
    public async Task RemoveSelectionsAbsentFrom_DeselectsAppWhenPidIsReusedByTheSameProcess()
    {
        var coordinator = new RouteCoordinator();
        var selected = new AudioSession(400, 4000, "app.exe", "Original App", true);
        var reused = new AudioSession(400, 5000, "app.exe", "Replacement App", true);

        await coordinator.ShareAsync(selected, CancellationToken.None);

        coordinator.RemoveSelectionsAbsentFrom([reused]);

        Assert.False(coordinator.IsSelected(selected));
        Assert.False(coordinator.IsSelected(reused));
    }
}
