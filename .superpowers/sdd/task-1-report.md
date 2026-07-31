# Task 1 Report: Persisted Exclusions And Preferences

## Implementation

- Added `FlowCastPreferences`, an immutable preference record with normalized, case-insensitive process exclusions and `ReduceMotion`.
- Added `FlowCastPreferencesStore`, which reads and writes UTF-8 JSON at `%LocalAppData%\\FlowCast\\preferences.json`. Missing or malformed data returns `FlowCastPreferences.Empty`.
- Loaded preferences in `MainWindow` and excluded user-excluded processes from active session state, application rows, selected sessions, routing candidates, route-state checks, reset checks, and diagnostics.
- Retained `AudioRoutingPolicy.IsProtectedProcess` as the independent permanent protection rule.

## RED Evidence

After adding `FlowCastPreferencesTests`, the focused command failed with `CS0103`: `FlowCastPreferences` did not exist. Both required tests failed to compile before implementation.

## GREEN Evidence

- `dotnet test audio-share/AudioShare.sln -c Release --no-restore --filter FullyQualifiedName~FlowCastPreferencesTests`: passed 2/2.
- `dotnet test audio-share/AudioShare.sln -c Release --no-restore`: passed 151/151.
- `git diff --check`: passed with no whitespace errors.

## Changed Files

- `audio-share/src/AudioShare.Core/FlowCastPreferences.cs`
- `audio-share/src/AudioShare.App/FlowCastPreferencesStore.cs`
- `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- `audio-share/tests/AudioShare.Core.Tests/FlowCastPreferencesTests.cs`
- `.superpowers/sdd/task-1-report.md`

## Self Review

- Preferences use `Path.GetFileNameWithoutExtension`, trim whitespace, discard empty values, and recreate an immutable case-insensitive set on each change.
- User exclusions are applied before any route plan is built; permanent protected-process behavior remains separate.
- No UI, release, distribution, routing helper, volume, shortcut, Voicemod, or automatic-selection changes were made.
