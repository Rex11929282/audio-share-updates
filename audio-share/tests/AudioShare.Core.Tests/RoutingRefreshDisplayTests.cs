using AudioShare.App;

namespace AudioShare.Core.Tests;

public sealed class RoutingRefreshDisplayTests
{
    [Fact]
    public void ShouldShowCheckingMessage_ShowsOnlyBeforeTheFirstSuccessfulRoutingCheck()
    {
        Assert.True(RoutingRefreshDisplay.ShouldShowCheckingMessage(false));
        Assert.False(RoutingRefreshDisplay.ShouldShowCheckingMessage(true));
    }
}
