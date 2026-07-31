namespace AudioShare.App;

public static class RoutingRefreshDisplay
{
    public static bool ShouldShowCheckingMessage(bool routingIsAvailable) => !routingIsAvailable;
}
