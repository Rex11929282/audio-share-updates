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
        var discord = new AudioSession(100, processName, "Discord", true);

        var result = await coordinator.ShareAsync(discord, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Discord cannot be shared.", result.Message);
        Assert.False(coordinator.IsSelected(discord));
    }

    [Fact]
    public async Task ShareAsync_SelectsANormalAppLocally()
    {
        var coordinator = new RouteCoordinator();
        var session = new AudioSession(200, "cloudmusic.exe", "NetEase Cloud Music", true);

        var result = await coordinator.ShareAsync(session, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.Message);
        Assert.True(coordinator.IsSelected(session));
    }

    [Fact]
    public async Task UnshareAsync_RemovesOnlyTheLocalSelection()
    {
        var coordinator = new RouteCoordinator();
        var first = new AudioSession(300, "chrome.exe", "Chrome", true);
        var second = new AudioSession(301, "cloudmusic.exe", "NetEase Cloud Music", true);

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
        var inactive = new AudioSession(400, "chrome.exe", "Chrome", true);
        var active = new AudioSession(401, "cloudmusic.exe", "NetEase Cloud Music", true);

        await coordinator.ShareAsync(inactive, CancellationToken.None);
        await coordinator.ShareAsync(active, CancellationToken.None);

        coordinator.RemoveSelectionsAbsentFrom([active]);

        Assert.False(coordinator.IsSelected(inactive));
        Assert.True(coordinator.IsSelected(active));
    }

    [Fact]
    public async Task RemoveSelectionsAbsentFrom_DeselectsAppWhenPidIsReusedByADifferentProcess()
    {
        var coordinator = new RouteCoordinator();
        var selected = new AudioSession(400, "app-a.exe", "App A", true);
        var reused = new AudioSession(400, "app-b.exe", "App B", true);

        await coordinator.ShareAsync(selected, CancellationToken.None);

        coordinator.RemoveSelectionsAbsentFrom([reused]);

        Assert.False(coordinator.IsSelected(reused));
    }
}
