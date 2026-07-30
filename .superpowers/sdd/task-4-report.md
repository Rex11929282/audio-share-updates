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

Full Debug solution verification run:

```text
dotnet test .\audio-share\AudioShare.sln --configuration Debug
Passed: 87
Failed: 0
Skipped: 0
```

## Concern

The pre-existing commit `e2f307a` includes this report alongside the three Task 4 source/test files. The current report update is intentionally left uncommitted to honor the requested source/tests-only commit boundary without rewriting prior history.

## Follow-Up Verification Evidence

Full, unfiltered output from the required Debug solution verification command:

```text
dotnet test .\audio-share\AudioShare.sln --configuration Debug
  正在确定要还原的项目…
  所有项目均是最新的，无法还原。
  AudioShare.Core -> D:\codexhome\worktrees\audio-share-external-routing\audio-share\src\AudioShare.Core\bin\Debug\net8.0-windows10.0.19041.0\AudioShare.Core.dll
  AudioShare.Engine -> D:\codexhome\worktrees\audio-share-external-routing\audio-share\src\AudioShare.Engine\bin\Debug\net8.0-windows10.0.19041.0\AudioShare.Engine.dll
  AudioShare.Windows -> D:\codexhome\worktrees\audio-share-external-routing\audio-share\src\AudioShare.Windows\bin\Debug\net8.0-windows10.0.19041.0\AudioShare.Windows.dll
  AudioShare.App -> D:\codexhome\worktrees\audio-share-external-routing\audio-share\src\AudioShare.App\bin\Debug\net8.0-windows10.0.19041.0\AudioShare.App.dll
  AudioShare.Core.Tests -> D:\codexhome\worktrees\audio-share-external-routing\audio-share\tests\AudioShare.Core.Tests\bin\Debug\net8.0-windows10.0.19041.0\AudioShare.Core.Tests.dll
D:\codexhome\worktrees\audio-share-external-routing\audio-share\tests\AudioShare.Core.Tests\bin\Debug\net8.0-windows10.0.19041.0\AudioShare.Core.Tests.dll (.NETCoreApp,Version=v8.0)的测试运行
VSTest 版本 17.11.1 (x64)

正在启动测试执行，请稍候...
总共 1 个测试文件与指定模式相匹配。

已通过! - 失败:     0，通过:    87，已跳过:     0，总计:    87，持续时间: 9 s - AudioShare.Core.Tests.dll (net8.0)
```
