namespace AudioShare.Core;

public static class StopVerificationPolicy
{
    public static bool IsComplete(SharingBusStatus status, bool routesVerified) =>
        !status.IsMainInputShared && routesVerified;
}
