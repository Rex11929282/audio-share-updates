# Audio Share Implementation Plan

**Goal:** Build a Windows utility that finds applications currently playing audio and helps the user share selected ones through the existing Voicemeeter B1 Discord route without returning Discord playback to callers.

**Architecture:** A WPF application enumerates active WASAPI sessions, keeps a local list of selected applications, and opens Windows Volume Mixer for the user to set a selected application's output to `Voicemeeter Input`. It never writes a Windows audio policy or changes any Voicemeeter, Voicemod, or Discord setting.

**Tech Stack:** .NET 8, WPF, NAudio, xUnit.

## Global Constraints

- Target Windows 10 LTSC 2021 build 19044 x64.
- Preserve the existing audio chain: Discord/general playback uses `Voicemeeter AUX Input` to A1 only; music uses the main Voicemeeter input to A1 and B1; Voicemod microphone uses B1.
- Discord is never selectable.
- Do not install, remove, enable, disable, start, restart, or reconfigure audio drivers or audio applications.
- Do not write, clear, restore, or otherwise automate a Windows per-application playback endpoint.

## Task 1: Buildable Solution

- [x] Create the .NET 8 WPF solution with Core, Windows, App, and test projects.
- [x] Add NAudio discovery dependencies and test infrastructure.
- [x] Verify the solution builds and tests run.

## Task 2: Audio Session Discovery

- [x] Define active audio-session models and deterministic filtering.
- [x] Enumerate active render sessions through WASAPI.
- [x] Exclude system/no-process sessions and deduplicate processes.

## Task 3: Safe Local Selection State

**Files:**
- Modify: `audio-share/src/AudioShare.Core/RouteCoordinator.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/RouteCoordinatorTests.cs`

- [ ] Write failing tests proving Discord cannot be selected and normal applications only change local selection state.
- [ ] Remove endpoint-routing dependencies from `RouteCoordinator` and implement minimal local select/unselect state.
- [ ] Run focused and full tests.

## Task 4: Windows Volume Mixer Launcher

**Files:**
- Create: `audio-share/src/AudioShare.Windows/VolumeMixerLauncher.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/VolumeMixerLauncherTests.cs`

- [ ] Write a failing test for the exact `ms-settings:apps-volume` URI.
- [ ] Implement a launcher that opens that URI with the shell and has no audio-policy code.
- [ ] Run all tests and manually confirm the settings page opens without changing the Discord endpoint.

## Task 5: WPF Selector and Audio Health

**Files:**
- Modify: `audio-share/src/AudioShare.App/`
- Create: `audio-share/src/AudioShare.App/AudioChainHealth.cs`
- Create: `audio-share/src/AudioShare.App/MainWindowViewModel.cs`

- [ ] Show active sessions with refresh and local share toggles.
- [ ] When a selected row is toggled on, open Volume Mixer and explain that the user must choose `Voicemeeter Input` there.
- [ ] Mark Discord rows as excluded and non-selectable.
- [ ] Show live status for `Voicemod` and `voicemeeterpro` without starting or changing either process.
- [ ] Verify the app launches and discovery works while Chrome or NetEase plays audio.

## Task 6: Operating Instructions

- [ ] Document the exact safe operating sequence and the existing Discord/Voicemeeter layout.
- [ ] State explicitly that the app records selection only; Windows Volume Mixer performs the endpoint change.
- [ ] Run `dotnet test audio-share/AudioShare.sln` and a manual safe smoke test.
