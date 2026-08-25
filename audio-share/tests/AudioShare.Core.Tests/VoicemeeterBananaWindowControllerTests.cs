using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class VoicemeeterBananaWindowControllerTests
{
    [Fact]
    public async Task EnsureReadyAndMinimizeAsync_StartsBananaThenWaitsForRemoteApi()
    {
        var calls = new List<string>();
        var running = false;
        var readinessChecks = 0;
        var controller = new VoicemeeterBananaWindowController(
            () => running,
            () => "C:\\VB\\voicemeeterpro.exe",
            _ =>
            {
                calls.Add("start");
                running = true;
            },
            () =>
            {
                calls.Add("ready");
                return ++readinessChecks == 2;
            },
            () => calls.Add("hide"),
            TimeSpan.Zero,
            maxAttempts: 3);

        var ready = await controller.EnsureReadyAndMinimizeAsync(CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(["start", "ready", "ready", "hide"], calls);
    }

    [Fact]
    public async Task EnsureReadyAndMinimizeAsync_DoesNotClaimReadyWhenRemoteApiNeverResponds()
    {
        var controller = new VoicemeeterBananaWindowController(
            () => true,
            () => null,
            _ => throw new InvalidOperationException(),
            () => false,
            () => throw new InvalidOperationException(),
            TimeSpan.Zero,
            maxAttempts: 2);

        var ready = await controller.EnsureReadyAndMinimizeAsync(CancellationToken.None);

        Assert.False(ready);
    }
}
