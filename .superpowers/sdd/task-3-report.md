# Task 3: Share Confirmation And Route Feedback

## RED

Added `ShareConfirmationTests` for Chrome and NetEase Cloud Music, plus a duplicate selected Chrome audio-session case. The focused Release run failed with `CS0103` because `ShareConfirmation` did not exist.

## GREEN

Added a pure `ShareConfirmation` summary that identifies sessions by PID and process start time, keeps only audible sessions, and places each distinct session in either Input or AUX. Added an Apple-glass confirmation window that lists `分享给朋友` and `仅自己听` before any routing operation starts.

`ApplyRoutingButton_Click` now returns immediately when the dialog is cancelled. It does not set `isRoutingOperation`, apply routes, change B1, or change the sharing state. After route verification and B1 enable both succeed, FlowCast displays `分享已确认`. Verification or B1 failure attempts the existing local-only recovery path and reports `未开始分享，已恢复只自己听` only when recovery succeeds.

## Tests

- Focused: `dotnet test audio-share/AudioShare.sln -c Release --no-restore --filter FullyQualifiedName~ShareConfirmationTests` -> 2/2 passed after implementation.
- Full: `dotnet test audio-share/AudioShare.sln -c Release --no-restore` -> 155/155 passed.
- `git diff --check` passed.

## Changed Paths

- `audio-share/src/AudioShare.Core/ShareConfirmation.cs`
- `audio-share/tests/AudioShare.Core.Tests/ShareConfirmationTests.cs`
- `audio-share/src/AudioShare.App/ShareConfirmationWindow.xaml`
- `audio-share/src/AudioShare.App/ShareConfirmationWindow.xaml.cs`
- `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- `.superpowers/sdd/task-3-report.md`

## Self-Review

- The confirmation summary uses PID plus process start time rather than executable name and removes duplicate sessions.
- Cancellation occurs before any routing or B1 state mutation.
- Existing `SharingRecoveryScope` remains the recovery source; this task does not filter it through user exclusions.
- No Task 4-7 work, volume controls, Voicemod dependency, automatic selection, or automatic sharing was added.

## Status

Task 3 is implemented and verified. Ready for the requested commit.

## Review Fix

The review found that an `ApplyAsync` failure without a pending transaction, and an exception after confirmation, could bypass the verified local-only recovery path. `RecoverFromShareStartFailureAsync` now handles all post-confirmation route-application, verification, B1, prior-transaction, and exception failures through `StopSharingAndKeepLocalOnlyAsync` before reporting the error. It only reports `未开始分享，已恢复只自己听。` after that verified recovery succeeds; otherwise it leaves FlowCast in an attention state with a clear recovery error.

`ShareStartRecoveryPolicyTests` covers the recovery-result decision. Direct `MainWindow` interaction tests remain infeasible in the existing WPF test architecture, so the focused Core regression covers the shared recovery outcome while the route recovery itself continues to use the existing verified stop path.

- Focused: `dotnet test audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ShareStartRecoveryPolicyTests|FullyQualifiedName~ShareConfirmationTests|FullyQualifiedName~SharingRecoveryScopeTests" --no-restore` -> 5/5 passed.
- Full: `dotnet test audio-share/AudioShare.sln -c Release --no-restore` -> 157/157 passed.
- `git diff --check` passed.
