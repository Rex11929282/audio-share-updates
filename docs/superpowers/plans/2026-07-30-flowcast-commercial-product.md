# FlowCast Commercial Product Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver FlowCast as an understandable commercial Windows music-sharing product with truthful sharing status, an installer, and automatic updates.

**Architecture:** Add a route-state reader that compares each active eligible application's Console and Multimedia endpoints with the existing Input and AUX endpoint IDs. Bind the derived state to a redesigned WPF dashboard. Use a separate installer script and the existing updater's release package format so neither alters routing code.

**Tech Stack:** .NET 8 WPF, xUnit, Inno Setup, existing Python routing helper, GitHub release feed.

## Global Constraints

- Visible product name is `FlowCast`; subtitle is `音乐分享`.
- No in-app volume controls.
- No change to application routing, device settings, Voicemeeter settings, Discord settings, or Voicemod behavior.
- Stop Sharing is enabled only when at least one active eligible app has an Input/B1 route.
- An unavailable route helper results in `未确认` and disabled Stop Sharing.
- Setup.exe includes uninstall, Start menu shortcut, and desktop shortcut.

---

### Task 1: Derive Truthful Sharing State

**Files:**
- Create: `audio-share/src/AudioShare.Core/SharingRouteState.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs`

- [ ] Write failing tests for `SharingRouteState.Classify(routes, inputId, auxId)` returning `Sharing`, `LocalOnly`, or `Unknown`.
- [ ] Verify the tests fail because the type does not exist.
- [ ] Implement classification: any Input route is Sharing; all roles AUX is LocalOnly; missing data is Unknown.
- [ ] Refresh route states asynchronously after session discovery and bind the stop button to `Sharing` only.
- [ ] Run focused tests and verify they pass.

### Task 2: FlowCast Identity and Commercial Dashboard

**Files:**
- Create: `audio-share/src/AudioShare.App/Assets/flowcast-logo.png`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Modify: `audio-share/src/AudioShare.App/AudioShare.App.csproj`

- [ ] Generate an original teal FlowCast sound-wave/music-note logo.
- [ ] Replace visible Audio Share identity with `FlowCast` and `音乐分享`.
- [ ] Add a prominent status card for `正在分享`, `只自己听到`, and `未确认`.
- [ ] Describe application states as `分享给朋友` and `只自己听到`; keep B1/AUX in advanced text only.
- [ ] Build the app and visually inspect the responsive WPF layout.

### Task 3: Setup.exe Distribution

**Files:**
- Create: `audio-share/installer/FlowCast.iss`
- Modify: `audio-share/scripts/publish-release.ps1`
- Modify: `audio-share/ThirdPartyNotices.txt` only if installer licensing adds a dependency.

- [ ] Write a packaging check that requires a produced `FlowCast Setup.exe`.
- [ ] Add an Inno Setup script with per-user installation, uninstaller, Start menu shortcut, desktop shortcut, and app-version metadata.
- [ ] Extend publish script to build the installer when ISCC.exe exists, otherwise fail with the exact missing prerequisite.
- [ ] Build the installer and verify silent uninstall metadata without running uninstall.

### Task 4: Background Update Download and Install Handoff

**Files:**
- Modify: `audio-share/src/AudioShare.App/UpdateService.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/UpdateServiceTests.cs`

- [ ] Write a failing test for update download verification before installation handoff.
- [ ] Download the existing signed checksum package in the background after update availability is confirmed.
- [ ] Expose only `立即更新` after checksum verification; launch installer/update script after FlowCast exits.
- [ ] Preserve manual deferral and report download/checksum failure without replacing the current app.
- [ ] Run focused updater tests.

### Task 5: End-to-End Verification

**Files:**
- Verify only: `audio-share/AudioShare.sln`

- [ ] Run all tests, Release build, and `git diff --check`.
- [ ] Publish a FlowCast test package and installer.
- [ ] Use Pi to open FlowCast, inspect all three status states without changing routes, open the tutorial, and confirm no audio settings changed.
