using System.Collections.Concurrent;

namespace AudioShare.Core;

public sealed class RouteCoordinator
{
    private static readonly HashSet<string> DeniedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord",
        "discord.exe",
    };

    private readonly IProcessEndpointRouter router;
    private readonly string targetEndpointId;
    private readonly ConcurrentDictionary<int, byte> toolCreatedRoutes = [];
    private readonly ConcurrentDictionary<int, SemaphoreSlim> processLocks = [];

    public RouteCoordinator(IProcessEndpointRouter router, string targetEndpointId)
    {
        this.router = router ?? throw new ArgumentNullException(nameof(router));
        this.targetEndpointId = string.IsNullOrWhiteSpace(targetEndpointId)
            ? throw new ArgumentException("A target endpoint ID is required.", nameof(targetEndpointId))
            : targetEndpointId;
    }

    public async Task<RouteResult> ShareAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (DeniedProcessNames.Contains(session.ProcessName))
        {
            return RouteResult.Failed("Discord cannot be shared.");
        }

        var processLock = processLocks.GetOrAdd(session.ProcessId, _ => new SemaphoreSlim(1, 1));
        await processLock.WaitAsync(token);

        try
        {
            token.ThrowIfCancellationRequested();

            // Once routing starts, retain ownership until a confirmed clear. A native call can
            // apply the override before reporting cancellation or another failure.
            toolCreatedRoutes.TryAdd(session.ProcessId, 0);
            await router.SetEndpointAsync(session.ProcessId, targetEndpointId, token);
            return RouteResult.Success();
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && token.IsCancellationRequested)
            {
                throw;
            }

            return RouteResult.Failed(exception.Message);
        }
        finally
        {
            processLock.Release();
        }
    }

    public async Task<RouteResult> UnshareAsync(int processId, CancellationToken token)
    {
        var processLock = processLocks.GetOrAdd(processId, _ => new SemaphoreSlim(1, 1));
        await processLock.WaitAsync(token);

        try
        {
            if (!toolCreatedRoutes.ContainsKey(processId))
            {
                return RouteResult.Success();
            }

            await router.ClearEndpointAsync(processId, token);
            toolCreatedRoutes.TryRemove(processId, out _);
            return RouteResult.Success();
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && token.IsCancellationRequested)
            {
                throw;
            }

            return RouteResult.Failed(exception.Message);
        }
        finally
        {
            processLock.Release();
        }
    }

    public bool IsToolCreatedRoute(int processId) => toolCreatedRoutes.ContainsKey(processId);
}

public sealed record RouteResult(bool Succeeded, string? Message)
{
    public static RouteResult Success() => new(true, null);

    public static RouteResult Failed(string message) => new(false, message);
}
