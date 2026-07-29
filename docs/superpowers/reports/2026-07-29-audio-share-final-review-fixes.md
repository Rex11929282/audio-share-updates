# Audio Share Final Review Fixes

## Scope

This pass fixes the four final-review findings without launching Audio Share or invoking live Windows audio routing:

1. Match canonical Voicemeeter Input/AUX labels only when exact or followed by a parenthesized endpoint description, then route by the returned MMDevice ID.
2. Own each route write before awaiting its helper response and retain unresolved compensation as a retryable transaction.
3. Snapshot and restore both Console and Multimedia persisted route roles.
4. Continue compensation and explicit restore across every snapshot, retaining only failed snapshots for retry.

Pre-existing dirty `.superpowers` reports, review packages, virtual environments, and Python cache artifacts were not edited or staged.

## Implementation

- `ExternalAudioDeviceSelector` rejects substring/lookalike endpoint names while accepting normal Windows Voicemeeter friendly names.
- `ApplicationRouteState` carries the Console and Multimedia device IDs independently, including system-default `null` values.
- `ApplicationRouteExecutor` pre-registers the current write as owned, compensates with `CancellationToken.None`, aggregates all recovery failures, and exposes `HasPendingTransaction`.
- The UI keeps Restore enabled when failed Apply or Restore operations retain owned snapshots.
- The helper protocol returns a dual-role object from `get-route` and accepts `restore-route` with both role values.
- The pinned helper adapter reads and writes the two persisted role slots directly through `winappaudiorouter` 1.1.1 internals already bundled with the application.

## Verification

Regression tests were written before the corresponding production changes:

- The initial focused .NET run failed to compile because the dual-role route state, retry ownership result, restore helper method, and endpoint selector did not exist.
- The initial helper run passed 13 tests and failed 5 because `get-route` still returned one string, `restore-route` was unknown, and malformed dual-role states were not validated.

Final commands and results:

1. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --filter "FullyQualifiedName~ApplicationRouteExecutorTests|FullyQualifiedName~ExternalRoutingHelperClientTests|FullyQualifiedName~ExternalAudioDeviceSelectorTests"`
   - Passed 32, failed 0, skipped 0.
2. `dotnet test .\audio-share\AudioShare.sln --configuration Debug`
   - Passed 110, failed 0, skipped 0.
3. `& '.\.venv-router-tests\Scripts\python.exe' -m pytest '.\audio-share\router-helper\tests' -q`
   - Passed 20, failed 0 in 0.10 seconds.
4. `dotnet build .\audio-share\AudioShare.sln --configuration Release --no-restore`
   - Build succeeded with 0 warnings and 0 errors.

No live routing test, application launch, or Windows audio route write was performed.
