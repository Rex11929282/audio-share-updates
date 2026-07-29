using System.Collections.Concurrent;

namespace AudioShare.Core;

public sealed class RouteCoordinator
{
    private static readonly HashSet<string> DeniedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord",
        "discord.exe",
    };

    private readonly ConcurrentDictionary<ProcessIdentity, byte> selectedProcesses = [];

    public Task<RouteResult> ShareAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();

        if (DeniedProcessNames.Contains(session.ProcessName))
        {
            return Task.FromResult(RouteResult.Failed("Discord cannot be shared."));
        }

        selectedProcesses.TryAdd(ProcessIdentity.From(session), 0);
        return Task.FromResult(RouteResult.Success());
    }

    public Task<RouteResult> UnshareAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();
        selectedProcesses.TryRemove(ProcessIdentity.From(session), out _);
        return Task.FromResult(RouteResult.Success());
    }

    public void RemoveSelectionsAbsentFrom(IEnumerable<AudioSession> activeSessions)
    {
        ArgumentNullException.ThrowIfNull(activeSessions);
        var activeProcessSet = activeSessions.Select(ProcessIdentity.From).ToHashSet();

        foreach (var process in selectedProcesses.Keys)
        {
            if (!activeProcessSet.Contains(process))
            {
                selectedProcesses.TryRemove(process, out _);
            }
        }
    }

    public bool IsSelected(AudioSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return selectedProcesses.ContainsKey(ProcessIdentity.From(session));
    }

    private readonly record struct ProcessIdentity(int ProcessId, string ProcessName)
    {
        public static ProcessIdentity From(AudioSession session) =>
            new(session.ProcessId, session.ProcessName.ToUpperInvariant());
    }
}

public sealed record RouteResult(bool Succeeded, string? Message)
{
    public static RouteResult Success() => new(true, null);

    public static RouteResult Failed(string message) => new(false, message);
}
