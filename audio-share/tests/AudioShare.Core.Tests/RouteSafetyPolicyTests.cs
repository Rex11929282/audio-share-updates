using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class RouteSafetyPolicyTests
{
    [Fact]
    public void DoesNotStopWhenSharingWasNeverStarted()
    {
        Assert.False(RouteSafetyPolicy.ShouldStopSharing(false, false));
    }

    [Fact]
    public void DoesNotStopForDelayedRouteStateWhileSharing()
    {
        Assert.False(RouteSafetyPolicy.ShouldStopSharing(true, false));
    }

    [Fact]
    public void StopsWhenAnActiveShareLosesItsVoicemeeterEndpoint()
    {
        Assert.True(RouteSafetyPolicy.ShouldStopSharing(true, true));
    }
}
