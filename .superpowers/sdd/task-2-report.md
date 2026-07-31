# Task 2 Report: Preferences UI

## Scope

Implemented only FlowCast Phase 1 Task 2. No Tasks 3-7, release artifacts, dist artifacts, routing executor changes, B1 changes, Discord changes, shortcuts, volume controls, Voicemod requirements, auto-selection, or auto-sharing were added.

## RED Evidence

Added `ChangingReduceMotionKeepsExcludedPrograms` to `FlowCastPreferencesTests` and ran:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj -c Release --no-restore
```

The test initially failed with `CS0200`: `FlowCastPreferences.ReduceMotion` was read-only and could not be assigned through a record `with` expression.

## GREEN Evidence

Changed `ReduceMotion` to `init`, preserving the immutable preferences model. The focused Release suite then passed `152/152`.

## Implementation

- Added `PreferencesWindow` using FlowCast's Apple-glass palette and Simplified Chinese controls: `设置`, `减少动态效果`, `恢复显示`, `完成`, and `取消`.
- Added a header `设置` button and an app-card `不再显示` button.
- Both controls update `FlowCastPreferencesStore` and use `RefreshApplicationsOnlyAsync` to rebuild the visible list. This method does not call the routing executor, change B1, stop sharing, or write a Windows audio route.
- Restoring an excluded program removes it from persistent preferences; a list refresh can show it again when it is actively producing audio.

## Tests

```powershell
dotnet test audio-share\AudioShare.sln -c Release --no-restore
```

Passed: `152/152` Release tests. `git diff --check` passed.

## Manual Verification

Not run: this execution has no interactive Windows input session for opening the WPF dialog and controlling active Chrome audio. The manual flow remains: hide Chrome, restart FlowCast and confirm it is absent; open `设置`, choose Chrome, click `恢复显示` then `完成`, refresh while Chrome plays, and confirm it returns.

## Self-review

- User exclusions remain filtered before visible list refresh and future routing selection.
- The permanent protected-process policy remains independent and unchanged.
- Preference actions are deliberately isolated from active route operations; route execution is not reachable from either action handler.

## Changed Files

- `audio-share/src/AudioShare.App/PreferencesWindow.xaml`
- `audio-share/src/AudioShare.App/PreferencesWindow.xaml.cs`
- `audio-share/src/AudioShare.App/MainWindow.xaml`
- `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- `audio-share/src/AudioShare.Core/FlowCastPreferences.cs`
- `audio-share/tests/AudioShare.Core.Tests/FlowCastPreferencesTests.cs`
