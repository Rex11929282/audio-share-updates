namespace AudioShare.Core;

public sealed record ShareConfirmation(
    IReadOnlyList<AudioSession> InputSessions,
    IReadOnlyList<AudioSession> AuxSessions)
{
    public static ShareConfirmation Create(
        IEnumerable<AudioSession> audibleSessions,
        IEnumerable<AudioSession> selectedSessions)
    {
        ArgumentNullException.ThrowIfNull(audibleSessions);
        ArgumentNullException.ThrowIfNull(selectedSessions);

        var selected = selectedSessions
            .Select(SessionIdentity.From)
            .ToHashSet();
        var audible = audibleSessions
            .Where(session => session.HasAudio)
            .GroupBy(SessionIdentity.From)
            .Select(group => group.First())
            .ToArray();

        return new ShareConfirmation(
            audible.Where(session => selected.Contains(SessionIdentity.From(session))).ToArray(),
            audible.Where(session => !selected.Contains(SessionIdentity.From(session))).ToArray());
    }

    private readonly record struct SessionIdentity(int ProcessId, long ProcessStartUtcTicks)
    {
        public static SessionIdentity From(AudioSession session) =>
            new(session.ProcessId, session.ProcessStartUtcTicks);
    }
}
