using AudioShare.Windows;

namespace AudioShare.Core.Tests;

public sealed class OptionalVoicemodLauncherTests
{
    [Fact]
    public async Task EnsureReadyAsync_DoesNothingWhenVoicemodIsNotInstalled()
    {
        var started = false;
        var launcher = new OptionalVoicemodLauncher(() => false, () => null, _ => started = true);

        var ready = await launcher.EnsureReadyAsync(CancellationToken.None);

        Assert.False(started);
        Assert.False(ready);
    }

    [Fact]
    public async Task EnsureReadyAsync_WaitsForVoicemodToStart()
    {
        string? startedPath = null;
        var pollCount = 0;
        var delayCount = 0;
        var launcher = new OptionalVoicemodLauncher(
            () => startedPath is not null && ++pollCount >= 3,
            () => @"C:\\Voicemod\\Voicemod.exe",
            path => startedPath = path,
            (delay, _) =>
            {
                delayCount++;
                return Task.CompletedTask;
            },
            TimeSpan.Zero,
            4,
            TimeSpan.Zero);

        var ready = await launcher.EnsureReadyAsync(CancellationToken.None);

        Assert.Equal(@"C:\\Voicemod\\Voicemod.exe", startedPath);
        Assert.True(ready);
        Assert.Equal(3, delayCount);
    }
}
