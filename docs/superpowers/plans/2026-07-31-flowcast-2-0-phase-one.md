# FlowCast 2.0 First Phase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe exclusions, share confirmation, verified recovery, health summary, and bounded Apple-glass motion.

**Architecture:** New pure Core models decide preferences, summaries, health, and stop success. Windows services only read device and process state. WPF persists preferences, confirms a proposed route, renders status, and owns visual motion; existing route classes remain the only writers.

**Tech Stack:** .NET 8, WPF, NAudio, Voicemeeter Remote API, xUnit.

## Global Constraints

- New UI copy is Simplified Chinese.
- Do not add shortcuts, volume controls, Voicemod requirements, auto-selection, or auto-sharing.
- Protected and user-excluded processes never enter route planning or execution.
- A failed route closes B1 and uses the existing local-only recovery path.
- Discord status is advisory: report whether it runs and direct manual B1 confirmation; do not claim its microphone can be read or changed.
- Animation is at most 30 FPS and is disabled while minimized or Reduce Motion is enabled.

## File Structure

- Create `audio-share/src/AudioShare.Core/FlowCastPreferences.cs`: normalized process exclusions and Reduce Motion preference.
- Create `audio-share/src/AudioShare.Core/ShareConfirmation.cs`: selected Input and local AUX summary.
- Create `audio-share/src/AudioShare.Core/HealthSummary.cs`: health state aggregation.
- Create `audio-share/src/AudioShare.Core/StopVerificationPolicy.cs`: B1-off plus local-route completion decision.
- Create `audio-share/src/AudioShare.Windows/FlowCastHealthProbe.cs`: read-only endpoint and process observations.
- Create `audio-share/src/AudioShare.App/FlowCastPreferencesStore.cs`: LocalAppData JSON persistence.
- Create `audio-share/src/AudioShare.App/PreferencesWindow.xaml` and `.xaml.cs`: exclusion and Reduce Motion controls.
- Create `audio-share/src/AudioShare.App/ShareConfirmationWindow.xaml` and `.xaml.cs`: route confirmation UI.
- Create `audio-share/src/AudioShare.App/MotionController.cs`: storyboard lifecycle control.
- Modify `MainWindow.xaml`, `MainWindow.xaml.cs`, `VoicemeeterSharingBusService.cs`, and `AudioShare.App.csproj`.

---

### Task 1: Persisted Exclusions And Preferences

**Files:**
- Create: `audio-share/src/AudioShare.Core/FlowCastPreferences.cs`
- Create: `audio-share/src/AudioShare.App/FlowCastPreferencesStore.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/FlowCastPreferencesTests.cs`

**Produces:**

```csharp
public sealed record FlowCastPreferences(IReadOnlySet<string> ExcludedProcesses, bool ReduceMotion)
{
    public static FlowCastPreferences Empty { get; }
    public FlowCastPreferences Exclude(string processName);
    public FlowCastPreferences Restore(string processName);
    public bool IsExcluded(string processName);
}
```

- [ ] Write the failing tests.

```csharp
[Fact]
public void NormalizesExtensionsBeforePersistingExclusions()
{
    var preferences = FlowCastPreferences.Empty.Exclude("Wallpaper64.exe");
    Assert.True(preferences.IsExcluded("wallpaper64"));
}

[Fact]
public void RestoreRemovesOnlyTheRequestedProcess()
{
    var preferences = FlowCastPreferences.Empty.Exclude("chrome").Exclude("cloudmusic").Restore("chrome.exe");
    Assert.False(preferences.IsExcluded("chrome"));
    Assert.True(preferences.IsExcluded("cloudmusic"));
}
```

- [ ] Run: `dotnet test audio-share/AudioShare.sln -c Release --no-restore --filter FullyQualifiedName~FlowCastPreferencesTests`

Expected: fail because `FlowCastPreferences` does not exist.

- [ ] Implement normalized immutable process names using `Path.GetFileNameWithoutExtension`, persisted as UTF-8 JSON in `%LocalAppData%\\FlowCast\\preferences.json`. Missing or invalid JSON returns `Empty`.
- [ ] Filter `activeSessions`, `UpdateApplications`, `GetSelectedSessions`, and planner candidates with `preferences.IsExcluded`. Preserve `AudioRoutingPolicy.IsProtectedProcess` as a separate permanent rule.
- [ ] Run: `dotnet test audio-share/AudioShare.sln -c Release --no-restore`

Expected: full suite passes.
- [ ] Commit only this task's files with message `feat: persist FlowCast program exclusions`.

### Task 2: Preferences UI

**Files:**
- Create: `audio-share/src/AudioShare.App/PreferencesWindow.xaml`
- Create: `audio-share/src/AudioShare.App/PreferencesWindow.xaml.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/FlowCastPreferencesTests.cs`

**Consumes:** `FlowCastPreferences` and `FlowCastPreferencesStore`.

- [ ] Add this failing test.

```csharp
[Fact]
public void ChangingReduceMotionKeepsExcludedPrograms()
{
    var preferences = FlowCastPreferences.Empty.Exclude("wallpaper64") with { ReduceMotion = true };
    Assert.True(preferences.ReduceMotion);
    Assert.True(preferences.IsExcluded("wallpaper64.exe"));
}
```

- [ ] Create a glass-style preferences dialog with `减少动态效果`, the persisted exclusion list, `恢复显示`, `完成`, and `取消` controls.
- [ ] Add a header `设置` button and an app-card `不再显示` button. These write preferences and refresh the visible list but never alter an active route.
- [ ] Run the full suite. Manual check: exclude Chrome, restart FlowCast, confirm it is absent; restore it from Settings, refresh while Chrome plays, and confirm it can reappear.
- [ ] Commit only this task's files with message `feat: add FlowCast preference controls`.

### Task 3: Share Confirmation And Route Feedback

**Files:**
- Create: `audio-share/src/AudioShare.Core/ShareConfirmation.cs`
- Create: `audio-share/src/AudioShare.App/ShareConfirmationWindow.xaml`
- Create: `audio-share/src/AudioShare.App/ShareConfirmationWindow.xaml.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/ShareConfirmationTests.cs`

**Produces:**

```csharp
public sealed record ShareConfirmation(IReadOnlyList<AudioSession> InputSessions, IReadOnlyList<AudioSession> AuxSessions)
{
    public static ShareConfirmation Create(IEnumerable<AudioSession> audibleSessions, IEnumerable<AudioSession> selectedSessions);
}
```

- [ ] Write this failing test.

```csharp
[Fact]
public void SeparatesSelectedInputAndUnselectedAuxSessions()
{
    var chrome = new AudioSession(1, 1, "chrome.exe", "Chrome", true);
    var cloudMusic = new AudioSession(2, 2, "cloudmusic.exe", "网易云音乐", true);
    var summary = ShareConfirmation.Create([chrome, cloudMusic], [cloudMusic]);
    Assert.Equal([cloudMusic], summary.InputSessions);
    Assert.Equal([chrome], summary.AuxSessions);
}
```

- [ ] Run focused test and confirm it fails because `ShareConfirmation` does not exist.
- [ ] Implement the pure summary with `ToHashSet`, `Distinct`, selected sessions in `InputSessions`, and remaining audible sessions in `AuxSessions`.
- [ ] Create a confirmation dialog that lists `分享给朋友` and `仅自己听`; its cancel button returns `false` and performs no routing.
- [ ] In `ApplyRoutingButton_Click`, show this dialog before setting `isRoutingOperation`; proceed only when `ShowDialog() == true`.
- [ ] After the existing verifier and B1 enable both succeed, show `分享已确认`; on failure keep the existing local-only recovery and show `未开始分享，已恢复只自己听`.
- [ ] Run full tests. Manual check: cancel confirmation and verify no route changes; confirm it and verify the summary matches the active selection.
- [ ] Commit only this task's files with message `feat: confirm FlowCast sharing routes`.

### Task 4: B1 Meter And Verified Stop

**Files:**
- Create: `audio-share/src/AudioShare.Core/StopVerificationPolicy.cs`
- Modify: `audio-share/src/AudioShare.Windows/VoicemeeterSharingBusService.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/StopVerificationPolicyTests.cs`

**Produces:**

```csharp
public static class StopVerificationPolicy
{
    public static bool IsComplete(SharingBusStatus status, bool routesVerified) => !status.IsMainInputShared && routesVerified;
}
```

- [ ] Write tests for B1-on failure, route-verification failure, and B1-off plus verified-local-route success.
- [ ] Run focused test and confirm it fails because `StopVerificationPolicy` does not exist.
- [ ] Extend `SharingBusStatus` with `B1Level`, read using the existing Voicemeeter Remote API lifecycle, and keep all values read-only.
- [ ] Add a width-bound B1 meter under `B1StatusText`; bind its visual width to `Math.Clamp(B1Level * 100, 0, 100)` from the existing 500 ms poller.
- [ ] After local-only routing and B1 false, re-read bus status and call `VerifyRoutePlanAsync`; report stop success only when `IsComplete` is true. On failure retain attention state and a retryable stop action.
- [ ] Run full tests. Manual check: complete start/stop; verify B1 turns off before success text appears.
- [ ] Commit only this task's files with message `feat: verify FlowCast stop recovery`.

### Task 5: Read-Only Health Summary

**Files:**
- Create: `audio-share/src/AudioShare.Core/HealthSummary.cs`
- Create: `audio-share/src/AudioShare.Windows/FlowCastHealthProbe.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/HealthSummaryTests.cs`

**Produces:**

```csharp
public enum HealthState { Ready, Attention, Unknown }
public sealed record HealthItem(string Key, HealthState State, string Message);
public sealed record HealthSummary(IReadOnlyList<HealthItem> Items);
```

- [ ] Write a failing test where missing Input produces `HealthState.Attention`, and running Discord produces an advisory `HealthState.Ready` message mentioning manual B1 confirmation.
- [ ] Run focused test and confirm it fails because `HealthSummary` does not exist.
- [ ] Implement `HealthSummary.Create(bool bananaRunning, bool hasDefaultPlayback, bool hasInput, bool hasAux, bool discordRunning)` and `FlowCastHealthProbe.CheckAsync`. Reuse endpoint discovery and dispose all `Process.GetProcessesByName("Discord")` results.
- [ ] Add five health chips: Banana, A1, Input, AUX, Discord. Banana/Input/AUX attention disables start through existing availability rules. Discord remains advisory and offers its settings page.
- [ ] Run full tests. Manual check: close Discord and refresh; verify only the advisory chip changes. Remove an endpoint and verify share start disables.
- [ ] Commit only this task's files with message `feat: show FlowCast health summary`.

### Task 6: Bounded Motion

**Files:**
- Create: `audio-share/src/AudioShare.App/MotionController.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Modify: `audio-share/src/AudioShare.App/PreferencesWindow.xaml`
- Test: `audio-share/tests/AudioShare.Core.Tests/FlowCastPreferencesTests.cs`

**Consumes:** `FlowCastPreferences.ReduceMotion`.

- [ ] Add a failing test asserting `FlowCastPreferences.Empty.ReduceMotion` is false.
- [ ] Name the logo, top card, B1 fill, route-flow path, and list panel. Add only opacity, translate, and scale storyboards lasting 0.18 to 0.9 seconds.
- [ ] Implement `MotionController.PlayLaunch()`, `PlayRouteFlow()`, `PlayStatusTransition()`, `Suspend()`, and `Resume()`. Each method returns immediately if Reduce Motion or minimized is active.
- [ ] Call launch after startup health refresh, route flow only after verified sharing, and status transition only after verified stop or attention. Never delay routing for animation.
- [ ] Stop storyboards when minimized and assign final values immediately when Reduce Motion changes.
- [ ] Run full tests. Manual check: launch, share, stop, minimize for 30 seconds, restore, enable Reduce Motion, and repeat. Verify no animation while minimized and unchanged routing behavior.
- [ ] Commit only this task's files with message `feat: add bounded FlowCast motion`.

### Task 7: Package, Install, And Publish

**Files:**
- Modify: `audio-share/src/AudioShare.App/AudioShare.App.csproj`
- Create: `D:/codexhome/scratch/flowcast-release-2.0.0/`

- [ ] Set the agreed version in `Version`, `AssemblyVersion`, and `FileVersion` after all feature tests pass.
- [ ] Run: `dotnet test audio-share/AudioShare.sln -c Release --no-restore`

Expected: zero failing tests.
- [ ] Package with `audio-share/scripts/publish-release.ps1`, `D:\codexhome\scratch\python-3.12.10-embed-amd64.zip`, and `D:\codexhome\scratch\flowcast-packaging-20260730\nsis\nsis-3.12`.
- [ ] Verify that `AudioShare-win-x64.zip`, `AudioShare-win-x64.zip.sha256`, and `FlowCast Setup.exe` exist; compare the ZIP SHA-256 to the checksum file.
- [ ] Replace only `%LocalAppData%\\Programs\\FlowCast` published files, verify executable SHA-256 equality, relaunch, and complete the manual checks from Tasks 2-6.
- [ ] Once GitHub authorization is available, create the public `v2.0.0` Release in `Rex11929282/audio-share-updates`, upload exactly the ZIP and SHA-256 assets, and verify the latest-release API response before announcing the update.

## Plan Self-Review

- Tasks 1-2 cover exclusions and Reduce Motion preference.
- Task 3 covers explicit sharing confirmation and result messaging.
- Task 4 covers B1 visibility and verified recovery.
- Task 5 covers the automatic health checks and the honest Discord advisory boundary.
- Task 6 covers the bounded motion system.
- Task 7 covers package, installation, and public release verification.
