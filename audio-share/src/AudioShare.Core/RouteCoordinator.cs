using System.Collections.Concurrent;

namespace AudioShare.Core;

public sealed class RouteCoordinator
{
    private static readonly HashSet<string> DeniedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord",
        "discord.exe",
    };

    // Windows saves per-app audio routing by executable identity, not a transient PID.
    private readonly ConcurrentDictionary<ProcessKey, byte> selectedProcesses = [];

    public Task<RouteResult> ShareAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();

        if (DeniedProcessNames.Contains(session.ProcessName))
        {
            return Task.FromResult(RouteResult.Failed("Discord cannot be shared."));
        }

        selectedProcesses.TryAdd(ProcessKey.From(session), 0);
        return Task.FromResult(RouteResult.Success());
    }

    public Task<RouteResult> SelectOnlyAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();

        if (DeniedProcessNames.Contains(session.ProcessName))
        {
            return Task.FromResult(RouteResult.Failed("Discord cannot be shared."));
        }

        selectedProcesses.Clear();
        selectedProcesses.TryAdd(ProcessKey.From(session), 0);
        return Task.FromResult(RouteResult.Success());
    }

    public Task<RouteResult> UnshareAsync(AudioSession session, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();
        selectedProcesses.TryRemove(ProcessKey.From(session), out _);
        return Task.FromResult(RouteResult.Success());
    }

    public bool RemoveSelectionsAbsentFrom(IEnumerable<AudioSession> activeSessions)
    {
        ArgumentNullException.ThrowIfNull(activeSessions);
        var activeProcessSet = activeSessions.Select(ProcessKey.From).ToHashSet();
        var removedSelection = false;

        foreach (var process in selectedProcesses.Keys)
        {
            if (!activeProcessSet.Contains(process))
            {
                removedSelection |= selectedProcesses.TryRemove(process, out _);
            }
        }

        return removedSelection;
    }

    public bool IsSelected(AudioSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return selectedProcesses.ContainsKey(ProcessKey.From(session));
    }

    public IReadOnlyList<AudioSession> GetSelectedSessions(IEnumerable<AudioSession> activeSessions)
    {
        ArgumentNullException.ThrowIfNull(activeSessions);

        var sessions = activeSessions.ToArray();
        if (sessions.Any(session => session is null))
        {
            throw new ArgumentException("Active sessions cannot contain null elements.", nameof(activeSessions));
        }

        return sessions
            .Where(session => !DeniedProcessNames.Contains(session.ProcessName))
            .Where(IsSelected)
            .ToArray();
    }

    private readonly record struct ProcessKey(string ProcessName)
    {
        public static ProcessKey From(AudioSession session) =>
            new(session.ProcessName.ToUpperInvariant());
    }
}

public sealed record RouteResult(bool Succeeded, string? Message)
{
    public static RouteResult Success() => new(true, null);

    public static RouteResult Failed(string message) => new(false, message);
}
