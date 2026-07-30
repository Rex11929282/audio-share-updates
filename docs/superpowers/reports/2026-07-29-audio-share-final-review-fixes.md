# Audio Share Final Review Fixes

## Scope

This pass fixes the final-review findings without launching Audio Share or invoking live Windows audio routing:

1. Match canonical Voicemeeter Input/AUX labels only when exact or followed by a parenthesized endpoint description, then route by the returned MMDevice ID.
2. Own each route write before awaiting its helper response and retain unresolved compensation as a retryable transaction.
3. Snapshot and restore both Console and Multimedia persisted route roles.
4. Continue compensation and explicit restore across every snapshot, retaining only failed snapshots for retry.
5. Bind every route command and snapshot to PID, executable name, and process start time so a reused PID cannot inherit routing work.
6. Attempt Console and Multimedia writes independently, aggregate role failures, and retain a partially restored snapshot for retry.

Pre-existing dirty `.superpowers` reports, review packages, virtual environments, and Python cache artifacts were not edited or staged.

## Implementation

- `ExternalAudioDeviceSelector` rejects substring/lookalike endpoint names while accepting normal Windows Voicemeeter friendly names.
- `ApplicationRouteState` carries the Console and Multimedia device IDs independently, including system-default `null` values.
- `ApplicationRouteExecutor` pre-registers the current write as owned, compensates with `CancellationToken.None`, aggregates all recovery failures, and exposes `HasPendingTransaction`.
- The UI keeps Restore enabled when failed Apply or Restore operations retain owned snapshots.
- The helper protocol returns a dual-role object from `get-route` and accepts `restore-route` with both role values.
- Route commands, snapshots, and helper requests carry the process name and start ticks in addition to PID. The helper verifies the current `psutil` process identity before get, set, clear, or restore and returns without a route read or write on mismatch.
- The pinned helper adapter reads and writes the two persisted role slots directly through `winappaudiorouter` 1.1.1 internals already bundled with the application.
- Console and Multimedia set/clear/restore operations run independently and raise one aggregate error after all roles have been attempted. Existing snapshot ownership keeps any partial failure retryable.
- The helper protocol version is `1.1.2`; the packaged dependency remains pinned to `winappaudiorouter` 1.1.1.

## Verification

Regression tests were written before the corresponding production changes:

- The initial focused .NET run failed to compile because the dual-role route state, retry ownership result, restore helper method, and endpoint selector did not exist.
- The initial helper run passed 13 tests and failed 5 because `get-route` still returned one string, `restore-route` was unknown, and malformed dual-role states were not validated.
- The PID-reuse regression run initially failed to compile with 7 missing identity API/property errors. The helper regression run passed 20 and failed 4 because identity mismatches still wrote routes, set had no independent role implementation, and restore stopped after the first role failure.
- The intermediate helper run passed 22 and failed 2 until the existing success fixtures supplied the required stable identity. The intermediate .NET run then exposed 3 stale test-helper interface implementations.

Final commands and results:

1. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --no-restore --filter "FullyQualifiedName~ApplicationRoutePlannerTests|FullyQualifiedName~ApplicationRouteExecutorTests|FullyQualifiedName~ExternalRoutingHelperClientTests"`
   - Passed 39, failed 0, skipped 0.
2. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --no-restore`
   - Passed 110, failed 0, skipped 0.
3. `& '.\.venv-router-tests\Scripts\python.exe' -m pytest '.\audio-share\router-helper\tests' -q`
   - Passed 29, failed 0 in 0.12 seconds.
4. `dotnet build .\audio-share\AudioShare.sln --configuration Release --no-restore`
   - Build succeeded with 0 warnings and 0 errors.

No live routing test, application launch, or Windows audio route write was performed.

## Active-Session Discovery Error Handling (2026-07-30)

- `handle()` now converts unexpected active-session discovery failures from `_route_session()` into the sanitized `Router unavailable.` protocol response.
- The Python regression invokes `main()` with a failing active-session enumeration and verifies one structured JSON response with a zero exit code, preventing a traceback or nonzero helper exit from that path.

Verification:

1. `& '.\.venv-router-tests\Scripts\python.exe' -m pytest '.\audio-share\router-helper\tests' -q`
   - Passed 35, failed 0.
2. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --no-restore --filter "FullyQualifiedName~ApplicationRoutePlannerTests|FullyQualifiedName~ApplicationRouteExecutorTests|FullyQualifiedName~ExternalRoutingHelperClientTests"`
   - Passed 40, failed 0, skipped 0.
3. `dotnet test .\audio-share\AudioShare.sln --configuration Debug --no-restore`
   - Passed 111, failed 0, skipped 0.
4. `dotnet build .\audio-share\AudioShare.sln --configuration Release --no-restore`
   - Succeeded with 0 warnings and 0 errors.

No live routing test, application launch, or Windows audio route write was performed.
