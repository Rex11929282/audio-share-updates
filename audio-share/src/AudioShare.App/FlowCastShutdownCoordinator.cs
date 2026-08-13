using AudioShare.Core;

namespace AudioShare.App;

public sealed class FlowCastShutdownCoordinator
{
    private readonly IShareCoordinator coordinator;
    private readonly TimeSpan exitTimeout;

    public FlowCastShutdownCoordinator(IShareCoordinator coordinator, TimeSpan exitTimeout)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        if (exitTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(exitTimeout));
        }

        this.exitTimeout = exitTimeout;
    }

    public async Task ExitAsync()
    {
        using var timeout = new CancellationTokenSource(exitTimeout);
        try
        {
            await coordinator.StopAsync("FlowCast closed", exitTimeout, timeout.Token)
                .WaitAsync(exitTimeout);
        }
        catch (OperationCanceledException)
        {
            // The coordinator leaves the recovery journal in place for the next launch.
        }
        catch (TimeoutException)
        {
            // Application exit must not be held hostage by a blocked audio driver call.
        }
        catch
        {
            // The persisted recovery journal is the safe fallback for an unexpected failure.
        }
    }
}
