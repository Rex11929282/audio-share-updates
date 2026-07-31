namespace AudioShare.Core;

public static class RouteSafetyPolicy
{
    public static bool ShouldStopSharing(bool wasSharing, bool endpointsChanged)
    {
        // Session discovery can lag behind a route change; actual selection removal is checked separately.
        return wasSharing && endpointsChanged;
    }
}
