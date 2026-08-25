namespace AudioShare.Core;

public sealed class ShareStateMachine
{
    private long operationId;

    public ShareStateSnapshot Snapshot { get; private set; } =
        new(FlowCastShareState.LocalOnly, null, 0, null);

    public long BeginStart(AudioSession selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (Snapshot.State != FlowCastShareState.LocalOnly)
        {
            throw new InvalidOperationException("A share operation is already active.");
        }

        Snapshot = new(FlowCastShareState.Preparing, selected, NextOperation(), null);
        return Snapshot.OperationId;
    }

    public long BeginStop()
    {
        Snapshot = Snapshot with
        {
            State = FlowCastShareState.Restoring,
            OperationId = NextOperation(),
            AttentionMessage = null,
        };
        return Snapshot.OperationId;
    }

    public bool CompleteStart(long operation) =>
        Commit(operation, FlowCastShareState.Preparing, FlowCastShareState.Sharing, Snapshot.Selected, null);

    public bool CompleteRestore(long operation) =>
        Commit(operation, FlowCastShareState.Restoring, FlowCastShareState.LocalOnly, null, null);

    public bool RequireAttention(long operation, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (operation != operationId)
        {
            return false;
        }

        Snapshot = Snapshot with
        {
            State = FlowCastShareState.AttentionRequired,
            AttentionMessage = message,
        };
        return true;
    }

    private bool Commit(
        long operation,
        FlowCastShareState expectedState,
        FlowCastShareState nextState,
        AudioSession? selected,
        string? message,
        params FlowCastShareState[] additionalExpectedStates)
    {
        if (operation != operationId ||
            (Snapshot.State != expectedState && !additionalExpectedStates.Contains(Snapshot.State)))
        {
            return false;
        }

        Snapshot = new(nextState, selected, operation, message);
        return true;
    }

    private long NextOperation() => ++operationId;
}
