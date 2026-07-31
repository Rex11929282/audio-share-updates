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

        var selectedApplicationIdentities =
            ApplicationRoutePlanner.GetSelectedApplicationIdentities(selectedSessions);
        var audible = audibleSessions
            .Where(session => session.HasAudio)
            .GroupBy(SessionIdentity.From)
            .Select(group => group.First())
            .ToArray();

        return new ShareConfirmation(
            audible.Where(session => selectedApplicationIdentities.Contains(session.ProcessName)).ToArray(),
            audible.Where(session => !selectedApplicationIdentities.Contains(session.ProcessName)).ToArray());
    }

    private readonly record struct SessionIdentity(int ProcessId, long ProcessStartUtcTicks)
    {
        public static SessionIdentity From(AudioSession session) =>
            new(session.ProcessId, session.ProcessStartUtcTicks);
    }
}
