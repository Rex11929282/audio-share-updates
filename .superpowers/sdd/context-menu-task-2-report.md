# Context Menu Repair — Task 2 Report

## Status

DONE_WITH_CONCERNS

## Changes

- Added `LyricsWebViewEnvironment.GetAsync()` backed by one process-local `Lazy<Task<CoreWebView2Environment>>`.
- The environment creates and uses `%LocalAppData%\FlowCast Lyrics\WebView2` as its WebView2 user-data folder.
- Updated both the Lyrics overlay and Liquid Glass tuner to await and pass that same environment to `EnsureCoreWebView2Async(environment)`.
- Added tuner initialization stages (`initialization`, `setup`, `mapping`, and `navigation`) and `Trace.TraceError` diagnostics while retaining the generic user-facing error text.
- Added focused source-contract regression coverage for the shared environment and diagnostic boundary.
- Did not modify `audio-share/src/AudioShare.App/MainWindow.xaml.cs`, audio routing, Radmin, or lyric transport.

## RED Evidence

Command:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests|FullyQualifiedName~FlowCastLyricsStartupTests"
```

Before implementation: exit 1; 2 failed, 12 passed, 14 total. Both new tests failed because `LyricsWebViewEnvironment.GetAsync()` was absent from the overlay and tuner sources.

## GREEN Evidence

The same focused command after implementation exited 0: 14 passed, 0 failed, 14 total.

Build command:

```powershell
dotnet build audio-share\src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --no-restore
```

Result: exit 0; 0 warnings, 0 errors.

## Related Verification

- Focused Task 2 tests: 14/14 passed.
- Lyrics project build: passed with 0 warnings and 0 errors.
- An extra full `AudioShare.Core.Tests` run had 314/315 pass; unrelated `ExternalRoutingHelperClientTests.CheckHealthAsync_CancellationKillsStartedHelper` teardown hit a transient `UnauthorizedAccessException` deleting `FakePython.dll`.
- Immediate isolated rerun of that unrelated test passed 1/1. No routing code was changed.
- `git diff --check` found no whitespace errors in the Task 2 changes.

## Root Cause and Evidence

The overlay and tuner each called parameterless `EnsureCoreWebView2Async()`. The already-running overlay successfully loaded the same packaged `wwwroot` assets and `https://flowcast.local` mapping, while the second tuner control failed inside its swallowed initialization boundary. This isolates the remaining code-level difference to a second implicit default WebView2 environment/user-data-folder initialization and supports the reported default-folder conflict hypothesis.

The fix removes that difference: both controls now consume the same process-local environment with one explicit Lyrics user-data folder. The tuner also records the failing stage and exception to `Trace` if initialization, setup, mapping, or navigation fails again.

## Commit

`fix: share Lyrics WebView environment` (the commit containing this report)

## Concerns

- No interactive GUI launch was performed in this isolated coding task, so the original user-visible tuner failure was not manually reproduced after the change. Automated RED/GREEN tests and the project build verify the intended initialization contract; the added stage trace preserves evidence if the runtime failure has another cause.
- The one unrelated full-suite cleanup failure was transient and passed on isolated rerun, but the full suite was not used as the Task 2 acceptance gate.
