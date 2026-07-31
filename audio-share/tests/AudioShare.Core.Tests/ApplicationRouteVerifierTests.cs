using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ApplicationRouteVerifierTests
{
    [Fact]
    public void MatchesOnlyWhenBothWindowsRolesUseTheExpectedDevice()
    {
        Assert.True(ApplicationRouteVerifier.MatchesTarget(new ApplicationRouteState("aux", "aux"), "aux"));
        Assert.False(ApplicationRouteVerifier.MatchesTarget(new ApplicationRouteState("input", "aux"), "aux"));
        Assert.False(ApplicationRouteVerifier.MatchesTarget(new ApplicationRouteState(null, "aux"), "aux"));
    }
}
