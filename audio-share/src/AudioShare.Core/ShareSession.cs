namespace AudioShare.Core;

public enum ShareSessionState
{
    Idle,
    Countdown,
    Sharing,
    Muted,
    Disconnected,
}

public sealed class ShareSession
{
    private DateTimeOffset? startedAt;
    private DateTimeOffset? countdownEndsAt;

    public ShareSessionState State { get; private set; } = ShareSessionState.Idle;

    public TimeSpan FinalDuration { get; private set; }

    public string? StopReason { get; private set; }

    public bool StartCountdown(TimeSpan delay, DateTimeOffset now)
    {
        if (State is ShareSessionState.Countdown or ShareSessionState.Sharing or ShareSessionState.Muted)
        {
            return false;
        }

        FinalDuration = TimeSpan.Zero;
        StopReason = null;
        countdownEndsAt = now.Add(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
        State = ShareSessionState.Countdown;
        return true;
    }

    public bool CancelCountdown()
    {
        if (State != ShareSessionState.Countdown)
        {
            return false;
        }

        countdownEndsAt = null;
        State = ShareSessionState.Idle;
        return true;
    }

    public bool TryStart(DateTimeOffset now)
    {
        if (State != ShareSessionState.Countdown || countdownEndsAt is null || now < countdownEndsAt)
        {
            return false;
        }

        startedAt = now;
        countdownEndsAt = null;
        State = ShareSessionState.Sharing;
        return true;
    }

    public bool SetMuted(bool muted)
    {
        if (State is not (ShareSessionState.Sharing or ShareSessionState.Muted))
        {
            return false;
        }

        State = muted ? ShareSessionState.Muted : ShareSessionState.Sharing;
        return true;
    }

    public bool Stop(DateTimeOffset now, string? reason = null, bool disconnected = false)
    {
        if (State == ShareSessionState.Countdown)
        {
            return CancelCountdown();
        }

        if (State is not (ShareSessionState.Sharing or ShareSessionState.Muted) || startedAt is null)
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

    public TimeSpan CountdownRemaining(DateTimeOffset now) =>
        countdownEndsAt is null || now >= countdownEndsAt ? TimeSpan.Zero : countdownEndsAt.Value - now;
}
