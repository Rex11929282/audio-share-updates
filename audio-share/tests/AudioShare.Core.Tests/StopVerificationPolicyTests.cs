using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class StopVerificationPolicyTests
{
    [Fact]
    public void IsComplete_ReturnsFalse_WhenB1IsStillShared()
    {
        var status = new SharingBusStatus(true, false, 0.2f, 0.2f);

        Assert.False(StopVerificationPolicy.IsComplete(status, routesVerified: true));
    }

    [Fact]
    public void IsComplete_ReturnsFalse_WhenAuxB1IsStillShared()
    {
        var status = new SharingBusStatus(false, true, 0.2f, 0.2f);

        Assert.False(StopVerificationPolicy.IsComplete(status, routesVerified: true));
    }

    [Fact]
    public void IsComplete_ReturnsFalse_WhenLocalOnlyRoutesAreNotVerified()
    {
        var status = new SharingBusStatus(false, false, 0.2f, 0.2f);

        Assert.False(StopVerificationPolicy.IsComplete(status, routesVerified: false));
    }

    [Fact]
    public void IsComplete_ReturnsTrue_WhenB1IsOffAndLocalOnlyRoutesAreVerified()
    {
        var status = new SharingBusStatus(false, false, 0.2f, 0.2f);

        Assert.True(StopVerificationPolicy.IsComplete(status, routesVerified: true));
    }
}
