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
            hasAux: true);

        var input = Assert.Single(summary.Items.Where(item => item.Key == "Input"));
        Assert.Equal(HealthState.Attention, input.State);
    }

    [Fact]
    public void Create_MissingAuxRequiresAttentionWhileInputStaysReady()
    {
        var summary = HealthSummary.Create(
            bananaRunning: true,
            hasDefaultPlayback: true,
            hasInput: true,
            hasAux: false);

        var input = Assert.Single(summary.Items.Where(item => item.Key == "Input"));
        var aux = Assert.Single(summary.Items.Where(item => item.Key == "AUX"));
        Assert.Equal(HealthState.Ready, input.State);
        Assert.Equal(HealthState.Attention, aux.State);
    }

    [Fact]
    public void Create_WhenRoutingHelperNotReady_DoesNotClaimTheDeviceIsMissing()
    {
        var summary = HealthSummary.Create(
            bananaRunning: true,
            hasDefaultPlayback: true,
            hasInput: false,
            hasAux: false,
            routingHelperReady: false);

        var input = Assert.Single(summary.Items.Where(item => item.Key == "Input"));
        Assert.Equal(HealthState.Attention, input.State);
        Assert.Contains("路由組件尚未就緒", input.Message);
        Assert.DoesNotContain("未檢測到 Voicemeeter Input。", input.Message);
    }

    [Fact]
    public void Create_WhenRoutingHelperReady_ReportsTheDeviceAsMissing()
    {
        var summary = HealthSummary.Create(
            bananaRunning: true,
            hasDefaultPlayback: true,
            hasInput: false,
            hasAux: true,
            routingHelperReady: true);

        var input = Assert.Single(summary.Items.Where(item => item.Key == "Input"));
        Assert.Equal("未檢測到 Voicemeeter Input。", input.Message);
    }

    [Fact]
    public void Create_DoesNotExposeDiscord()
    {
        var summary = HealthSummary.Create(
            bananaRunning: true,
            hasDefaultPlayback: true,
            hasInput: true,
            hasAux: true);

        Assert.Equal(["Banana", "A1", "Input", "AUX"], summary.Items.Select(item => item.Key));
        Assert.DoesNotContain(summary.Items, item => item.Key == "Discord");
    }
}
