namespace AudioShare.Core;

public enum FlowCastShareState
{
    LocalOnly,
    Preparing,
    Sharing,
    Restoring,
    AttentionRequired,
}

public sealed record ShareStateSnapshot(
    FlowCastShareState State,
    AudioSession? Selected,
    long OperationId,
    string? AttentionMessage);
