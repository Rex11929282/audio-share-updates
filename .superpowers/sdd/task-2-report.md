# Task 2 Report: Reversible Application Route Transactions

## Changed Paths

- `audio-share/src/AudioShare.Core/IApplicationRouteExecutor.cs`
- `audio-share/src/AudioShare.Windows/ApplicationRouteExecutor.cs`
- `audio-share/tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs`
- `.superpowers/sdd/task-2-report.md`

## Tests

- `ApplyAsync_WhenSecondWriteFails_RestoresTheFirstSnapshot`
- `ApplyAsync_ReadsEverySnapshotBeforeTheFirstWrite`
- `RestoreAsync_WhenPriorRouteWasNull_ClearsTheRoute`
- `ApplyAsync_WhenPlanIsEmpty_DoesNotCallTheHelper`
- `RestoreAsync_BeforeSuccessfulApply_DoesNotCallTheHelper`

## Commands And Results

1. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --filter FullyQualifiedName~ApplicationRouteExecutorTests`
   - Initial red result: exit 1 with `CS0246` because `IExternalRoutingHelper` did not exist.
   - Final focused result: passed 5, failed 0.
2. `dotnet test .\audio-share\AudioShare.sln --configuration Debug`
   - Result: passed 75, failed 0, skipped 0.
3. `git diff --check`
   - Result: exit 0; no whitespace errors.

## Self-Review

- The executor snapshots every command before its first route write.
- A failed write rolls back only earlier successful writes in reverse order.
- Null snapshots call `ClearRouteAsync`; non-null snapshots call `SetRouteAsync`.
- Empty plans and restores without a completed transaction return failed results without helper calls.
- The latest transaction is recorded only after every write succeeds and is consumed after a successful restore.
- The implementation reaches only `IExternalRoutingHelper`; it does not launch processes, start Python, or call Windows audio APIs.
- Task 1 source files were not changed.

## Concerns

- Rollback and restore use the caller-provided cancellation token. A caller that cancels during recovery can interrupt restoration; no live helper is included in this task, so this behavior remains unexercised beyond the abstract interface tests.

## Cancellation Recovery Fix

### Changed Paths

- `audio-share/src/AudioShare.Windows/ApplicationRouteExecutor.cs`
- `audio-share/tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs`
- `.superpowers/sdd/task-2-report.md`

### Test Added

- `ApplyAsync_WhenCallerCancelsAfterFirstWrite_RestoresTheFirstSnapshot`

### Commands And Results

1. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --filter FullyQualifiedName~ApplicationRouteExecutorTests`
   - Red result: exit 1; the new test failed with `OperationCanceledException` during compensation because rollback received the caller's canceled token.
   - Green result: passed 6, failed 0.
2. `dotnet test .\audio-share\AudioShare.sln --configuration Debug`
   - Result: passed 76, failed 0, skipped 0.

### Self-Review

- Rollback writes use `CancellationToken.None` after at least one route write succeeds.
- `RestoreAsync` rejects an already-canceled request before issuing its first write; once recovery begins, all restore writes use `CancellationToken.None`.
- No live audio, process, Python, or Windows-settings interaction was added.

### Concerns

- The prior cancellation concern is resolved for helper calls that honor cancellation tokens. Recovery can still fail only when the helper itself throws independently of cancellation.

## Compensation Continuation Fix

### Changed Paths

- `audio-share/src/AudioShare.Windows/ApplicationRouteExecutor.cs`
- `audio-share/tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs`
- `.superpowers/sdd/task-2-report.md`

### Test Added

- `ApplyAsync_WhenLaterCompensationFails_ContinuesRestoringEarlierSnapshots`

### Commands And Results

1. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --filter FullyQualifiedName~ApplicationRouteExecutorTests`
   - Red result: exit 1; a later compensation `InvalidOperationException` escaped rollback before the earlier snapshot could be restored.
   - Green result: passed 7, failed 0.
2. `dotnet test .\audio-share\AudioShare.sln --configuration Debug`
   - Result: passed 77, failed 0, skipped 0.

### Self-Review

- `ApplyAsync` catches every compensation failure, records the process ID and error, then continues through all earlier successful writes in reverse order.
- Compensation retains `CancellationToken.None` for every restore write.
- A recovery failure returns a failed `ApplicationRouteExecutionResult` with `Recovery failed` details instead of escaping from rollback.

### Concerns

- If a helper restore fails, its route remains unrecovered and is reported in the result; all other eligible prior writes are still attempted.
