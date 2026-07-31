namespace AudioShare.Core;

public sealed class SharingRouteState
{
    private SharingRouteState()
    {
    }

    public static SharingRouteState Sharing { get; } = new();

    public static SharingRouteState LocalOnly { get; } = new();

    public static SharingRouteState Unknown { get; } = new();

    public static SharingRouteState Classify(
        ApplicationRouteState? routes,
        string? inputDeviceId,
        string? auxDeviceId)
    {
        if (routes is null || string.IsNullOrWhiteSpace(inputDeviceId) || string.IsNullOrWhiteSpace(auxDeviceId))
        {
            return Unknown;
        }

        if (Matches(routes.ConsoleDeviceId, inputDeviceId) || Matches(routes.MultimediaDeviceId, inputDeviceId))
        {
            return Sharing;
        }

        return Matches(routes.ConsoleDeviceId, auxDeviceId) && Matches(routes.MultimediaDeviceId, auxDeviceId)
            ? LocalOnly
            : Unknown;
    }

    public static SharingRouteState Aggregate(IEnumerable<SharingRouteState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var aggregate = LocalOnly;
        foreach (var state in states)
        {
            if (state == Sharing)
            {
                return Sharing;
            }

            if (state != LocalOnly)
            {
                aggregate = Unknown;
            }
        }

        return aggregate;
    }

    private static bool Matches(string? actualDeviceId, string expectedDeviceId) =>
        string.Equals(actualDeviceId, expectedDeviceId, StringComparison.OrdinalIgnoreCase);
}
