# Task 4 Report: Validate and Invoke Routing Helper

## Scope

Implemented only the Task 4 .NET client, manifest records, and fixture-only tests. No Task 1-3 files, UI, packaging, Python helper, or audio settings were changed.

## Implementation

- `ExternalRoutingHelperClient` implements `IExternalRoutingHelper`.
- Every helper launch reads `router-helper-manifest.json` and verifies the SHA-256 of `audio_share_router_helper.py` before starting the process.
- The client starts only the packaged `python.exe` with `UseShellExecute = false` and `ProcessStartInfo.ArgumentList` containing `-I` and the helper path.
- Requests are one JSON line on stdin. Responses must be a single JSON stdout line with boolean `ok`; nonzero exits, timeout, invalid JSON, extra stdout, missing `ok`, and health-version mismatches fail safely.
- Health returns availability details. Device-list parsing and get/set/clear route command mapping are implemented.

## Tests

The tests build a temporary fake executable named `python.exe` and use fixture JSON responses. They do not invoke Python, the router package, Windows audio APIs, or live routing.

Focused verification run:

```text
dotnet test .\audio-share\AudioShare.sln --configuration Debug --filter FullyQualifiedName~ExternalRoutingHelperClientTests
Passed: 10
Failed: 0
Skipped: 0
```

Coverage includes changed-helper integrity rejection before launch, timeout, nonzero exit, extra stdout, invalid JSON, missing `ok`, health-version mismatch, device parsing, route request mapping, and direct packaged invocation arguments.

## Concern

The requested full solution suite was not run in this checkpoint; only the focused Task 4 suite was requested and executed.
