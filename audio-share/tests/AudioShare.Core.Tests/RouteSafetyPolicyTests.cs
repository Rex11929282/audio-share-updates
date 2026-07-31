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

    [Fact]
    public void DirectInputSharingRetainsResetGateAfterSourceClosureWithoutRouteRecoveryWork()
    {
        Assert.True(RouteSafetyPolicy.HasActiveResetGate(
            isSharing: true,
            hasRecoverySessions: false,
            hasOwnedRoutingTransaction: false));
    }

    [Fact]
    public void NoSharingStateOrRecoveryWorkHasNoResetGate()
    {
        Assert.False(RouteSafetyPolicy.HasActiveResetGate(
            isSharing: false,
            hasRecoverySessions: false,
            hasOwnedRoutingTransaction: false));
    }
}
