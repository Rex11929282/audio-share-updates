namespace AudioShare.Core;

public sealed class SharingRecoveryScope
{
    private readonly Dictionary<SessionKey, AudioSession> sessions = [];

    public void Track(IEnumerable<AudioSession> routedSessions)
    {
        ArgumentNullException.ThrowIfNull(routedSessions);

        foreach (var session in routedSessions)
        {
            sessions[SessionKey.From(session)] = session;
        }
    }

    public IReadOnlyList<AudioSession> IncludeCurrent(IEnumerable<AudioSession> currentSessions)
    {
        ArgumentNullException.ThrowIfNull(currentSessions);

        var recovered = new Dictionary<SessionKey, AudioSession>(sessions);
        foreach (var session in currentSessions)
        {
            recovered[SessionKey.From(session)] = session;
        }

        return recovered.Values.ToArray();
    }

    public void Clear() => sessions.Clear();

    private readonly record struct SessionKey(int ProcessId, long ProcessStartUtcTicks, string ProcessName)
    {
        public static SessionKey From(AudioSession session) =>
            new(session.ProcessId, session.ProcessStartUtcTicks, session.ProcessName.ToUpperInvariant());
    }
}
