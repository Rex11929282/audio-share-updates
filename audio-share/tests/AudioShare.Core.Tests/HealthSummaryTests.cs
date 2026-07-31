using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class HealthSummaryTests
{
    [Fact]
    public void Create_MissingInputRequiresAttention()
    {
        var summary = HealthSummary.Create(
            bananaRunning: true,
            hasDefaultPlayback: true,
            hasInput: false,
            hasAux: true,
            discordRunning: false);

        var input = Assert.Single(summary.Items.Where(item => item.Key == "Input"));
        Assert.Equal(HealthState.Attention, input.State);
    }

    [Fact]
    public void Create_RunningDiscordIsReadyAndRequestsManualB1Confirmation()
    {
        var summary = HealthSummary.Create(
            bananaRunning: true,
            hasDefaultPlayback: true,
            hasInput: true,
            hasAux: true,
            discordRunning: true);

        var discord = Assert.Single(summary.Items.Where(item => item.Key == "Discord"));
        Assert.Equal(HealthState.Ready, discord.State);
        Assert.Contains("手动确认 B1", discord.Message, StringComparison.Ordinal);
    }
}
