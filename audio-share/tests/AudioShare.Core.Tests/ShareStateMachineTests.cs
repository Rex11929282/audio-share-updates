using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class ShareStateMachineTests
{
    [Fact]
    public void StartsLocalOnlyWithoutASelection()
    {
        var machine = new ShareStateMachine();

        Assert.Equal(FlowCastShareState.LocalOnly, machine.Snapshot.State);
        Assert.Null(machine.Snapshot.Selected);
    }

    [Fact]
    public void StartCommitsExactlyOneSelection()
    {
        var machine = new ShareStateMachine();
        var chrome = Session("chrome");
        var operation = machine.BeginStart(chrome);

        Assert.Equal(FlowCastShareState.Preparing, machine.Snapshot.State);
        Assert.True(machine.CompleteStart(operation));
        Assert.Equal(FlowCastShareState.Sharing, machine.Snapshot.State);
        Assert.Same(chrome, machine.Snapshot.Selected);

        Assert.Throws<InvalidOperationException>(() => machine.BeginStart(Session("cloudmusic")));
        Assert.Same(chrome, machine.Snapshot.Selected);
    }

    [Fact]
    public void StaleStartCannotOverwriteANewerRestore()
    {
        var machine = new ShareStateMachine();
        var startOperation = machine.BeginStart(Session("chrome"));
        var stopOperation = machine.BeginStop();

        Assert.False(machine.CompleteStart(startOperation));
        Assert.True(machine.CompleteRestore(stopOperation));
        Assert.Equal(FlowCastShareState.LocalOnly, machine.Snapshot.State);
        Assert.Null(machine.Snapshot.Selected);
    }

    [Fact]
    public void StateMachine_DoesNotExposeMutedStateOrTransition()
    {
        Assert.DoesNotContain("Muted", Enum.GetNames<FlowCastShareState>());
        Assert.DoesNotContain(typeof(ShareStateMachine).GetMethods(), method => method.Name is "BeginMuteChange" or "CompleteMute");
    }

    [Fact]
    public void AttentionStateKeepsTheSelectionForRepair()
    {
        var machine = new ShareStateMachine();
        var chrome = Session("chrome");
        var operation = machine.BeginStart(chrome);

        Assert.True(machine.RequireAttention(operation, "B1 did not match."));
        Assert.Equal(FlowCastShareState.AttentionRequired, machine.Snapshot.State);
        Assert.Same(chrome, machine.Snapshot.Selected);
        Assert.Equal("B1 did not match.", machine.Snapshot.AttentionMessage);
    }

    private static AudioSession Session(string processName) =>
        new(1, 10, processName, processName, HasAudio: false);
}
