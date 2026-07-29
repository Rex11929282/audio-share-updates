# Audio Share Routing Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Audio Share safely guide manual Voicemeeter routing for explicitly selected audio applications on Windows 10 build 19044.

**Architecture:** Add a small Core policy that owns protected-process exclusion and manual-routing guidance. Keep all Windows device changes out of the product: the WPF app only persists selections through `RouteCoordinator`, displays the policy guidance, and opens the existing Windows Volume Mixer URI.

**Tech Stack:** .NET 8, C#, WPF, xUnit, existing NAudio 2.2.1 dependency.

## Global Constraints

- Do not capture live application audio, install drivers, create virtual microphones, or mutate any audio endpoint.
- Do not change Windows default playback devices, Voicemeeter buses, Voicemod settings, Discord settings, or per-application device assignments.
- Exclude `discord`, `discord.exe`, `voicemod`, `voicemod.exe`, `voicemeeter`, `voicemeeter.exe`, `voicemeeterpro`, and `voicemeeterpro.exe` case-insensitively before display.
- A process selection is keyed by PID, start time, and executable name through the existing `RouteCoordinator` identity behavior.
- Selected applications are manually routed to `Voicemeeter Input`; Discord and ordinary playback stay on `Voicemeeter AUX Input`.

---

## File Structure

- Create `audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs`: protected-process policy and deterministic user guidance.
- Modify `audio-share/src/AudioShare.Core/AudioSessionFilter.cs`: reuse the policy before sessions are exposed to the app.
- Create `audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs`: policy and guidance unit tests.
- Modify `audio-share/tests/AudioShare.Core.Tests/AudioSessionFilteringTests.cs`: assert Discord and every protected process is absent from discovered sessions.
- Modify `audio-share/src/AudioShare.App/MainWindow.xaml`: replace the unrestricted mixer button with a disabled-until-selected setup action and clear safe copy.
- Modify `audio-share/src/AudioShare.App/MainWindow.xaml.cs`: derive setup availability and instructions from current selections without changing endpoints.

## Task 1: Centralize Protected Processes and Manual Guidance

**Files:**

- Create: `audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs`

**Consumes:** `AudioSession` from `AudioShare.Core`.

**Produces:** `AudioRoutingPolicy.IsProtectedProcess(string)` and `AudioRoutingPolicy.GetSetupInstruction(IReadOnlyCollection<AudioSession>)` for session discovery and WPF display.

- [ ] **Step 1: Write the failing tests**

```csharp
using AudioShare.Core;

namespace AudioShare.Core.Tests;

public sealed class AudioRoutingPolicyTests
{
    [Theory]
    [InlineData("discord")]
    [InlineData("Discord.EXE")]
    [InlineData("Voicemod")]
    [InlineData("voicemeeterpro.exe")]
    public void IsProtectedProcess_RecognizesExcludedPrograms(string processName)
    {
        Assert.True(AudioRoutingPolicy.IsProtectedProcess(processName));
    }

    [Fact]
    public void GetSetupInstruction_WithoutSelections_ExplainsThatNothingWillBeShared()
    {
        var instruction = AudioRoutingPolicy.GetSetupInstruction([]);

        Assert.Contains("No application is selected", instruction, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSetupInstruction_WithSelection_NamesTheManualTargetDevice()
    {
        var instruction = AudioRoutingPolicy.GetSetupInstruction(
        [new AudioSession(10, 100, "chrome.exe", "Chrome", true)]);

        Assert.Contains("Voicemeeter Input", instruction, StringComparison.Ordinal);
        Assert.Contains("Windows Volume Mixer", instruction, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioRoutingPolicyTests`

Expected: FAIL because `AudioRoutingPolicy` does not exist.

- [ ] **Step 3: Add the minimal policy**

```csharp
namespace AudioShare.Core;

public static class AudioRoutingPolicy
{
    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discord.exe", "voicemod", "voicemod.exe",
        "voicemeeter", "voicemeeter.exe", "voicemeeterpro", "voicemeeterpro.exe",
    };

    public static bool IsProtectedProcess(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && ProtectedProcessNames.Contains(processName);

    public static string GetSetupInstruction(IReadOnlyCollection<AudioSession> selectedSessions)
    {
        ArgumentNullException.ThrowIfNull(selectedSessions);

        return selectedSessions.Count == 0
            ? "No application is selected. Nothing will be shared."
            : "Open Windows Volume Mixer and manually set each selected application to Voicemeeter Input. " +
              "Keep Discord and normal playback on Voicemeeter AUX Input.";
    }
}
```

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioRoutingPolicyTests`

Expected: PASS with all policy tests green.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs
git commit -m "feat: add safe audio routing policy"
```

## Task 2: Filter Protected Programs Before Display

**Files:**

- Modify: `audio-share/src/AudioShare.Core/AudioSessionFilter.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/AudioSessionFilteringTests.cs`

**Consumes:** `AudioRoutingPolicy.IsProtectedProcess(string)` from Task 1.

**Produces:** `GetActiveProcessSessions` that never exposes Discord, Voicemod, or either Voicemeeter executable to the application UI.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void GetActiveProcessSessions_ExcludesDiscordAndAllProtectedPrograms()
{
    var sessions = AudioSessionFilter.GetActiveProcessSessions(
    [
        new AudioSessionCandidate(1, 10, "discord.exe", "Discord", true, false),
        new AudioSessionCandidate(2, 20, "Voicemod.exe", "Voicemod", true, false),
        new AudioSessionCandidate(3, 30, "Voicemeeter.exe", "Voicemeeter", true, false),
        new AudioSessionCandidate(4, 40, "chrome.exe", "Chrome", true, false),
    ]);

    var session = Assert.Single(sessions);
    Assert.Equal("chrome.exe", session.ProcessName);
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioSessionFilteringTests`

Expected: FAIL because Discord and `voicemeeter.exe` are currently not both filtered.

- [ ] **Step 3: Replace the local exclusion list with the policy**

```csharp
return candidates
    .Where(candidate => candidate.IsActive)
    .Where(candidate => !candidate.IsSystemSession)
    .Where(candidate => candidate.ProcessId > 0)
    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ProcessName))
    .Where(candidate => !AudioRoutingPolicy.IsProtectedProcess(candidate.ProcessName))
    // Keep the existing stable grouping and AudioSession projection.
```

Delete the former `ExcludedProcessNames` field so only one protected-process list exists.

- [ ] **Step 4: Run the focused tests**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioSessionFilteringTests`

Expected: PASS with existing filtering tests and the new protection test green.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Core/AudioSessionFilter.cs audio-share/tests/AudioShare.Core.Tests/AudioSessionFilteringTests.cs
git commit -m "fix: hide protected audio programs"
```

## Task 3: Make Setup Explicitly Selection-Gated in the WPF App

**Files:**

- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`

**Consumes:** `RouteCoordinator.GetSelectedSessions(IReadOnlyList<AudioSession>)`, `AudioRoutingPolicy.GetSetupInstruction(IReadOnlyCollection<AudioSession>)`, and existing `VolumeMixerLauncher.Open()`.

**Produces:** A `Set up selected apps` button that is disabled without a selection, opens only the Windows Volume Mixer, and always states the manual routing boundary.

- [ ] **Step 1: Add a failing interaction assertion to the policy test**

```csharp
[Fact]
public void GetSetupInstruction_WithProtectedOnlySessions_Throws()
{
    var discord = new AudioSession(20, 200, "discord.exe", "Discord", true);

    Assert.Throws<ArgumentException>(() => AudioRoutingPolicy.GetSetupInstruction([discord]));
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioRoutingPolicyTests`

Expected: FAIL because the first policy implementation accepts a protected session.

- [ ] **Step 3: Harden policy and wire the selection-gated UI**

Update the policy method before returning a message:

```csharp
if (selectedSessions.Any(session => IsProtectedProcess(session.ProcessName)))
{
    throw new ArgumentException("Protected processes cannot be configured for sharing.", nameof(selectedSessions));
}
```

In `MainWindow.xaml`, replace the always-enabled mixer button with:

```xml
<Button x:Name="SetupSelectedButton"
        Grid.Column="1"
        HorizontalAlignment="Right"
        VerticalAlignment="Center"
        IsEnabled="False"
        Background="#1D5FBF"
        Foreground="White"
        BorderBrush="#1D5FBF"
        Click="SetupSelectedButton_Click"
        Content="Set up selected apps" />
```

In `MainWindow.xaml.cs`, add this method and call it at the end of both
`UpdateApplications` and `ApplicationSelectionChanged` after selection state
has been updated:

```csharp
private void UpdateRoutingSetupState()
{
    var selected = routeCoordinator.GetSelectedSessions(Applications.Select(row => row.Session));
    SetupSelectedButton.IsEnabled = selected.Count > 0;
    InstructionText.Text = AudioRoutingPolicy.GetSetupInstruction(selected);
}
```

Replace `OpenVolumeMixerButton_Click` with:

```csharp
private void SetupSelectedButton_Click(object sender, RoutedEventArgs e)
{
    var selected = routeCoordinator.GetSelectedSessions(Applications.Select(row => row.Session));
    if (selected.Count == 0)
    {
        UpdateRoutingSetupState();
        return;
    }

    try
    {
        VolumeMixerLauncher.Open();
        ErrorPanel.Visibility = Visibility.Collapsed;
        InstructionText.Text = AudioRoutingPolicy.GetSetupInstruction(selected);
    }
    catch (Exception exception)
    {
        ShowError("Unable to open Windows Volume Mixer.", exception);
    }
}
```

Remove the duplicate in-event `VolumeMixerLauncher.Open()` call when a checkbox
is checked. Selection must only update stored intent; the explicit setup button
is the only UI action that opens Windows settings.

- [ ] **Step 4: Run focused tests and build the app**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioRoutingPolicyTests`

Expected: PASS, including protected-session rejection.

Run: `dotnet build .\\audio-share\\AudioShare.sln --configuration Debug`

Expected: `0` warnings and `0` errors.

- [ ] **Step 5: Perform manual WPF verification**

Run: `dotnet run --project .\\audio-share\\src\\AudioShare.App\\AudioShare.App.csproj`

Expected:

- Discord, Voicemod, and Voicemeeter do not appear in the active-audio list.
- The setup button is disabled before any application is selected.
- Selecting Chrome enables setup but does not open any Windows settings.
- Clicking setup opens Windows Volume Mixer and states `Voicemeeter Input` for Chrome while keeping Discord/AUX guidance visible.
- Clearing Chrome disables setup and states that nothing will be shared.

- [ ] **Step 6: Commit**

```powershell
git add audio-share/src/AudioShare.App/MainWindow.xaml audio-share/src/AudioShare.App/MainWindow.xaml.cs audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs
git commit -m "feat: guide selected app routing"
```

## Task 4: Verify the Delivery Boundary

**Files:**

- Modify: `audio-share/README.md`
- Modify: `audio-share/COMMERCIAL.md`
- Modify: `audio-share/tests/AudioShare.Core.Tests/ReleaseUpdateParserTests.cs`

**Consumes:** completed safe routing manager behavior.

**Produces:** documentation that does not imply automatic Windows routing or a working virtual microphone.

- [ ] **Step 1: Write the failing documentation assertion**

```csharp
[Fact]
public void Readme_StatesThatWindowsDeviceAssignmentsRemainManual()
{
    var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));

    Assert.Contains("does not change an application's Windows output device", readme, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the focused test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~ReleaseUpdateParserTests`

Expected: FAIL because the exact manual-routing statement is absent.

- [ ] **Step 3: Add exact limitation text**

Add this sentence in both `README.md` and `COMMERCIAL.md`:

```markdown
Audio Share does not change an application's Windows output device; the user must choose the device manually in Windows Volume Mixer.
```

Keep the existing process-loopback and virtual-microphone limitation unchanged.

- [ ] **Step 4: Run full verification**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug`

Expected: all tests PASS.

Run: `dotnet build .\\audio-share\\AudioShare.sln --configuration Release`

Expected: `0` warnings and `0` errors.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/README.md audio-share/COMMERCIAL.md audio-share/tests/AudioShare.Core.Tests/ReleaseUpdateParserTests.cs
git commit -m "docs: clarify manual audio routing"
```

## Self-Review

- Spec coverage: Task 1 centralizes all protected-process and routing guidance; Task 2 prevents those processes appearing in the share list; Task 3 makes manual setup available only for selected applications; Task 4 documents the manual boundary.
- Completeness check: every implementation step and command is explicit.
- Type consistency: Tasks 2 and 3 use only `AudioRoutingPolicy.IsProtectedProcess`, `AudioRoutingPolicy.GetSetupInstruction`, existing `AudioSession`, `RouteCoordinator`, and `VolumeMixerLauncher` APIs.
