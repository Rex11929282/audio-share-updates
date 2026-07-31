namespace AudioShare.Core;

public sealed class ShareStopSchedule
{
    public DateTimeOffset? StopAt { get; private set; }

    public bool IsScheduled => StopAt is not null;

    public void Schedule(TimeSpan duration, DateTimeOffset now)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        StopAt = now.Add(duration);
    }

    public void Cancel() => StopAt = null;

    public TimeSpan? Remaining(DateTimeOffset now) => StopAt is null
        ? null
        : StopAt.Value > now ? StopAt.Value - now : TimeSpan.Zero;

    public bool ConsumeIfDue(DateTimeOffset now)
    {
        if (StopAt is null || StopAt > now)
        {
            return false;
        }

        StopAt = null;
        return true;
    }
}
