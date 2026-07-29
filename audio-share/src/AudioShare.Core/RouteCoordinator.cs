using System.Collections.Concurrent;

namespace AudioShare.Core;

public sealed class RouteCoordinator
{
    private static readonly HashSet<string> DeniedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord",
        "discord.exe",
    };

    private readonly ConcurrentDictionary<int, byte> selectedProcessIds = [];

    public Task<RouteResult> ShareAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();

        if (DeniedProcessNames.Contains(session.ProcessName))
        {
            return Task.FromResult(RouteResult.Failed("Discord cannot be shared."));
        }

        selectedProcessIds.TryAdd(session.ProcessId, 0);
        return Task.FromResult(RouteResult.Success());
    }

    public Task<RouteResult> UnshareAsync(int processId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        selectedProcessIds.TryRemove(processId, out _);
        return Task.FromResult(RouteResult.Success());
    }

    public void RemoveSelectionsAbsentFrom(IEnumerable<int> activeProcessIds)
    {
        ArgumentNullException.ThrowIfNull(activeProcessIds);
        var activeProcessIdSet = activeProcessIds.ToHashSet();

        foreach (var processId in selectedProcessIds.Keys)
        {
            if (!activeProcessIdSet.Contains(processId))
            {
                selectedProcessIds.TryRemove(processId, out _);
            }
        }
    }

    public bool IsSelected(int processId) => selectedProcessIds.ContainsKey(processId);
}

public sealed record RouteResult(bool Succeeded, string? Message)
{
    public static RouteResult Success() => new(true, null);

    public static RouteResult Failed(string message) => new(false, message);
}
