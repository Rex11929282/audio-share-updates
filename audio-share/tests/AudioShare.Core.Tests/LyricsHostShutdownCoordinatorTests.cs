using System.IO;

namespace AudioShare.Lyrics;

public sealed class LyricsHostShutdownCoordinatorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-host-shutdown-{Guid.NewGuid():N}");

    [Fact]
    public async Task RepeatedShutdown_IsIdempotentAndKeepsRequiredOrder()
    {
        var steps = new List<string>();
        var coordinator = CreateCoordinator(
            stopRenderer: () => steps.Add("renderer-stop"),
            disposeRenderer: () => steps.Add("renderer-dispose"),
            disposeConnection: () => steps.Add("radmin-dispose"));

        var first = coordinator.ShutdownAsync();
        var second = coordinator.ShutdownAsync();
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(["renderer-stop", "renderer-dispose", "radmin-dispose"], steps);
    }

    [Fact]
    public async Task FailedStep_IsLoggedAndRemainingCleanupStillRuns()
    {
        var steps = new List<string>();
        var coordinator = CreateCoordinator(
            stopRenderer: () =>
            {
                steps.Add("renderer-stop");
                throw new IOException("stop failed");
            },
            disposeRenderer: () => steps.Add("renderer-dispose"),
            disposeConnection: () => steps.Add("radmin-dispose"));

        await coordinator.ShutdownAsync();

        Assert.Equal(["renderer-stop", "renderer-dispose", "radmin-dispose"], steps);
        var diagnostic = File.ReadAllText(Path.Combine(directory, "diagnostics.log"));
        Assert.Contains("host-shutdown stage=renderer-stop", diagnostic);
        Assert.Contains("System.IO.IOException: stop failed", diagnostic);
    }

    private LyricsHostShutdownCoordinator CreateCoordinator(
        Action stopRenderer,
        Action disposeRenderer,
        Action disposeConnection)
    {
        Directory.CreateDirectory(directory);
        return new LyricsHostShutdownCoordinator(
            _ => InvokeAsync(stopRenderer),
            () => InvokeAsync(disposeRenderer),
            () => InvokeAsync(disposeConnection),
            new LyricsDiagnosticLog(Path.Combine(directory, "diagnostics.log")));
    }

    private static Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
