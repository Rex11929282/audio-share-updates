using AudioShare.App;

namespace AudioShare.Core.Tests;

public sealed class FlowCastErrorPresenterTests
{
    [Fact]
    public void RouteStartFailureUsesAPlainLanguageMessage()
    {
        var message = FlowCastErrorPresenter.FromCode("route_start_failed");

        Assert.Equal("沒有連接到分享路徑", message.Title);
        Assert.Equal(FlowCastRepairAction.ReconnectRoute, message.Action);
    }
}
