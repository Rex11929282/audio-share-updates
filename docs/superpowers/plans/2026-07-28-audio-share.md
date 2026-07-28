# Audio Share Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows desktop utility that routes selected active applications to Voicemeeter Input so their audio reaches Discord B1 without routing Discord playback back to callers.

**Architecture:** A WPF application enumerates WASAPI audio sessions and exposes share toggles. A native Windows audio-policy adapter applies a per-application render endpoint override to `Voicemeeter Input`; the app records only overrides it creates and restores them on unshare or normal exit.

**Tech Stack:** .NET 8 SDK, WPF, NAudio for audio-session enumeration, xUnit, Windows Core Audio COM interop.

## Global Constraints

- Target Windows 10 LTSC 2021 build 19044 x64.
- Use the existing `Voicemeeter Input (VB-Audio Voicemeeter VAIO)` render endpoint and existing Discord B1 configuration.
- Never route a Discord process.
- Do not install, remove, enable, or disable audio drivers.
- Restore only endpoint overrides created by this utility.
- Detect but never automatically start, restart, or reconfigure Voicemod or Voicemeeter Banana.

---

## File Structure

- `audio-share/AudioShare.sln`: solution root.
- `audio-share/src/AudioShare.App/`: WPF executable and composition root.
- `audio-share/src/AudioShare.Core/`: session models, discovery, route coordination, and endpoint-policy abstraction.
- `audio-share/src/AudioShare.Windows/`: Core Audio and per-app endpoint-policy COM adapters.
- `audio-share/tests/AudioShare.Core.Tests/`: unit tests for selection, exclusion, restoration, and failures.
- `audio-share/README.md`: setup and operating instructions.

### Task 1: Prepare the Buildable Windows Solution

**Files:**
- Create: `audio-share/AudioShare.sln`
- Create: `audio-share/src/AudioShare.Core/AudioShare.Core.csproj`
- Create: `audio-share/src/AudioShare.Windows/AudioShare.Windows.csproj`
- Create: `audio-share/src/AudioShare.App/AudioShare.App.csproj`
- Create: `audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj`
- Create: `audio-share/Directory.Packages.props`

**Interfaces:**
- Produces a .NET 8 x64 WPF application and xUnit test project.

- [ ] Install the official .NET 8 SDK x64 and verify `dotnet --list-sdks` reports an `8.0.x` SDK.
- [ ] Create the solution and four projects with `net8.0-windows10.0.19041.0`, `UseWPF=true` only in the app project, and project references `App -> Core, Windows`, `Windows -> Core`, `Tests -> Core`.
- [ ] Pin `NAudio` and `Microsoft.NET.Test.Sdk`, `xunit`, and `xunit.runner.visualstudio` in `Directory.Packages.props`.
- [ ] Run `dotnet test audio-share/AudioShare.sln`; expected result: zero tests, successful build.

### Task 2: Define and Test Route Coordination

**Files:**
- Create: `audio-share/src/AudioShare.Core/AudioSession.cs`
- Create: `audio-share/src/AudioShare.Core/IAudioSessionDiscovery.cs`
- Create: `audio-share/src/AudioShare.Core/IProcessEndpointRouter.cs`
- Create: `audio-share/src/AudioShare.Core/RouteCoordinator.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/RouteCoordinatorTests.cs`

**Interfaces:**
- `AudioSession(int ProcessId, string ProcessName, string DisplayName, bool HasAudio)`.
- `IProcessEndpointRouter.SetEndpointAsync(int processId, string endpointId, CancellationToken token)`.
- `IProcessEndpointRouter.ClearEndpointAsync(int processId, CancellationToken token)`.
- `RouteCoordinator.ShareAsync(AudioSession session, CancellationToken token)` and `UnshareAsync(int processId, CancellationToken token)`.

- [ ] Write failing xUnit tests that assert `discord.exe` is rejected, a selected process receives the Voicemeeter endpoint ID, and unshare clears only that process override.
- [ ] Run `dotnet test ... --filter RouteCoordinatorTests`; expected result: compile failure because the coordinator does not exist.
- [ ] Implement `RouteCoordinator` with a case-insensitive Discord process-name denylist, a `HashSet<int>` of tool-created routes, and exception-to-status conversion.
- [ ] Run the focused tests; expected result: all pass.

### Task 3: Enumerate Active Windows Audio Sessions

**Files:**
- Create: `audio-share/src/AudioShare.Windows/WasapiAudioSessionDiscovery.cs`
- Modify: `audio-share/src/AudioShare.Windows/AudioShare.Windows.csproj`
- Create: `audio-share/tests/AudioShare.Core.Tests/AudioSessionFilteringTests.cs`

**Interfaces:**
- `IAudioSessionDiscovery.GetActiveSessionsAsync(CancellationToken token)` returns active, non-system process sessions with a valid process ID.

- [ ] Add failing tests for filtering system/no-process sessions and deduplicating multiple sessions from one process.
- [ ] Implement the pure filtering function in Core, then make `WasapiAudioSessionDiscovery` use NAudio `MMDeviceEnumerator` and `AudioSessionManager2` to gather render sessions.
- [ ] Run all tests; expected result: session filtering tests and route coordinator tests pass.

### Task 4: Implement and Verify Per-Process Endpoint Policy

**Files:**
- Create: `audio-share/src/AudioShare.Windows/ProcessEndpointRouter.cs`
- Create: `audio-share/src/AudioShare.Windows/AudioEndpointLocator.cs`
- Create: `audio-share/src/AudioShare.Windows/Interop/AudioPolicyConfig.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/RouteCoordinatorFailureTests.cs`

**Interfaces:**
- `AudioEndpointLocator.FindVoicemeeterInputAsync()` returns the active render endpoint ID whose friendly name equals `Voicemeeter Input (VB-Audio Voicemeeter VAIO)`.
- `ProcessEndpointRouter` implements `IProcessEndpointRouter` through Windows per-app audio endpoint policy COM calls.

- [ ] Write failing coordinator tests for unavailable target endpoint and native routing failure; assert the prior share state remains unchanged and an error status is returned.
- [ ] Implement COM interop using the Windows per-app endpoint policy interface, scoped to the selected executable/app identity; set the render endpoint for all three roles and clear it on unshare.
- [ ] Add a manual verification command that routes a test media process to Voicemeeter Input, confirms it appears in Voicemeeter, then clears the override.
- [ ] Run all automated tests and the manual route/restore check; expected result: success with no change to Discord output endpoint.

### Task 5: Build the WPF Selector UI

**Files:**
- Create: `audio-share/src/AudioShare.App/App.xaml`
- Create: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Create: `audio-share/src/AudioShare.App/MainWindowViewModel.cs`
- Create: `audio-share/src/AudioShare.App/SessionRowViewModel.cs`
- Create: `audio-share/src/AudioShare.App/Services/RefreshLoop.cs`

**Interfaces:**
- `MainWindowViewModel.RefreshCommand`, `ToggleShareCommand`, and `ObservableCollection<SessionRowViewModel> Sessions`.
- `SessionRowViewModel` shows display name, process name, active status, shared status, and an error message.

- [ ] Add view-model tests for Discord rows being non-selectable and an unsuccessful route showing its error text.
- [ ] Create the UI: header, refresh button, Voicemeeter availability status, and session rows with share toggles; use a 2-second refresh interval only while the window is open.
- [ ] Bind toggles to `RouteCoordinator` and disable them while their route action is in progress.
- [ ] Add `AudioChainHealth` using process names `Voicemod` and `voicemeeterpro`; refresh its two indicators with the existing 2-second refresh loop and show an actionable stopped-process message without changing any system setting.
- [ ] Run `dotnet test` and launch the app; expected result: Chrome/NetEase rows appear during playback, Discord is visible only as excluded, and both health indicators change within three seconds after their process exits.

### Task 6: Restore Routes and Document Operation

**Files:**
- Modify: `audio-share/src/AudioShare.App/MainWindowViewModel.cs`
- Create: `audio-share/src/AudioShare.App/Services/RouteRestorer.cs`
- Create: `audio-share/README.md`

- [ ] Write a failing test that calls `RestoreAllAsync` after two shared processes and verifies only those two overrides are cleared.
- [ ] Implement normal-window-close restoration and process-exit cleanup; do not attempt restoration after a crash because endpoint state is intentionally persisted for the user to control manually.
- [ ] Document prerequisites, the exact Discord B1 input and physical output settings, how to select Chrome/NetEase, and how to recover with the refresh/unshare controls.
- [ ] Run `dotnet test audio-share/AudioShare.sln` and manually verify: select Chrome, select NetEase, start a Discord call, confirm remote audio is not returned, unshare both, and confirm audio returns to physical playback.

## Self-Review

- Spec coverage: Tasks 2-6 cover discovery, selection, Discord exclusion, target verification, route restoration, errors, and manual end-to-end validation.
- Scope: tab capture, recording, driver management, and gain controls remain excluded.
- Type consistency: all routing UI actions use `RouteCoordinator`; Windows-specific COM code is isolated behind `IProcessEndpointRouter`.
