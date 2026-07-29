# Audio Share External Application Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Route selected active applications to Voicemeeter Input and all other discovered active applications to Voicemeeter AUX Input through a bundled winappaudiorouter helper.

**Architecture:** AudioShare.Core builds a deterministic route plan. AudioShare.Windows validates the packaged helper, exchanges one JSON request per helper invocation, snapshots routes, and rolls back failures. A packaged Python 3.12 helper is the only code allowed to call winappaudiorouter.

**Tech Stack:** .NET 8 WPF, xUnit, NAudio 2.2.1, Python 3.12 embedded distribution, winappaudiorouter 1.1.1, comtypes, psutil, pycaw, PowerShell.

## Global Constraints

- Preserve Voicemeeter Input as the share bus and Voicemeeter AUX Input as the local-only bus.
- Never route Discord, Voicemod, Voicemeeter, or VoicemeeterPro; the helper must reject them too.
- Never change global default endpoints, drivers, or Discord microphone settings.
- A Chrome selection affects every concurrently active Chrome audio process; PID-specific isolation is not supported.
- Package Python 3.12 and winappaudiorouter 1.1.1 with all notices. Never download, replace, or execute routing code from the network at runtime.
- The feature is experimental and disabled if helper integrity, health, or endpoint discovery fails.
- Automated tests use fake helpers only. Live audio writes require separate user confirmation.

---

## Task 1: Create the Pure Route Plan

**Files:**
- Create: audio-share/src/AudioShare.Core/ApplicationRoutePlan.cs
- Create: audio-share/src/AudioShare.Core/ApplicationRoutePlanner.cs
- Create: audio-share/tests/AudioShare.Core.Tests/ApplicationRoutePlannerTests.cs
- Modify: audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs

**Interfaces:**
- Produces ApplicationRouteCommand(int ProcessId, string ProcessName, string TargetDeviceId).
- Produces ApplicationRouteSnapshot(int ProcessId, string ProcessName, string? PreviousDeviceId).
- Produces ApplicationRoutePlan(IReadOnlyList<ApplicationRouteCommand> Commands).
- Produces ApplicationRoutePlanner.Create(IReadOnlyCollection<AudioSession> active, IReadOnlyCollection<AudioSession> selected, string inputDeviceId, string auxDeviceId).

- [ ] **Step 1: Write failing tests**

~~~csharp
[Fact]
public void Create_SendsSelectedSessionsToInputAndUnselectedSessionsToAux()
{
    var chrome = new AudioSession(11, 100, "chrome.exe", "Chrome", true);
    var music = new AudioSession(12, 200, "cloudmusic.exe", "NetEase", true);

    var plan = ApplicationRoutePlanner.Create([chrome, music], [chrome], "input-id", "aux-id");

    Assert.Equal("input-id", Assert.Single(plan.Commands, x => x.ProcessId == 11).TargetDeviceId);
    Assert.Equal("aux-id", Assert.Single(plan.Commands, x => x.ProcessId == 12).TargetDeviceId);
}
~~~

Add tests proving duplicate sessions become one PID command and a protected process throws before a command is produced.

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test .\audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter FullyQualifiedName~ApplicationRoutePlannerTests

Expected: FAIL because ApplicationRoutePlanner does not exist.

- [ ] **Step 3: Implement the smallest planner**

~~~csharp
public sealed record ApplicationRouteCommand(int ProcessId, string ProcessName, string TargetDeviceId);
public sealed record ApplicationRouteSnapshot(int ProcessId, string ProcessName, string? PreviousDeviceId);
public sealed record ApplicationRoutePlan(IReadOnlyList<ApplicationRouteCommand> Commands);

public static class ApplicationRoutePlanner
{
    public static ApplicationRoutePlan Create(
        IReadOnlyCollection<AudioSession> active,
        IReadOnlyCollection<AudioSession> selected,
        string inputDeviceId,
        string auxDeviceId);
}
~~~

Validate null inputs and blank endpoint identifiers. Reject AudioRoutingPolicy.IsProtectedProcess names. Filter to HasAudio, group by ProcessId, sort by ProcessId, send checked PIDs to Input, and send every other discovered PID to AUX.

- [ ] **Step 4: Verify and commit**

Run: dotnet test .\audio-share\AudioShare.sln --configuration Debug

Expected: PASS.

~~~powershell
git add audio-share/src/AudioShare.Core/ApplicationRoutePlan.cs audio-share/src/AudioShare.Core/ApplicationRoutePlanner.cs audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs audio-share/tests/AudioShare.Core.Tests/ApplicationRoutePlannerTests.cs
git commit -m "feat: plan selected application routes"
~~~

## Task 2: Add Snapshot, Rollback, and Restore

**Files:**
- Create: audio-share/src/AudioShare.Core/IApplicationRouteExecutor.cs
- Create: audio-share/src/AudioShare.Windows/ApplicationRouteExecutor.cs
- Create: audio-share/tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs

**Interfaces:**
- Produces Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token).
- Produces Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token).
- Consumes IExternalRoutingHelper.GetRouteAsync, SetRouteAsync, and ClearRouteAsync.

- [ ] **Step 1: Write the failing rollback test**

~~~csharp
[Fact]
public async Task ApplyAsync_WhenSecondWriteFails_RestoresTheFirstSnapshot()
{
    var helper = new RecordingHelper(
        new Dictionary<int, string?> { [1] = "old-input", [2] = null },
        failOnSetProcessId: 2);
    var executor = new ApplicationRouteExecutor(helper);
    var plan = new ApplicationRoutePlan([new(1, "chrome.exe", "input"), new(2, "cloudmusic.exe", "aux")]);

    var result = await executor.ApplyAsync(plan, CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Contains("set:1:old-input", helper.Calls);
}
~~~

Add tests proving all snapshots are read before the first write, null snapshots restore with ClearRouteAsync, and an empty plan performs no helper call.

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test .\audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter FullyQualifiedName~ApplicationRouteExecutorTests

Expected: FAIL because ApplicationRouteExecutor does not exist.

- [ ] **Step 3: Implement transaction semantics**

~~~csharp
public interface IApplicationRouteExecutor
{
    Task<ApplicationRouteExecutionResult> ApplyAsync(ApplicationRoutePlan plan, CancellationToken token);
    Task<ApplicationRouteExecutionResult> RestoreAsync(CancellationToken token);
}
public interface IExternalRoutingHelper
{
    Task<string?> GetRouteAsync(int processId, CancellationToken token);
    Task SetRouteAsync(int processId, string deviceId, CancellationToken token);
    Task ClearRouteAsync(int processId, CancellationToken token);
}
~~~

Read every prior route before writing. On any failure, restore successful writes in reverse order. Retain a transaction only after complete success. Restore uses only the latest successful in-memory transaction.

- [ ] **Step 4: Verify and commit**

Run: dotnet test .\audio-share\AudioShare.sln --configuration Debug

Expected: PASS.

~~~powershell
git add audio-share/src/AudioShare.Core/IApplicationRouteExecutor.cs audio-share/src/AudioShare.Windows/ApplicationRouteExecutor.cs audio-share/tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs
git commit -m "feat: add reversible application route transactions"
~~~

## Task 3: Build the Closed Python Helper Protocol

**Files:**
- Create: audio-share/router-helper/audio_share_router_helper.py
- Create: audio-share/router-helper/requirements.in
- Create: audio-share/router-helper/THIRD_PARTY_NOTICES.txt
- Create: audio-share/router-helper/tests/test_protocol.py

**Interfaces:**
- Consumes one stdin JSON request with command, processId, and optional deviceId.
- Produces one stdout JSON response: {"ok": true, "value": ...} or {"ok": false, "error": "..."}.
- Supports exactly health, list-devices, get-route, set-route, clear-route.

- [ ] **Step 1: Write failing helper tests**

~~~python
def test_set_route_rejects_discord_without_calling_router(monkeypatch):
    helper = load_helper(monkeypatch, sessions=[{"pid": 8, "process_name": "discord.exe"}])

    assert helper.handle({"command": "set-route", "processId": 8, "deviceId": "input"}) == {
        "ok": False, "error": "Protected process cannot be routed."
    }
    assert helper.router.set_calls == []
~~~

Also test malformed request, unknown command, missing session, and JSON-safe endpoint serialization.

- [ ] **Step 2: Run the test to verify it fails**

Run: py -3.12 -m pytest .\audio-share\router-helper\tests -q

Expected: FAIL because the helper module does not exist.

- [ ] **Step 3: Implement the allowlisted helper**

~~~python
PROTECTED = {"discord", "discord.exe", "voicemod", "voicemod.exe",
             "voicemeeter", "voicemeeter.exe", "voicemeeterpro", "voicemeeterpro.exe"}

def handle(request: dict) -> dict:
    if request.get("command") == "health":
        return {"ok": True, "value": {"version": "1.1.1"}}
    if request.get("command") == "list-devices":
        return {"ok": True, "value": [device.__dict__ for device in war.list_output_devices()]}
    if request.get("command") not in {"get-route", "set-route", "clear-route"}:
        return {"ok": False, "error": "Unknown command."}
    return handle_process_command(request)
~~~

Resolve the active output session by PID; reject missing/protected processes. Call only war.get_app_output_device, war.set_app_output_device, or war.clear_app_output_device. Read exactly one UTF-8 line, write one JSON line to stdout, write diagnostics to stderr, and exit non-zero for malformed JSON.

- [ ] **Step 4: Add direct dependency and notices**

Create requirements.in:

~~~text
winappaudiorouter==1.1.1
~~~

Write the full winappaudiorouter MIT notice to THIRD_PARTY_NOTICES.txt. Task 5 adds the resolved dependency notices.

- [ ] **Step 5: Verify and commit**

Run: py -3.12 -m pytest .\audio-share\router-helper\tests -q

Expected: PASS.

~~~powershell
git add audio-share/router-helper
git commit -m "feat: add constrained audio routing helper"
~~~

## Task 4: Invoke the Helper Safely from .NET

**Files:**
- Create: audio-share/src/AudioShare.Windows/ExternalRoutingHelperClient.cs
- Create: audio-share/src/AudioShare.Windows/RoutingHelperManifest.cs
- Create: audio-share/tests/AudioShare.Core.Tests/ExternalRoutingHelperClientTests.cs

**Interfaces:**
- Produces ExternalRoutingHelperClient : IExternalRoutingHelper.
- Produces Task<ExternalRoutingHealth> CheckHealthAsync(CancellationToken token).
- Produces Task<IReadOnlyList<ExternalAudioDevice>> ListOutputDevicesAsync(CancellationToken token).

- [ ] **Step 1: Write failing helper-client tests**

~~~csharp
[Fact]
public async Task CheckHealthAsync_RejectsChangedHelperBeforeLaunch()
{
    using var fixture = HelperFixture.Create("{\"ok\":true,\"value\":{\"version\":\"1.1.1\"}}");
    fixture.WriteManifestWithWrongHash();

    var result = await fixture.CreateClient().CheckHealthAsync(CancellationToken.None);

    Assert.False(result.IsAvailable);
    Assert.Contains("integrity", result.Message, StringComparison.OrdinalIgnoreCase);
    Assert.False(fixture.WasLaunched);
}
~~~

Also test five-second timeout, non-zero exit, extra stdout, invalid JSON, and a mismatched helper version.

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test .\audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter FullyQualifiedName~ExternalRoutingHelperClientTests

Expected: FAIL because ExternalRoutingHelperClient does not exist.

- [ ] **Step 3: Implement manifest-first one-shot execution**

~~~csharp
public sealed record RoutingHelperManifest(string HelperSha256, string RouterVersion);
public sealed record ExternalRoutingHealth(bool IsAvailable, string Message);
public sealed record ExternalAudioDevice(string Id, string Name);

public sealed class ExternalRoutingHelperClient : IExternalRoutingHelper
{
    public Task<ExternalRoutingHealth> CheckHealthAsync(CancellationToken token);
    public Task<IReadOnlyList<ExternalAudioDevice>> ListOutputDevicesAsync(CancellationToken token);
    public Task<string?> GetRouteAsync(int processId, CancellationToken token);
    public Task SetRouteAsync(int processId, string deviceId, CancellationToken token);
    public Task ClearRouteAsync(int processId, CancellationToken token);
}
~~~

Hash the helper script and compare it to the manifest before launch. Start only the packaged python.exe with -I and argument-list APIs, send one JSON request through stdin, accept exactly one response JSON, and kill after five seconds. Never invoke a shell or interpolate a process name into a command string.

- [ ] **Step 4: Verify and commit**

Run: dotnet test .\audio-share\AudioShare.sln --configuration Debug

Expected: PASS.

~~~powershell
git add audio-share/src/AudioShare.Windows/ExternalRoutingHelperClient.cs audio-share/src/AudioShare.Windows/RoutingHelperManifest.cs audio-share/tests/AudioShare.Core.Tests/ExternalRoutingHelperClientTests.cs
git commit -m "feat: validate and invoke routing helper"
~~~

## Task 5: Package Pinned Runtime, Wheels, and Notices

**Files:**
- Create: audio-share/scripts/build-router-helper.ps1
- Create: audio-share/router-helper/requirements.lock
- Modify: audio-share/scripts/publish-release.ps1
- Modify: audio-share/ThirdPartyNotices.txt
- Modify: audio-share/COMMERCIAL.md

**Interfaces:**
- Consumes an explicit Python 3.12 embedded ZIP, helper source, and a hash-checked lock file.
- Produces publish/router-helper/python.exe, helper source, site-packages, notices, and router-helper-manifest.json.

- [ ] **Step 1: Create the dependency lock**

Run from a clean Python 3.12 build environment:

~~~powershell
py -3.12 -m pip install pip-tools
py -3.12 -m piptools compile .\audio-share\router-helper\requirements.in --generate-hashes --resolver=backtracking --output-file .\audio-share\router-helper\requirements.lock
~~~

Expected: requirements.lock pins winappaudiorouter==1.1.1 and all transitive packages with SHA-256 hashes. Review each resolved package license; accept only MIT, BSD, PSF, or Apache-2.0.

- [ ] **Step 2: Add a failing package smoke test**

~~~powershell
$output = Join-Path $env:TEMP "audio-share-router-helper-test"
.\audio-share\scripts\build-router-helper.ps1 -OutputDirectory $output -PythonEmbedZip C:\build\python-3.12.13-embed-amd64.zip
~~~

Expected: FAIL because build-router-helper.ps1 does not exist.

- [ ] **Step 3: Implement deterministic packaging**

~~~powershell
param(
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $PythonEmbedZip
)

$target = Join-Path $OutputDirectory "router-helper"
Expand-Archive -LiteralPath $PythonEmbedZip -DestinationPath $target -Force
py -3.12 -m pip install --require-hashes --no-deps --target (Join-Path $target "site-packages") -r (Join-Path $PSScriptRoot "..\router-helper\requirements.lock")
Copy-Item (Join-Path $PSScriptRoot "..\router-helper\audio_share_router_helper.py") $target
~~~

Enable site-packages in python312._pth, copy full notices for every resolved package, hash the helper, and create router-helper-manifest.json with router version 1.1.1. Call this build script from publish-release.ps1 after dotnet publish and before compression.

- [ ] **Step 4: Update commercial notices**

Add the full winappaudiorouter MIT notice to ThirdPartyNotices.txt. Update COMMERCIAL.md to list the bundled Python runtime, winappaudiorouter, comtypes, psutil, and pycaw with their notice files. Keep Audio Share source proprietary.

- [ ] **Step 5: Verify and commit**

Run:

~~~powershell
.\audio-share\scripts\publish-release.ps1 -OutputDirectory C:\release\audio-share-routing-test
Expand-Archive C:\release\audio-share-routing-test\AudioShare-win-x64.zip -DestinationPath C:\release\audio-share-routing-test\expanded -Force
Test-Path C:\release\audio-share-routing-test\expanded\router-helper\router-helper-manifest.json
~~~

Expected: publish succeeds; the ZIP contains the helper, manifest, Python runtime, and notices. Do not upload a release.

~~~powershell
git add audio-share/scripts/build-router-helper.ps1 audio-share/router-helper/requirements.lock audio-share/scripts/publish-release.ps1 audio-share/ThirdPartyNotices.txt audio-share/COMMERCIAL.md
git commit -m "build: package licensed audio routing helper"
~~~

## Task 6: Add Explicit Apply and Restore UI

**Files:**
- Modify: audio-share/src/AudioShare.App/MainWindow.xaml
- Modify: audio-share/src/AudioShare.App/MainWindow.xaml.cs
- Modify: audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs
- Modify: audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs
- Modify: audio-share/README.md

**Interfaces:**
- Consumes the planner, helper client, and executor from Tasks 1-4.
- Enables Apply only after a selected session, helper health pass, and exactly one active Voicemeeter Input and Voicemeeter AUX Input endpoint.
- Enables Restore only after a successful Apply in the current application run.

- [ ] **Step 1: Write the failing confirmation test**

~~~csharp
[Fact]
public void GetRouteConfirmationText_StatesAllChromeProcessesAreAffected()
{
    var text = AudioRoutingPolicy.GetRouteConfirmationText([
        new AudioSession(1, 10, "chrome.exe", "Chrome", true)]);

    Assert.Contains("Chrome", text, StringComparison.Ordinal);
    Assert.Contains("all active processes", text, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Discord", text, StringComparison.OrdinalIgnoreCase);
}
~~~

- [ ] **Step 2: Run the test to verify it fails**

Run: dotnet test .\audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter FullyQualifiedName~AudioRoutingPolicyTests

Expected: FAIL because GetRouteConfirmationText does not exist.

- [ ] **Step 3: Add guarded UI controls**

~~~xml
<Button x:Name="ApplyRoutingButton" IsEnabled="False"
        Click="ApplyRoutingButton_Click" Content="Apply selected audio routing" />
<Button x:Name="RestoreRoutingButton" IsEnabled="False"
        Click="RestoreRoutingButton_Click" Content="Restore this routing" />
~~~

Refresh may invoke only health and list-devices. Apply must build the plan and show a MessageBox with selected-to-Input and unselected-to-AUX counts, experimental notice, and Discord warning. Execute only when the user chooses Yes. Restore needs another Yes. Keep Set up selected apps as manual fallback. Checkbox changes never write routes.

- [ ] **Step 4: Document and run non-live verification**

Update README with helper fallback, application-identity scope, explicit restore, protected process exclusion, and no runtime download. Run:

~~~powershell
dotnet test .\audio-share\AudioShare.sln --configuration Debug
dotnet build .\audio-share\AudioShare.sln --configuration Release
py -3.12 -m pytest .\audio-share\router-helper\tests -q
~~~

Expected: all commands pass without a live route write.

- [ ] **Step 5: Commit and request live-test consent**

~~~powershell
git add audio-share/src/AudioShare.App/MainWindow.xaml audio-share/src/AudioShare.App/MainWindow.xaml.cs audio-share/src/AudioShare.Core/AudioRoutingPolicy.cs audio-share/tests/AudioShare.Core.Tests/AudioRoutingPolicyTests.cs audio-share/README.md
git commit -m "feat: add guarded external audio routing controls"
~~~

After this commit, ask for explicit permission to test one non-Discord music application. If approved, verify music reaches B1, Discord friend audio remains local on AUX/A1, then restore the original route. Never test Discord, Voicemod, or Voicemeeter.

## Plan Self-Review

- Tasks 1-2 implement deterministic selection, snapshots, rollback, and restore.
- Tasks 3-5 implement the constrained helper, integrity gate, pinned release packaging, and commercial notices.
- Task 6 implements explicit user confirmation and manual fallback.
- Protected processes are rejected in both .NET and Python; no task changes a global endpoint or driver.

