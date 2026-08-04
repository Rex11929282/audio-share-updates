namespace AudioShare.Lyrics;

internal sealed class LyricsHostShutdownCoordinator
{
    private readonly object shutdownGate = new();
    private readonly Func<CancellationToken, Task> stopRenderer;
    private readonly Func<Task> disposeRenderer;
    private readonly Func<Task> disposeConnection;
    private readonly LyricsDiagnosticLog diagnosticLog;
    private Task? shutdownTask;

    internal LyricsHostShutdownCoordinator(
        Func<CancellationToken, Task> stopRenderer,
        Func<Task> disposeRenderer,
        Func<Task> disposeConnection,
        LyricsDiagnosticLog diagnosticLog)
    {
        ArgumentNullException.ThrowIfNull(stopRenderer);
        ArgumentNullException.ThrowIfNull(disposeRenderer);
        ArgumentNullException.ThrowIfNull(disposeConnection);
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        this.stopRenderer = stopRenderer;
        this.disposeRenderer = disposeRenderer;
        this.disposeConnection = disposeConnection;
        this.diagnosticLog = diagnosticLog;
    }

    internal Task ShutdownAsync()
    {
        lock (shutdownGate)
        {
            return shutdownTask ??= RunShutdownAsync();
        }
    }

    private async Task RunShutdownAsync()
    {
        await RunStepAsync("renderer-stop", () => stopRenderer(CancellationToken.None));
        await RunStepAsync("renderer-dispose", disposeRenderer);
        await RunStepAsync("radmin-dispose", disposeConnection);
    }

    private async Task RunStepAsync(string stage, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception)
        {
            diagnosticLog.WriteHostShutdownFailure(stage, exception);
        }
    }
}
