# Audio Share Visual Tutorial Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` (recommended) or `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an in-app Simplified Chinese visual tutorial for selected-app audio sharing through Voicemeeter Banana and Discord.

**Architecture:** Keep `TutorialWindow` as the entry point. Extend `TutorialContent` with rules and visual metadata, then render real screenshots with scalable, coordinate-based callouts. The tutorial never calls routing, device, Discord, or Voicemod APIs.

**Tech Stack:** .NET 8, WPF, xUnit.

## Global Constraints

- All tutorial copy is Simplified Chinese.
- Do not mention or require Voicemod.
- Do not modify audio routing, device selection, Discord settings, or Voicemeeter settings.
- Selected apps use B1; unselected apps and stop/close behavior use AUX.
- Use Pi only for live inspection and safe screenshot capture.

---

### Task 1: Add Testable Rules and Detailed Copy

**Files:**
- Modify: `audio-share/tests/AudioShare.Core.Tests/TutorialContentTests.cs`
- Modify: `audio-share/src/AudioShare.App/TutorialContent.cs`

**Interfaces:**
- `TutorialStep` gains `IReadOnlyList<string> KeyRules`.
- `TutorialContent.Rules` returns `IReadOnlyList<string>`.

- [ ] **Step 1: Write failing rule and copy tests**

```csharp
[Fact]
public void Rules_ExplainB1AuxDiscordAndStopSharingWithoutVoicemod()
{
    var text = string.Join("\n", TutorialContent.Rules);
    Assert.Contains("B1", text, StringComparison.Ordinal);
    Assert.Contains("AUX", text, StringComparison.Ordinal);
    Assert.Contains("Discord", text, StringComparison.Ordinal);
    Assert.Contains("关闭", text, StringComparison.Ordinal);
    Assert.DoesNotContain("Voicemod", text, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Steps_ExplainActionReasonSuccessAndRecovery()
{
    var text = string.Join("\n", TutorialContent.Steps.Select(step => step.Body));
    Assert.Contains("怎么做", text, StringComparison.Ordinal);
    Assert.Contains("为什么", text, StringComparison.Ordinal);
    Assert.Contains("成功后", text, StringComparison.Ordinal);
    Assert.Contains("出问题时", text, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

```powershell
dotnet test .\audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~TutorialContentTests"
```

Expected: FAIL because `Rules` and the four labels do not exist.

- [ ] **Step 3: Add minimal rule content and detailed step labels**

```csharp
public static IReadOnlyList<string> Rules { get; } =
[
    "勾选的程序会送到 B1，朋友可以听到。",
    "未勾选的程序会送到 AUX，只有你自己听到。",
    "Discord 扬声器使用 AUX，避免朋友的声音回传。",
    "点击停止分享或关闭本程序后，当前程序会回到 AUX。",
];
```

Every step body uses `怎么做`、`为什么`、`成功后`、`出问题时`.

- [ ] **Step 4: Run focused test and verify GREEN**

Run the command from Step 2. Expected: PASS.

### Task 2: Package Safe Real Screenshot Assets

**Files:**
- Create: `audio-share/src/AudioShare.App/Assets/tutorial-discord-voice-video.png`
- Modify: `audio-share/src/AudioShare.App/AudioShare.App.csproj`

**Interfaces:**
- WPF resource URIs: `/Assets/tutorial-voicemeeter-banana.png` and `/Assets/tutorial-discord-voice-video.png`.

- [ ] **Step 1: Capture the Discord settings page safely**

Use Pi to show only Discord `语音和视频`, verify no private messages are visible, capture its window bounds, then minimize Discord. Keep the existing real Banana capture.

- [ ] **Step 2: Add explicit WPF resources**

```xml
<ItemGroup>
  <Resource Include="Assets\tutorial-voicemeeter-banana.png" />
  <Resource Include="Assets\tutorial-discord-voice-video.png" />
</ItemGroup>
```

- [ ] **Step 3: Build resource package**

```powershell
dotnet build .\audio-share\src\AudioShare.App\AudioShare.App.csproj --configuration Debug --no-restore
```

Expected: 0 warnings and 0 errors.

### Task 3: Add Scalable Screenshot Callouts

**Files:**
- Modify: `audio-share/tests/AudioShare.Core.Tests/TutorialContentTests.cs`
- Modify: `audio-share/src/AudioShare.App/TutorialContent.cs`
- Modify: `audio-share/src/AudioShare.App/TutorialWindow.cs`

**Interfaces:**
- `TutorialScreenshot(string ResourceUri, IReadOnlyList<TutorialCallout> Callouts)`.
- `TutorialCallout(string Number, string Label, double X, double Y, double TargetX, double TargetY)` uses 0–1 coordinates.
- `TutorialStep` gains nullable `TutorialScreenshot Screenshot`.

- [ ] **Step 1: Write failing screenshot metadata test**

```csharp
[Fact]
public void SetupSteps_ProvideRealScreenshotResourcesAndCallouts()
{
    var visualSteps = TutorialContent.Steps.Where(step => step.Screenshot is not null).ToArray();
    Assert.Contains(visualSteps, step => step.Screenshot!.ResourceUri.EndsWith("tutorial-voicemeeter-banana.png"));
    Assert.Contains(visualSteps, step => step.Screenshot!.ResourceUri.EndsWith("tutorial-discord-voice-video.png"));
    Assert.All(visualSteps, step => Assert.NotEmpty(step.Screenshot!.Callouts));
}
```

- [ ] **Step 2: Run focused test and verify RED**

Run the Task 1 command. Expected: FAIL because `Screenshot` does not exist.

- [ ] **Step 3: Add models, metadata, and WPF overlay renderer**

```csharp
public sealed record TutorialScreenshot(string ResourceUri, IReadOnlyList<TutorialCallout> Callouts);
public sealed record TutorialCallout(string Number, string Label, double X, double Y, double TargetX, double TargetY);
```

Render each screenshot in a scaled `Canvas` with a red `Line`, numbered `Ellipse`, arrow-head `Polygon`, and opaque white label. Add Banana callouts for A1, B1, Input, AUX; add Discord callouts for microphone and speaker selectors.

- [ ] **Step 4: Verify GREEN**

```powershell
dotnet test .\audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --configuration Debug --no-restore --filter "FullyQualifiedName~TutorialContentTests"
dotnet build .\audio-share\src\AudioShare.App\AudioShare.App.csproj --configuration Debug --no-restore
```

Expected: focused tests pass; build has 0 warnings and 0 errors.

### Task 4: Render Rules and Troubleshooting Cards

**Files:**
- Modify: `audio-share/src/AudioShare.App/TutorialWindow.cs`

**Interfaces:**
- `TutorialWindow` renders a blue `使用规则` card and an amber `常见问题` card below the tutorial steps.

- [ ] **Step 1: Render the rules card**

Show `TutorialContent.Rules` as short bullet-like text rows.

- [ ] **Step 2: Render the troubleshooting card**

Use this exact content:

```text
听不到电脑声音：检查 Banana 的 A1 是否还是你的耳机。
朋友听不到音乐：确认程序已勾选，并确认该程序正在播放声音。
朋友听到自己的声音：确认 Discord 扬声器是 Voicemeeter AUX Input，且 Discord 没有被勾选分享。
程序不在列表：先让程序开始播放，再点击重新检测。
```

- [ ] **Step 3: Build to verify**

Run the Task 3 build command. Expected: 0 warnings and 0 errors.

### Task 5: Full Validation and Pi Visual QA

**Files:**
- Verify only: `audio-share/AudioShare.sln`

- [ ] **Step 1: Run automated validation**

```powershell
dotnet test .\audio-share\AudioShare.sln --configuration Debug --no-restore
dotnet build .\audio-share\AudioShare.sln --configuration Release --no-restore
git diff --check
```

Expected: all tests pass, Release build has 0 warnings and 0 errors, `git diff --check` has no output.

- [ ] **Step 2: Publish test package**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\audio-share\scripts\publish-release.ps1 `
  -OutputDirectory D:\codexhome\scratch\audio-share-visual-tutorial-test `
  -PythonEmbedZip D:\codexhome\scratch\python-3.12.10-embed-amd64.zip `
  -HostPython D:\codexhome\scratch\task-5-router-lock-20260729\Scripts\python.exe
```

Expected: zip and SHA-256 files exist.

- [ ] **Step 3: Run Pi visual QA**

Open the packaged app, click `使用教程`, inspect that both images load, red callouts target the expected controls, Chinese copy wraps without clipping, and the close button works. Do not apply routing or change settings.
