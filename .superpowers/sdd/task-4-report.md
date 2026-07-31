# Task 4 Report: B1 Meter And Verified Stop

## RED

- Added `StopVerificationPolicyTests` for B1 still enabled, route verification failure, and verified local-only recovery.
- `dotnet test audio-share/AudioShare.sln -c Release --no-restore --filter FullyQualifiedName~StopVerificationPolicyTests` failed because `SharingBusStatus` and `StopVerificationPolicy` did not exist in Core.

## GREEN

- Added Core `SharingBusStatus` and `StopVerificationPolicy`.
- `VoicemeeterSharingBusService` now reads the Banana B1 output meter through the existing Remote API login/logout lifecycle.
- The main window updates a bounded B1 meter on the existing 500 ms polling timer.
- Stop sharing now requires both B1-off and verified local-only routes before reporting success. Failure retains the recovery scope, attention state, and retryable stop action.

## Verification

- Focused: `StopVerificationPolicyTests` passed, 3/3.
- Full: `dotnet test audio-share/AudioShare.sln -c Release --no-restore` passed, 160/160.
- `git diff --check` passed.

## Changed Files

- `audio-share/src/AudioShare.Core/SharingBusStatus.cs`
- `audio-share/src/AudioShare.Core/StopVerificationPolicy.cs`
- `audio-share/src/AudioShare.Windows/VoicemeeterSharingBusService.cs`
- `audio-share/src/AudioShare.App/MainWindow.xaml`
- `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- `audio-share/tests/AudioShare.Core.Tests/StopVerificationPolicyTests.cs`
- `.superpowers/sdd/task-4-report.md`

## Manual Check

- Not run: this task does not have an attached interactive Windows control channel. The B1 output channels use the official Voicemeeter Remote API Banana mapping: output level type 3, channels 24 and 25.
