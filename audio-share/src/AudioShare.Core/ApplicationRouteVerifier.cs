namespace AudioShare.Core;

public static class ApplicationRouteVerifier
{
    public static bool MatchesTarget(ApplicationRouteState? route, string? targetDeviceId)
    {
        if (route is null || string.IsNullOrWhiteSpace(targetDeviceId))
        {
            return false;
        }

        return Matches(route.ConsoleDeviceId, targetDeviceId) &&
               Matches(route.MultimediaDeviceId, targetDeviceId);
    }

    private static bool Matches(string? actualDeviceId, string expectedDeviceId) =>
        string.Equals(actualDeviceId, expectedDeviceId, StringComparison.OrdinalIgnoreCase);
}
