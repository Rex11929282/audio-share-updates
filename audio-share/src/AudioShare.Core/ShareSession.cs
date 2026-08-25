namespace AudioShare.Core;

public enum ShareSessionState
{
    Idle,
    Sharing,
    Disconnected,
}

public sealed class ShareSession
{
    private DateTimeOffset? startedAt;

    public ShareSessionState State { get; private set; } = ShareSessionState.Idle;

    public TimeSpan FinalDuration { get; private set; }

    public string? StopReason { get; private set; }

    public bool Start(DateTimeOffset now)
    {
        if (State == ShareSessionState.Sharing)
        {
            return false;
        }

        FinalDuration = TimeSpan.Zero;
        StopReason = null;
        startedAt = now;
        State = ShareSessionState.Sharing;
        return true;
    }

    public bool Stop(DateTimeOffset now, string? reason = null, bool disconnected = false)
    {
        if (State != ShareSessionState.Sharing || startedAt is null)
        {
            return false;
        }

        FinalDuration = now > startedAt ? now - startedAt.Value : TimeSpan.Zero;
        StopReason = reason;
        startedAt = null;
        State = disconnected ? ShareSessionState.Disconnected : ShareSessionState.Idle;
        return true;
    }

    public TimeSpan Duration(DateTimeOffset now) =>
        startedAt is null ? FinalDuration : now > startedAt ? now - startedAt.Value : TimeSpan.Zero;
}
