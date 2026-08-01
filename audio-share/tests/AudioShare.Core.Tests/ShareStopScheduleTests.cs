using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareStopScheduleTests
{
    [Fact]
    public void ScheduleShowsRemainingTimeUntilItIsDue()
    {
        var schedule = new ShareStopSchedule();
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        schedule.Schedule(TimeSpan.FromMinutes(30), now);

        Assert.Equal(TimeSpan.FromMinutes(30), schedule.Remaining(now));
        Assert.False(schedule.ConsumeIfDue(now.AddMinutes(29)));
        Assert.True(schedule.ConsumeIfDue(now.AddMinutes(30)));
        Assert.False(schedule.IsScheduled);
    }

    [Fact]
    public void CancelRemovesTheTimer()
    {
        var schedule = new ShareStopSchedule();
        schedule.Schedule(TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow);

        schedule.Cancel();

        Assert.Null(schedule.Remaining(DateTimeOffset.UtcNow));
        Assert.False(schedule.IsScheduled);
    }

    [Fact]
    public void DueTimerRemainsScheduledUntilTheStopOperationCancelsIt()
    {
        var schedule = new ShareStopSchedule();
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        schedule.Schedule(TimeSpan.FromMinutes(5), now);

        Assert.True(schedule.IsDue(now.AddMinutes(5)));
        Assert.True(schedule.IsScheduled);
    }

    [Fact]
    public void RejectsAZeroOrNegativeDuration()
    {
        var schedule = new ShareStopSchedule();

        Assert.Throws<ArgumentOutOfRangeException>(() => schedule.Schedule(TimeSpan.Zero, DateTimeOffset.UtcNow));
    }
}
