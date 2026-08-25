using AudioShare.App;

namespace AudioShare.Core.Tests;

public sealed class TutorialContentTests
{
    [Fact]
    public void Rules_ExplainB1AuxDiscordAndClosingWithoutVoicemod()
    {
        var text = string.Join("\n", TutorialContent.Rules);
        var tutorialParts = new List<string>(TutorialContent.Rules);

        foreach (var step in TutorialContent.Steps)
        {
            tutorialParts.Add(step.Title);
            tutorialParts.Add(step.Body);
            tutorialParts.AddRange(step.DiagramNodes);
            tutorialParts.AddRange(step.KeyRules);
        }

        var tutorialText = string.Join("\n", tutorialParts);

        Assert.Contains("B1", text, StringComparison.Ordinal);
        Assert.Contains("AUX", text, StringComparison.Ordinal);
        Assert.Contains("Discord", text, StringComparison.Ordinal);
        Assert.Contains("關閉", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Voicemod", tutorialText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Steps_ExplainActionReasonSuccessAndTroubleshootingWithoutVoicemod()
    {
        foreach (var step in TutorialContent.Steps)
        {
            Assert.Contains("怎麼做", step.Body, StringComparison.Ordinal);
            Assert.Contains("為什麼", step.Body, StringComparison.Ordinal);
            Assert.Contains("成功後", step.Body, StringComparison.Ordinal);
            Assert.Contains("出問題時", step.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Voicemod", step.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Steps_ProvideAtLeastOneConciseKeyRule()
    {
        foreach (var step in TutorialContent.Steps)
        {
            Assert.NotEmpty(step.KeyRules);
            Assert.All(step.KeyRules, rule => Assert.False(string.IsNullOrWhiteSpace(rule)));
        }
    }

    [Fact]
    public void Tutorial_ExplainsSingleProgramConfirmationAndAutomaticInternalRoutes()
    {
        var text = string.Join("\n", TutorialContent.Rules.Concat(TutorialContent.Steps.SelectMany(step => new[] { step.Title, step.Body })));

        Assert.Contains("一次只分享一個程序", text, StringComparison.Ordinal);
        Assert.Contains("勾選", text, StringComparison.Ordinal);
        Assert.Contains("確認", text, StringComparison.Ordinal);
        Assert.Contains("不會顯示 Voicemeeter Input 或 AUX", text, StringComparison.Ordinal);
        Assert.Contains("下次打開", text, StringComparison.Ordinal);
        Assert.DoesNotContain("靜音分享", text, StringComparison.Ordinal);
        Assert.Contains("系統托盤", text, StringComparison.Ordinal);
        Assert.DoesNotContain("點擊開始分享", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SetupSteps_ProvideRealScreenshotResourcesAndCallouts()
    {
        var visualSteps = TutorialContent.Steps
            .Where(step => step.Screenshot is not null)
            .ToArray();

        Assert.Contains(visualSteps, step => step.Screenshot!.ResourceUri.EndsWith("tutorial-voicemeeter-banana.png", StringComparison.Ordinal));
        Assert.Contains(visualSteps, step => step.Screenshot!.ResourceUri.EndsWith("tutorial-discord-voice-video.png", StringComparison.Ordinal));
        Assert.All(visualSteps, step => Assert.NotEmpty(step.Screenshot!.Callouts));

        Assert.Contains(visualSteps.SelectMany(step => step.Screenshot!.Callouts), callout => callout.Label.Contains("A1", StringComparison.Ordinal));
        Assert.Contains(visualSteps.SelectMany(step => step.Screenshot!.Callouts), callout => callout.Label.Contains("B1", StringComparison.Ordinal));
        Assert.Contains(visualSteps.SelectMany(step => step.Screenshot!.Callouts), callout => callout.Label.Contains("Voicemeeter Input", StringComparison.Ordinal));
        Assert.Contains(visualSteps.SelectMany(step => step.Screenshot!.Callouts), callout => callout.Label.Contains("Voicemeeter AUX Input", StringComparison.Ordinal));
        Assert.Contains(visualSteps.SelectMany(step => step.Screenshot!.Callouts), callout => callout.Label.Contains("麥克風", StringComparison.Ordinal));
        Assert.Contains(visualSteps.SelectMany(step => step.Screenshot!.Callouts), callout => callout.Label.Contains("揚聲器", StringComparison.Ordinal));
        Assert.All(
            visualSteps.SelectMany(step => step.Screenshot!.Callouts),
            callout =>
            {
                Assert.InRange(callout.X, 0, 1);
                Assert.InRange(callout.Y, 0, 1);
                Assert.InRange(callout.TargetX, 0, 1);
                Assert.InRange(callout.TargetY, 0, 1);
            });
    }
}
