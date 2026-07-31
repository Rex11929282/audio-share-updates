namespace AudioShare.Core;

public static class StopVerificationPolicy
{
    public static bool IsComplete(SharingBusStatus status, bool routesVerified) =>
        !status.IsMainInputShared && !status.IsAuxShared && routesVerified;

    public static SharingRouteState ResultingRouteState(SharingBusStatus status, bool routesVerified) =>
        IsComplete(status, routesVerified)
            ? SharingRouteState.LocalOnly
            : SharingRouteState.Sharing;
}
