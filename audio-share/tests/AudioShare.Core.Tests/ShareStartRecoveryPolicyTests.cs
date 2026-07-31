using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareStartRecoveryPolicyTests
{
    [Fact]
    public void FromRecoveryResult_WhenRecoverySucceeds_AllowsLocalOnlySuccessMessage()
    {
        var outcome = ShareStartRecoveryPolicy.FromRecoveryResult(true);

        Assert.True(outcome.LocalOnlyRecovered);
        Assert.False(outcome.RequiresAttention);
    }

    [Fact]
    public void FromRecoveryResult_WhenRecoveryFails_RequiresAttentionAndForbidsSuccessMessage()
    {
        var outcome = ShareStartRecoveryPolicy.FromRecoveryResult(false);

        Assert.False(outcome.LocalOnlyRecovered);
        Assert.True(outcome.RequiresAttention);
    }
}
