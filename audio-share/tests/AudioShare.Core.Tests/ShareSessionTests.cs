using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Countdown_DoesNotStartSharingBeforeTheConfiguredDelay()
    {
        var session = new ShareSession();

        Assert.True(session.StartCountdown(TimeSpan.FromSeconds(3), Start));
        Assert.Equal(ShareSessionState.Countdown, session.State);
        Assert.False(session.TryStart(Start.AddSeconds(2)));
        Assert.True(session.TryStart(Start.AddSeconds(3)));
        Assert.Equal(ShareSessionState.Sharing, session.State);
    }

    [Fact]
    public void Muting_KeepsTheSharingDurationRunning()
    {
        var session = new ShareSession();
        session.StartCountdown(TimeSpan.Zero, Start);
        session.TryStart(Start);

        Assert.True(session.SetMuted(true));
        Assert.Equal(ShareSessionState.Muted, session.State);
        Assert.Equal(TimeSpan.FromSeconds(12), session.Duration(Start.AddSeconds(12)));
        Assert.True(session.SetMuted(false));
        Assert.Equal(ShareSessionState.Sharing, session.State);
    }

    [Fact]
    public void Disconnect_PreservesTheFinalDurationAndReason()
    {
        var session = new ShareSession();
        session.StartCountdown(TimeSpan.Zero, Start);
        session.TryStart(Start);

        Assert.True(session.Stop(Start.AddMinutes(1).AddSeconds(24), "cloudmusic was closed", disconnected: true));
        Assert.Equal(ShareSessionState.Disconnected, session.State);
        Assert.Equal(TimeSpan.FromSeconds(84), session.FinalDuration);
        Assert.Equal("cloudmusic was closed", session.StopReason);
    }

    [Fact]
    public void NewCountdown_ResetsThePreviousSessionDuration()
    {
        var session = new ShareSession();
        session.StartCountdown(TimeSpan.Zero, Start);
        session.TryStart(Start);
        session.Stop(Start.AddSeconds(20));

        Assert.True(session.StartCountdown(TimeSpan.FromSeconds(3), Start.AddMinutes(1)));
        Assert.Equal(TimeSpan.Zero, session.FinalDuration);
        Assert.Null(session.StopReason);
    }
}
