namespace AudioShare.Core;

public static class ApplicationRoutePlanner
{
    public static ApplicationRoutePlan Create(
        IReadOnlyCollection<AudioSession> active,
        IReadOnlyCollection<AudioSession> selected,
        string inputDeviceId,
        string auxDeviceId)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDeviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(auxDeviceId);

        if (active.Concat(selected).Any(session => AudioRoutingPolicy.IsProtectedProcess(session.ProcessName)))
        {
            throw new ArgumentException("Protected processes cannot be routed.", nameof(active));
        }

        var selectedApplicationIdentities = GetSelectedApplicationIdentities(selected);
        var selectedOrder = selected
            .Select((session, index) => (Name: session.ProcessName, Index: index))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var sessions = active
            .GroupBy(session => session.ProcessId)
            .Select(group => group.First());
        if (selectedOrder.Count > 0)
        {
            sessions = sessions.OrderBy(session => selectedOrder.TryGetValue(session.ProcessName, out var index) ? index : int.MaxValue);
        }

        var commands = sessions
            .Select(session =>
            {
                var targetDeviceId = selectedApplicationIdentities.Contains(session.ProcessName)
                    ? inputDeviceId
                    : auxDeviceId;

                return new ApplicationRouteCommand(
                    session.ProcessId,
                    session.ProcessStartUtcTicks,
                    session.ProcessName,
                    targetDeviceId);
            })
            .ToArray();

        return new ApplicationRoutePlan(commands);
    }

    internal static HashSet<string> GetSelectedApplicationIdentities(IEnumerable<AudioSession> selected) =>
        selected
            .Select(session => session.ProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
