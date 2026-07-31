namespace AudioShare.Core;

public sealed record ShareStartRecoveryOutcome(bool LocalOnlyRecovered, bool RequiresAttention);

public static class ShareStartRecoveryPolicy
{
    public static ShareStartRecoveryOutcome FromRecoveryResult(bool localOnlyRecovered) =>
        new(localOnlyRecovered, !localOnlyRecovered);
}
