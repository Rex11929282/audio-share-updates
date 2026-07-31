# Task 4 Report: B1 Meter And Verified Stop

## Review Fix

- Added an explicit `DisableAuxInputSharing` Voicemeeter operation and call it from the shared stop/local-only recovery path after disabling Input B1. The same path is used by stop retries.
- Added a focused service contract test for the explicit AUX B1 operation and retained policy coverage proving stop success requires both B1 switches to be off.

## Review Fix Verification

- The previous review finding was that stop verification could reject an AUX B1 switch while the stop path had no explicit operation to close it. The stop path now actively closes AUX B1 before rereading status, so a retry can recover the same condition.

## Review Fix Tests

- Focused: `dotnet test audio-share/AudioShare.sln -c Release --no-restore --filter "FullyQualifiedName~StopVerificationPolicyTests|FullyQualifiedName~VoicemeeterSharingBusServiceContractTests"`.
- Full: `dotnet test audio-share/AudioShare.sln -c Release --no-restore`.

## Review Fix Scope

- Changed only the Task 4 stop/recovery service, its MainWindow call site, the focused contract test, and this report. Tasks 5+ were not modified.

## Previous Task Details

- Updated `StopVerificationPolicy.IsComplete` to require both the main Input B1 and AUX B1 switches to be off, plus verified local-only routes.
- Added a focused regression test proving an AUX B1 switch that remains on cannot report stop success.
- Confirmed the stop flow rereads Voicemeeter status after applying local-only routes. When either B1 switch remains on, it keeps `requiresAttention`, retains the sharing recovery scope, preserves the retryable stop action, and returns failure before clearing routing state.

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
