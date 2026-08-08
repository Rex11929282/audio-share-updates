# FlowCast Major Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild FlowCast's control flow and WPF experience around one verified share state machine so one selected application is shared, every other application stays local, and every stop or failure restores the prior Windows routes.

**Architecture:** Keep the existing .NET 8 WPF application, Windows routing executor, Voicemeeter Remote integration, and packaged `router-helper`. Move orchestration out of `MainWindow.xaml.cs` into a pure state machine, a Windows route runtime, an atomic recovery journal, and focused view models; every UI state is rendered from a verified route snapshot rather than checkbox state.

**Tech Stack:** C# 12, .NET 8 `net8.0-windows10.0.19041.0`, WPF, xUnit, Windows Core Audio, Voicemeeter Remote API, Python `router-helper`, NSIS, PowerShell update helper.

## Global Constraints

- Keep WPF and the verified route engine; do not migrate to WinUI or replace `router-helper`.
- Exactly one application may be selected and shared at a time.
- A silent application may still be selected and shared; audio level is informational only.
- Route the selected application to Voicemeeter Input and every other routable application to local-only AUX, including applications that appear during sharing.
- A selected application cannot be unchecked or have its route edited; switching requires confirmation and restores the old application first.
- No selected application means B1 health never blocks or warns and FlowCast exit is always allowed.
- Remove timed-stop UI; retain elapsed session duration, total duration, history, tray, and share mute.
- Do not add Lyrics, device management, shortcuts, social features, Discord detection, diagnostics UI, or volume controls.
- Internal Input/AUX endpoints stay hidden from user route selectors and use friendly labels in health details.
- All dialogs are FlowCast windows except Windows UAC.
- Do not publish GitHub releases; produce local verified code, Release output, and Setup EXE only.
- Preserve existing dirty worktree changes. Stage only files named by the current task; never stage `dist/` or `__pycache__/`.

---

### Task 1: Pure Share State Machine

**Files:**
- Create: `src/AudioShare.Core/FlowCastShareState.cs`
- Create: `src/AudioShare.Core/ShareStateMachine.cs`
- Create: `tests/AudioShare.Core.Tests/ShareStateMachineTests.cs`

**Interfaces:**
- Produces: `FlowCastShareState`, `ShareStateSnapshot`, and `ShareStateMachine`.
- `ShareStateMachine.BeginStart(AudioSession)` returns an operation ID used to reject stale completion callbacks.
- `ShareStateMachine.BeginStop()` returns the stop operation ID.

- [ ] **Step 1: Write failing transition and stale-operation tests**

```csharp
[Fact]
public void OnlyOneSelectionCanBeCommitted()
{
    var machine = new ShareStateMachine();
    var chrome = Session("chrome");
    var cloudMusic = Session("cloudmusic");

    var first = machine.BeginStart(chrome);
    Assert.True(machine.CompleteStart(first));

    Assert.Throws<InvalidOperationException>(() => machine.BeginStart(cloudMusic));
    Assert.Equal("chrome", machine.Snapshot.Selected!.ProcessName);
}

[Fact]
public void StaleStartCannotOverwriteNewerRestore()
{
    var machine = new ShareStateMachine();
    var start = machine.BeginStart(Session("chrome"));
    var stop = machine.BeginStop();

    Assert.False(machine.CompleteStart(start));
    Assert.True(machine.CompleteRestore(stop));
    Assert.Equal(FlowCastShareState.LocalOnly, machine.Snapshot.State);
}

private static AudioSession Session(string name) => new(1, 10, name, name, false);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~ShareStateMachineTests`

Expected: FAIL because `ShareStateMachine` and `FlowCastShareState` do not exist.

- [ ] **Step 3: Add the state types and guarded transitions**

```csharp
namespace AudioShare.Core;

public enum FlowCastShareState
{
    LocalOnly,
    Preparing,
    Sharing,
    Muted,
    Restoring,
    AttentionRequired,
}

public sealed record ShareStateSnapshot(
    FlowCastShareState State,
    AudioSession? Selected,
    long OperationId,
    string? AttentionMessage);

public sealed class ShareStateMachine
{
    private long operationId;
    public ShareStateSnapshot Snapshot { get; private set; } =
        new(FlowCastShareState.LocalOnly, null, 0, null);

    public long BeginStart(AudioSession selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (Snapshot.State != FlowCastShareState.LocalOnly)
            throw new InvalidOperationException("A share operation is already active.");
        Snapshot = new(FlowCastShareState.Preparing, selected, ++operationId, null);
        return operationId;
    }

    public long BeginStop()
    {
        Snapshot = Snapshot with { State = FlowCastShareState.Restoring, OperationId = ++operationId };
        return operationId;
    }

    public bool CompleteStart(long id) => Commit(id, FlowCastShareState.Sharing, Snapshot.Selected, null);
    public bool CompleteMute(long id, bool muted) => Commit(id, muted ? FlowCastShareState.Muted : FlowCastShareState.Sharing, Snapshot.Selected, null);
    public bool CompleteRestore(long id) => Commit(id, FlowCastShareState.LocalOnly, null, null);
    public bool RequireAttention(long id, string message) => Commit(id, FlowCastShareState.AttentionRequired, Snapshot.Selected, message);

    private bool Commit(long id, FlowCastShareState state, AudioSession? selected, string? message)
    {
        if (id != operationId) return false;
        Snapshot = new(state, selected, id, message);
        return true;
    }
}
```

- [ ] **Step 4: Run state tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~ShareStateMachineTests`

Expected: PASS.

- [ ] **Step 5: Commit only Task 1 files**

```powershell
git add src/AudioShare.Core/FlowCastShareState.cs src/AudioShare.Core/ShareStateMachine.cs tests/AudioShare.Core.Tests/ShareStateMachineTests.cs
git commit -m "feat: add FlowCast share state machine"
```

### Task 2: Verified Windows Route Runtime And App Isolation

**Files:**
- Create: `src/AudioShare.Core/IShareRouteRuntime.cs`
- Create: `src/AudioShare.Windows/FlowCastRouteRuntime.cs`
- Modify: `src/AudioShare.Core/IApplicationRouteExecutor.cs`
- Modify: `src/AudioShare.Windows/ApplicationRouteExecutor.cs`
- Modify: `src/AudioShare.Windows/VoicemeeterSharingBusService.cs`
- Create: `tests/AudioShare.Core.Tests/FlowCastRouteRuntimeTests.cs`
- Modify: `tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs`

**Interfaces:**
- Produces: `RouteTruthSnapshot`, `RouteMutationResult`, and `IShareRouteRuntime`.
- Consumes: `ApplicationRoutePlanner`, `IApplicationRouteExecutor`, `IExternalRoutingHelper`, and `VoicemeeterSharingBusService`.

- [ ] **Step 1: Add failing tests for selected Input, unselected AUX, and silent selection**

```csharp
[Fact]
public async Task BeginShareRoutesOnlySelectedIdentityToInput()
{
    var executor = new RecordingRouteExecutor();
    var runtime = Runtime(executor, new RecordingBus());
    var chrome = Session(1, "chrome", hasAudio: false);
    var game = Session(2, "game", hasAudio: true);

    var result = await runtime.BeginShareAsync(chrome, [chrome, game], CancellationToken.None);

    Assert.True(result.Succeeded);
    Assert.Equal("input-id", executor.TargetFor("chrome"));
    Assert.Equal("aux-id", executor.TargetFor("game"));
}

[Fact]
public async Task ReconcileRoutesNewApplicationToAuxDuringShare()
{
    var executor = new RecordingRouteExecutor();
    var runtime = Runtime(executor, new RecordingBus());
    var chrome = Session(1, "chrome", false);
    await runtime.BeginShareAsync(chrome, [chrome], CancellationToken.None);

    await runtime.ReconcileApplicationsAsync(chrome, [chrome, Session(3, "steam", true)], CancellationToken.None);

    Assert.Equal("aux-id", executor.TargetFor("steam"));
}
```

- [ ] **Step 2: Run route runtime tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~FlowCastRouteRuntimeTests`

Expected: FAIL because the runtime contract does not exist.

- [ ] **Step 3: Add the route runtime contract**

```csharp
namespace AudioShare.Core;

public sealed record RouteTruthSnapshot(
    bool RuntimeReady,
    bool SelectedProcessAlive,
    bool SelectedOnShareEndpoint,
    bool UnselectedApplicationsLocalOnly,
    bool MainInputShared,
    bool AuxShared,
    float InputLevel,
    float B1Level,
    string? TechnicalMessage)
{
    public bool IsSharing => RuntimeReady && SelectedProcessAlive && SelectedOnShareEndpoint &&
                             UnselectedApplicationsLocalOnly && MainInputShared && !AuxShared;
}

public sealed record RouteMutationResult(
    bool Succeeded,
    bool HasPendingRestore,
    IReadOnlyList<ApplicationRouteSnapshot> Snapshots,
    string? TechnicalMessage);

public sealed record FlowCastEndpointSet(string InputDeviceId, string AuxDeviceId);

public interface ISharingBusController
{
    Task SetSharedAsync(bool enabled, CancellationToken token);
    Task<SharingBusStatus> ReadAsync(CancellationToken token);
}

public interface IShareRouteRuntime
{
    Task<RouteMutationResult> BeginShareAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token);
    Task<RouteMutationResult> ReconcileApplicationsAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token);
    Task<RouteMutationResult> RestoreAsync(CancellationToken token);
    Task<RouteMutationResult> RestoreAsync(IReadOnlyList<ApplicationRouteSnapshot> snapshots, CancellationToken token);
    Task<RouteTruthSnapshot> ReadTruthAsync(AudioSession? selected, IReadOnlyList<AudioSession> active, CancellationToken token);
    Task SetSharingBusAsync(bool enabled, CancellationToken token);
}
```

- [ ] **Step 4: Expose restoration from persisted snapshots**

Add to `IApplicationRouteExecutor` and implement in `ApplicationRouteExecutor`:

```csharp
Task<ApplicationRouteExecutionResult> RestoreAsync(
    IReadOnlyList<ApplicationRouteSnapshot> snapshots,
    CancellationToken token);

public async Task<ApplicationRouteExecutionResult> RestoreAsync(
    IReadOnlyList<ApplicationRouteSnapshot> snapshots,
    CancellationToken token)
{
    ArgumentNullException.ThrowIfNull(snapshots);
    pendingTransaction = snapshots.ToArray();
    return await RestoreAsync(token);
}
```

- [ ] **Step 5: Implement `FlowCastRouteRuntime` with plan-and-verify behavior**

```csharp
public async Task<RouteMutationResult> BeginShareAsync(
    AudioSession selected,
    IReadOnlyList<AudioSession> active,
    CancellationToken token)
{
    var endpoints = await endpointProvider(token);
    var plan = ApplicationRoutePlanner.Create(active, [selected], endpoints.Input.Id, endpoints.AuxInput.Id);
    var applied = await executor.ApplyAsync(plan, token);
    if (!applied.Succeeded)
        return new(false, applied.HasPendingTransaction, applied.Snapshots, applied.Message);

    try
    {
        await sharingBus.SetSharedAsync(true, token);
        return new(true, true, applied.Snapshots, null);
    }
    catch (Exception exception)
    {
        await executor.RestoreAsync(CancellationToken.None);
        return new(false, false, applied.Snapshots, exception.Message);
    }
}
```

Implement `ReadTruthAsync` by reading the selected route, every unselected route, and `GetStatus()`; never infer sharing from selection state. `ReconcileApplicationsAsync` adds only newly observed application identities to AUX and extends the owned snapshot set.

- [ ] **Step 6: Make B1 operations asynchronous at the runtime boundary**

```csharp
public Task SetSharingBusAsync(bool enabled, CancellationToken token) =>
    sharingBus.SetSharedAsync(enabled, token);
```

Make `VoicemeeterSharingBusService` implement `ISharingBusController`; its methods wrap the existing synchronous Remote API calls with `Task.Run`. The test file defines a `RecordingBus : ISharingBusController` that stores every requested Boolean and returns a configurable `SharingBusStatus`.

- [ ] **Step 7: Run route executor and runtime tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~FlowCastRouteRuntimeTests|FullyQualifiedName~ApplicationRouteExecutorTests|FullyQualifiedName~ApplicationRoutePlannerTests"`

Expected: PASS, including the existing no-current-audio-session route test.

- [ ] **Step 8: Commit Task 2**

```powershell
git add src/AudioShare.Core/IShareRouteRuntime.cs src/AudioShare.Core/IApplicationRouteExecutor.cs src/AudioShare.Windows/FlowCastRouteRuntime.cs src/AudioShare.Windows/ApplicationRouteExecutor.cs src/AudioShare.Windows/VoicemeeterSharingBusService.cs tests/AudioShare.Core.Tests/FlowCastRouteRuntimeTests.cs tests/AudioShare.Core.Tests/ApplicationRouteExecutorTests.cs
git commit -m "feat: verify isolated Windows share routes"
```

### Task 3: Atomic Recovery Journal

**Files:**
- Create: `src/AudioShare.Core/ShareRecoveryRecord.cs`
- Create: `src/AudioShare.Core/IShareRecoveryJournal.cs`
- Create: `src/AudioShare.App/FlowCastRecoveryJournal.cs`
- Create: `tests/AudioShare.Core.Tests/FlowCastRecoveryJournalTests.cs`

**Interfaces:**
- Produces: `ShareRecoveryRecord`, `IShareRecoveryJournal`, and `FlowCastRecoveryJournal.ReadAsync/WriteAsync/ClearAsync`.
- Consumes: `ApplicationRouteSnapshot` from Task 2.

- [ ] **Step 1: Write failing persistence and corrupt-file tests**

```csharp
[Fact]
public async Task WriteUsesReplaceableJsonAndRoundTripsSnapshots()
{
    using var folder = new TemporaryFolder();
    var journal = new FlowCastRecoveryJournal(folder.Path);
    var record = new ShareRecoveryRecord("chrome", "Chrome", "input", [
        new ApplicationRouteSnapshot(10, 20, "chrome", new("speakers", "speakers"))],
        DateTimeOffset.Parse("2026-08-08T10:00:00Z"));

    await journal.WriteAsync(record, CancellationToken.None);

    var loaded = await journal.ReadAsync(CancellationToken.None);
    Assert.NotNull(loaded);
    Assert.Equal(record.ProcessName, loaded.ProcessName);
    Assert.Equal(record.Snapshots, loaded.Snapshots);
    Assert.False(File.Exists(Path.Combine(folder.Path, "share-recovery.tmp")));
}

[Fact]
public async Task CorruptJournalIsQuarantinedInsteadOfBlockingStartup()
{
    using var folder = new TemporaryFolder();
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "share-recovery.json"), "{");
    var journal = new FlowCastRecoveryJournal(folder.Path);

    Assert.Null(await journal.ReadAsync(CancellationToken.None));
    Assert.Single(Directory.GetFiles(folder.Path, "share-recovery.corrupt-*.json"));
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~FlowCastRecoveryJournalTests`

Expected: FAIL because journal types do not exist.

- [ ] **Step 3: Add the immutable recovery record**

```csharp
public sealed record ShareRecoveryRecord(
    string ProcessName,
    string DisplayName,
    string ShareEndpointId,
    IReadOnlyList<ApplicationRouteSnapshot> Snapshots,
    DateTimeOffset StartedAt);

public interface IShareRecoveryJournal
{
    Task<ShareRecoveryRecord?> ReadAsync(CancellationToken token);
    Task WriteAsync(ShareRecoveryRecord record, CancellationToken token);
    Task ClearAsync();
}
```

- [ ] **Step 4: Implement atomic JSON persistence under LocalAppData**

```csharp
public async Task WriteAsync(ShareRecoveryRecord record, CancellationToken token)
{
    Directory.CreateDirectory(root);
    var json = JsonSerializer.Serialize(record, JsonOptions);
    await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(false), token);
    File.Move(tempPath, journalPath, overwrite: true);
}

public Task ClearAsync()
{
    if (File.Exists(journalPath)) File.Delete(journalPath);
    return Task.CompletedTask;
}
```

`ReadAsync` catches `JsonException`, renames the bad file with a UTC timestamp, and returns `null` so startup continues.

```csharp
public async Task<ShareRecoveryRecord?> ReadAsync(CancellationToken token)
{
    if (!File.Exists(journalPath)) return null;
    try
    {
        var json = await File.ReadAllTextAsync(journalPath, token);
        return JsonSerializer.Deserialize<ShareRecoveryRecord>(json, JsonOptions);
    }
    catch (JsonException)
    {
        var quarantine = Path.Combine(root, $"share-recovery.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Move(journalPath, quarantine, overwrite: true);
        return null;
    }
}
```

Declare `FlowCastRecoveryJournal : IShareRecoveryJournal`. Add the test-only `TemporaryFolder` helper in the same test file; it creates a GUID folder under `Path.GetTempPath()` and recursively removes it from `Dispose`.

- [ ] **Step 5: Run journal tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~FlowCastRecoveryJournalTests`

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

```powershell
git add src/AudioShare.Core/ShareRecoveryRecord.cs src/AudioShare.Core/IShareRecoveryJournal.cs src/AudioShare.App/FlowCastRecoveryJournal.cs tests/AudioShare.Core.Tests/FlowCastRecoveryJournalTests.cs
git commit -m "feat: persist FlowCast route recovery journal"
```

### Task 4: Share Coordinator And Human Error Mapping

**Files:**
- Create: `src/AudioShare.Core/ShareCoordinator.cs`
- Create: `src/AudioShare.Core/ShareCommandResult.cs`
- Create: `src/AudioShare.App/FlowCastErrorPresenter.cs`
- Create: `tests/AudioShare.Core.Tests/ShareCoordinatorTests.cs`
- Create: `tests/AudioShare.Core.Tests/FlowCastErrorPresenterTests.cs`

**Interfaces:**
- Consumes: `ShareStateMachine`, `IShareRouteRuntime`, and `IShareRecoveryJournal`.
- Produces: serialized `StartAsync`, `SwitchAsync`, `StopAsync`, `SetMutedAsync`, and `ReconcileAsync` commands.

- [ ] **Step 1: Write failing coordinator tests for duplicate start, switch order, and nonblocking stop**

```csharp
[Fact]
public async Task DuplicateStartExecutesRuntimeOnce()
{
    var runtime = new FakeRuntime();
    var coordinator = Coordinator(runtime);
    var selected = Session("chrome");

    await Task.WhenAll(
        coordinator.StartAsync(selected, [selected], CancellationToken.None),
        coordinator.StartAsync(selected, [selected], CancellationToken.None));

    Assert.Equal(1, runtime.BeginCalls);
}

[Fact]
public async Task SwitchRestoresOldBeforeStartingNew()
{
    var runtime = new FakeRuntime();
    var coordinator = Coordinator(runtime);
    await coordinator.StartAsync(Session("chrome"), [Session("chrome")], CancellationToken.None);

    await coordinator.SwitchAsync(Session("cloudmusic"), [Session("cloudmusic")], CancellationToken.None);

    Assert.Equal(["begin:chrome", "bus:false", "restore", "begin:cloudmusic"], runtime.Calls);
}

[Fact]
public async Task ExitStopReturnsEvenWhenRestoreFails()
{
    var runtime = new FakeRuntime { RestoreFails = true };
    var coordinator = Coordinator(runtime);
    await coordinator.StartAsync(Session("chrome"), [Session("chrome")], CancellationToken.None);

    var result = await coordinator.StopAsync("FlowCast 已退出", TimeSpan.FromSeconds(2), CancellationToken.None);

    Assert.False(result.Succeeded);
    Assert.Equal(FlowCastShareState.LocalOnly, coordinator.Snapshot.State);
}
```

- [ ] **Step 2: Run coordinator tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~ShareCoordinatorTests|FullyQualifiedName~FlowCastErrorPresenterTests"`

Expected: FAIL because coordinator and presenter do not exist.

- [ ] **Step 3: Add command results and serialize operations with `SemaphoreSlim`**

```csharp
public sealed record ShareCommandResult(
    bool Succeeded,
    FlowCastShareState State,
    string? ErrorCode,
    string? TechnicalMessage);

public interface IShareCoordinator
{
    ShareStateSnapshot Snapshot { get; }
    Task<ShareCommandResult> StartAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token);
    Task<ShareCommandResult> SwitchAsync(AudioSession selected, IReadOnlyList<AudioSession> active, CancellationToken token);
    Task<ShareCommandResult> StopAsync(string reason, TimeSpan timeout, CancellationToken token);
    Task<ShareCommandResult> SetMutedAsync(bool muted, CancellationToken token);
}

public async Task<ShareCommandResult> StartAsync(
    AudioSession selected,
    IReadOnlyList<AudioSession> active,
    CancellationToken token)
{
    if (!await gate.WaitAsync(0, token))
        return new(false, Snapshot.State, "operation_busy", null);
    try
    {
        var operation = state.BeginStart(selected);
        var route = await runtime.BeginShareAsync(selected, active, token);
        if (!route.Succeeded)
        {
            await runtime.SetSharingBusAsync(false, CancellationToken.None);
            await runtime.RestoreAsync(CancellationToken.None);
            state.CompleteRestore(state.BeginStop());
            return new(false, state.Snapshot.State, "route_start_failed", route.TechnicalMessage);
        }
        await journal.WriteAsync(ToRecoveryRecord(selected, route.Snapshots), token);
        var truth = await runtime.ReadTruthAsync(selected, active, token);
        if (!truth.IsSharing) return await FailAndRestoreAsync("route_not_confirmed", truth.TechnicalMessage);
        state.CompleteStart(operation);
        return new(true, state.Snapshot.State, null, null);
    }
    finally { gate.Release(); }
}

private ShareRecoveryRecord ToRecoveryRecord(
    AudioSession selected,
    IReadOnlyList<ApplicationRouteSnapshot> snapshots) =>
    new(selected.ProcessName, selected.DisplayName, shareEndpointId, snapshots, clock.GetUtcNow());

private async Task<ShareCommandResult> FailAndRestoreAsync(string errorCode, string? technicalMessage)
{
    var restoreOperation = state.BeginStop();
    await runtime.SetSharingBusAsync(false, CancellationToken.None);
    var restored = await runtime.RestoreAsync(CancellationToken.None);
    if (restored.Succeeded) await journal.ClearAsync();
    state.CompleteRestore(restoreOperation);
    return new(false, state.Snapshot.State, errorCode, technicalMessage);
}
```

Implement `StopAsync` so B1 disable is attempted first, restore uses a linked timeout token, pending recovery remains in the journal, and the state becomes `LocalOnly` even when the bounded restore fails.

```csharp
public async Task<ShareCommandResult> StopAsync(
    string reason,
    TimeSpan timeout,
    CancellationToken token)
{
    await gate.WaitAsync(token);
    try
    {
        var operation = state.BeginStop();
        try { await runtime.SetSharingBusAsync(false, CancellationToken.None); }
        catch (Exception exception) { technicalLog.Write(exception); }
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(timeout);
        RouteMutationResult restored;
        try { restored = await runtime.RestoreAsync(bounded.Token); }
        catch (OperationCanceledException) { restored = new(false, true, [], "Restore timed out."); }
        if (restored.Succeeded) await journal.ClearAsync();
        state.CompleteRestore(operation);
        shareSession.Stop(clock.GetUtcNow(), reason, disconnected: !restored.Succeeded);
        return new(restored.Succeeded, state.Snapshot.State,
            restored.Succeeded ? null : "restore_pending", restored.TechnicalMessage);
    }
    finally { gate.Release(); }
}
```

- [ ] **Step 4: Add stable user-facing error mapping**

```csharp
public static FlowCastErrorMessage FromCode(string code) => code switch
{
    "operation_busy" => new("正在处理上一个操作", "当前设置没有改变。", "请稍等一秒再试。", null),
    "route_start_failed" => new("没有连接到分享路径", "朋友暂时听不到这个程序。", "重新连接", FlowCastRepairAction.ReconnectRoute),
    "route_not_confirmed" => new("Windows 还没有采用新路径", "分享没有开始，原播放路径正在恢复。", "重试", FlowCastRepairAction.ReconnectRoute),
    "banana_unavailable" => new("Voicemeeter 暂时没有回应", "分享已经停止。", "重新打开", FlowCastRepairAction.RestartBanana),
    "restore_pending" => new("原播放路径正在等待恢复", "朋友已经听不到该程序。", "刷新", FlowCastRepairAction.Refresh),
    _ => new("这次操作没有完成", "FlowCast 已保持在安全状态。", "重试", FlowCastRepairAction.Refresh),
};
```

- [ ] **Step 5: Run coordinator and presenter tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~ShareCoordinatorTests|FullyQualifiedName~FlowCastErrorPresenterTests"`

Expected: PASS.

- [ ] **Step 6: Commit Task 4**

```powershell
git add src/AudioShare.Core/ShareCoordinator.cs src/AudioShare.Core/ShareCommandResult.cs src/AudioShare.App/FlowCastErrorPresenter.cs tests/AudioShare.Core.Tests/ShareCoordinatorTests.cs tests/AudioShare.Core.Tests/FlowCastErrorPresenterTests.cs
git commit -m "feat: coordinate verified FlowCast sharing"
```

### Task 5: Startup, Shutdown, Recovery, And Single Instance

**Files:**
- Create: `src/AudioShare.App/FlowCastRuntimeHost.cs`
- Create: `src/AudioShare.App/FlowCastShutdownCoordinator.cs`
- Modify: `src/AudioShare.App/App.xaml.cs`
- Modify: `src/AudioShare.App/CloseFlowCastDialog.cs`
- Create: `tests/AudioShare.Core.Tests/FlowCastRuntimeHostTests.cs`
- Create: `tests/AudioShare.Core.Tests/FlowCastShutdownCoordinatorTests.cs`

**Interfaces:**
- Consumes: Task 4 coordinator, recovery journal, Banana window controller, and optional Voicemod launcher.
- Produces: one composition root used by `App` and `MainWindow`.

- [ ] **Step 1: Write failing recovery and bounded-exit tests**

```csharp
[Fact]
public async Task StartupDisablesBusBeforeRecoveringJournal()
{
    var host = HostWithPendingRecovery();

    await host.InitializeAsync(CancellationToken.None);

    Assert.Equal(["bus:false", "restore:snapshots", "journal:clear"], host.Calls);
}

[Fact]
public async Task ShutdownDoesNotWaitBeyondConfiguredTimeout()
{
    var coordinator = new HangingShareCoordinator();
    var shutdown = new FlowCastShutdownCoordinator(coordinator, TimeSpan.FromSeconds(2));
    var watch = Stopwatch.StartNew();

    await shutdown.ExitAsync();

    Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
}
```

- [ ] **Step 2: Run lifecycle tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~FlowCastRuntimeHostTests|FullyQualifiedName~FlowCastShutdownCoordinatorTests"`

Expected: FAIL because lifecycle classes do not exist.

- [ ] **Step 3: Build the runtime host composition root**

```csharp
public async Task InitializeAsync(CancellationToken token)
{
    bananaLauncher.TryStartAndMinimize();
    voicemodLauncher.TryStartIfInstalled();
    var recovery = await journal.ReadAsync(token);
    if (recovery is null) return;
    await runtime.SetSharingBusAsync(false, CancellationToken.None);
    var restored = await runtime.RestoreAsync(recovery.Snapshots, CancellationToken.None);
    if (restored.Succeeded) await journal.ClearAsync();
}
```

- [ ] **Step 4: Move single-instance activation behind an owned lifetime and show the main window only after startup**

```csharp
protected override async void OnStartup(StartupEventArgs e)
{
    if (!singleInstance.TryAcquire())
    {
        singleInstance.ActivateExisting();
        Shutdown();
        return;
    }
    base.OnStartup(e);
    var host = FlowCastRuntimeHost.CreateDefault();
    await host.InitializeAsync(CancellationToken.None);
    var window = new MainWindow(host);
    MainWindow = window;
    window.Show();
}
```

- [ ] **Step 5: Implement close choice and nonblocking exit**

```csharp
public async Task ExitAsync()
{
    using var timeout = new CancellationTokenSource(exitTimeout);
    try { await coordinator.StopAsync("FlowCast 已退出", exitTimeout, timeout.Token); }
    catch (Exception exception) { technicalLog.Write(exception); }
}
```

The close dialog returns `MinimizeToTray`, `Exit`, or `Cancel`; only `Exit` calls `ExitAsync`, and restore failure never cancels application shutdown.

- [ ] **Step 6: Run lifecycle and existing single-instance tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~FlowCastRuntimeHostTests|FullyQualifiedName~FlowCastShutdownCoordinatorTests|FullyQualifiedName~AudioOnlyPackagingTests"`

Expected: PASS.

- [ ] **Step 7: Commit Task 5**

```powershell
git add src/AudioShare.App/FlowCastRuntimeHost.cs src/AudioShare.App/FlowCastShutdownCoordinator.cs src/AudioShare.App/App.xaml.cs src/AudioShare.App/CloseFlowCastDialog.cs tests/AudioShare.Core.Tests/FlowCastRuntimeHostTests.cs tests/AudioShare.Core.Tests/FlowCastShutdownCoordinatorTests.cs
git commit -m "feat: recover routes across FlowCast lifetime"
```

### Task 6: Compact View Models And Main Window

**Files:**
- Create: `src/AudioShare.App/MainWindowViewModel.cs`
- Create: `src/AudioShare.App/AudioApplicationViewModel.cs`
- Create: `src/AudioShare.App/HealthSummaryViewModel.cs`
- Create: `src/AudioShare.App/RouteDisplayFormatter.cs`
- Create: `src/AudioShare.App/AsyncCommand.cs`
- Modify: `src/AudioShare.App/MainWindow.xaml`
- Modify: `src/AudioShare.App/MainWindow.xaml.cs`
- Delete: `src/AudioShare.Core/ShareStopSchedule.cs`
- Delete: `tests/AudioShare.Core.Tests/ShareStopScheduleTests.cs`
- Create: `tests/AudioShare.Core.Tests/MainWindowViewModelTests.cs`
- Create: `tests/AudioShare.Core.Tests/AudioApplicationViewModelTests.cs`

**Interfaces:**
- Consumes: `FlowCastRuntimeHost`, coordinator snapshots, health snapshots, preferences, and history.
- Produces: bindable state and commands; code-behind retains window chrome, drag, and lifecycle bridges only.

- [ ] **Step 1: Write failing UI-state tests**

```csharp
[Fact]
public async Task SelectingSilentApplicationRequestsConfirmationAndCanStart()
{
    var dialogs = new FakeDialogs(confirmShare: true);
    var vm = ViewModel(dialogs, Session("chrome", hasAudio: false));

    await vm.Applications[0].SelectCommand.ExecuteAsync(null);

    Assert.True(vm.Applications[0].IsSelected);
    Assert.False(vm.Applications[0].CanEditRoute);
    Assert.Equal("正在分享", vm.Applications[0].RouteSummary);
}

[Fact]
public async Task SelectingAnotherApplicationPromptsAndSwitches()
{
    var dialogs = new FakeDialogs(confirmShare: true, confirmSwitch: true);
    var vm = ViewModel(dialogs, Session("chrome", false), Session("cloudmusic", false));
    await vm.Applications[0].SelectCommand.ExecuteAsync(null);

    await vm.Applications[1].SelectCommand.ExecuteAsync(null);

    Assert.False(vm.Applications[0].IsSelected);
    Assert.True(vm.Applications[1].IsSelected);
    Assert.Equal(1, dialogs.SwitchConfirmations);
}
```

- [ ] **Step 2: Run view-model tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~AudioApplicationViewModelTests"`

Expected: FAIL because view models do not exist.

- [ ] **Step 3: Add view-model properties derived only from coordinator snapshots**

```csharp
public bool IsSelected => string.Equals(
    owner.ShareSnapshot.Selected?.ProcessName,
    Session.ProcessName,
    StringComparison.OrdinalIgnoreCase);

public bool CanEditRoute => !IsSelected && !owner.IsRouteOperationRunning && RouteOptions.Count > 1;
public string RouteSummary => IsSelected ? "正在分享" : RouteDisplayFormatter.Summarize(Session);
public bool CanSelect => !IsSelected && !owner.IsRouteOperationRunning;
```

The selection command calls a custom confirmation service, then `StartAsync` or `SwitchAsync`. It never changes `IsSelected` directly.

- [ ] **Step 4: Replace the fixed 1500-pixel layout with bounded compact XAML**

```xml
<Window Width="1180" Height="760"
        MinWidth="1080" MaxWidth="1320"
        MinHeight="700" MaxHeight="860"
        WindowStyle="None"
        ResizeMode="CanResizeWithGrip">
    <Grid Margin="24,52,24,22">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>
        <Border Grid.Row="1" Style="{StaticResource GlassCard}" Margin="0,0,0,16">
            <ContentPresenter Content="{Binding ShareStatus}"/>
        </Border>
        <ListBox Grid.Row="2"
                 ItemsSource="{Binding Applications}"
                 ScrollViewer.VerticalScrollBarVisibility="Auto"
                 FocusVisualStyle="{StaticResource FlowCastFocusStyle}"/>
    </Grid>
</Window>
```

Use one fixed-height row per application. Set `TextTrimming="CharacterEllipsis"`, fixed route-summary width, and a tooltip for the full device list. Remove `ApplyRoutingButton`, `ScheduleStopButton`, `TimerPanel`, and every timed-stop handler.

- [ ] **Step 5: Add a bright blue custom radio and a non-dotted focus style**

```xml
<Style x:Key="FlowCastFocusStyle" TargetType="Control">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate>
                <Border BorderBrush="#4C9DFF" BorderThickness="2" CornerRadius="14"/>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

The selected circle uses `#1687FF`; keyboard focus uses the same rounded outline and never the Windows dotted rectangle.

- [ ] **Step 6: Collapse health to one line and remove Discord checks**

Bind four friendly checks only: Banana, Windows default playback, local monitor channel, and share channel. B1 appears only when a program is selected or sharing.

- [ ] **Step 7: Run view-model and XAML build tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~AudioApplicationViewModelTests|FullyQualifiedName~HealthSummaryTests"`

Run: `dotnet build src/AudioShare.App/AudioShare.App.csproj -c Debug`

Expected: all tests PASS and build succeeds with 0 errors.

- [ ] **Step 8: Commit Task 6**

```powershell
git add src/AudioShare.App/MainWindowViewModel.cs src/AudioShare.App/AudioApplicationViewModel.cs src/AudioShare.App/HealthSummaryViewModel.cs src/AudioShare.App/RouteDisplayFormatter.cs src/AudioShare.App/AsyncCommand.cs src/AudioShare.App/MainWindow.xaml src/AudioShare.App/MainWindow.xaml.cs src/AudioShare.Core/ShareStopSchedule.cs tests/AudioShare.Core.Tests/ShareStopScheduleTests.cs tests/AudioShare.Core.Tests/MainWindowViewModelTests.cs tests/AudioShare.Core.Tests/AudioApplicationViewModelTests.cs
git commit -m "feat: rebuild compact FlowCast sharing UI"
```

### Task 7: Launch Motion, Custom Windows, History, And Low-Power Mode

**Files:**
- Create: `src/AudioShare.App/LaunchSequenceController.cs`
- Create: `src/AudioShare.App/ShareHistoryWindow.xaml`
- Create: `src/AudioShare.App/ShareHistoryWindow.xaml.cs`
- Modify: `src/AudioShare.App/StartupChime.cs`
- Modify: `src/AudioShare.App/MotionController.cs`
- Modify: `src/AudioShare.App/FlowCastDialogWindow.cs`
- Modify: `src/AudioShare.App/FlowCastMessageDialog.cs`
- Modify: `src/AudioShare.App/ShareHistoryStore.cs`
- Create: `tests/AudioShare.Core.Tests/LaunchSequenceControllerTests.cs`
- Modify: `tests/AudioShare.Core.Tests/ShareSessionTests.cs`

**Interfaces:**
- Consumes: main-window visibility, local playback endpoint, coordinator events, and history store.
- Produces: deterministic 3-second launch phases and an event-driven reduced-motion mode.

- [ ] **Step 1: Write failing launch timeline and local-chime tests**

```csharp
[Fact]
public void LaunchTimelineEndsClearAtThreeSeconds()
{
    var timeline = LaunchSequenceController.CreateTimeline();

    Assert.Equal(TimeSpan.FromSeconds(3), timeline.Duration);
    Assert.Equal(0, timeline.FinalBlurRadius);
    Assert.Equal(1, timeline.FinalLogoOpacity);
    Assert.False(timeline.ShowsMainWindowBeforeCompletion);
}

[Fact]
public void ChimeSkipsWhenLocalEndpointResolvesToShareEndpoint()
{
    Assert.False(StartupChime.ShouldPlay("input-id", "input-id", "aux-id"));
    Assert.True(StartupChime.ShouldPlay("speakers-id", "input-id", "aux-id"));
}
```

- [ ] **Step 2: Run motion tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~LaunchSequenceControllerTests`

Expected: FAIL because deterministic timeline APIs do not exist.

- [ ] **Step 3: Implement the three-phase launch sequence**

```csharp
public static LaunchTimeline CreateTimeline() => new(
    TimeSpan.FromSeconds(3),
    [
        new LaunchPhase(TimeSpan.Zero, TimeSpan.FromMilliseconds(850), LaunchVisual.WaveBars),
        new LaunchPhase(TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(950), LaunchVisual.NoteAndSignal),
        new LaunchPhase(TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(1500), LaunchVisual.BrandResolve),
    ],
    FinalBlurRadius: 0,
    FinalLogoOpacity: 1,
    ShowsMainWindowBeforeCompletion: false);

public enum LaunchVisual { WaveBars, NoteAndSignal, BrandResolve }
public sealed record LaunchPhase(TimeSpan Start, TimeSpan Duration, LaunchVisual Visual);
public sealed record LaunchTimeline(
    TimeSpan Duration,
    IReadOnlyList<LaunchPhase> Phases,
    double FinalBlurRadius,
    double FinalLogoOpacity,
    bool ShowsMainWindowBeforeCompletion);
```

Animate vector paths and bars, not the white-background PNG. Play one local chime only after `ShouldPlay` confirms the endpoint is neither Input nor AUX.

- [ ] **Step 4: Replace the message-based history popup with a scrollable custom window**

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="520">
    <ItemsControl ItemsSource="{Binding Records}">
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Grid Margin="0,0,0,10">
                    <TextBlock Text="{Binding ApplicationName}" FontWeight="SemiBold"/>
                    <TextBlock Margin="180,0,0,0" Text="{Binding DurationText}"/>
                </Grid>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</ScrollViewer>
```

- [ ] **Step 5: Stop nonessential motion when minimized**

```csharp
public void SetPowerMode(bool minimized)
{
    atmosphereStoryboard?.SetIsPaused(minimized);
    if (minimized) decorativeRefreshTimer.Stop();
    else if (window.IsVisible) decorativeRefreshTimer.Start();
}
```

The route safety monitor remains active at a low frequency and does not depend on the decorative timer.

- [ ] **Step 6: Run motion/history tests and build the app**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~LaunchSequenceControllerTests|FullyQualifiedName~ShareSessionTests"`

Run: `dotnet build src/AudioShare.App/AudioShare.App.csproj -c Debug`

Expected: PASS and 0 build errors.

- [ ] **Step 7: Commit Task 7**

```powershell
git add src/AudioShare.App/LaunchSequenceController.cs src/AudioShare.App/ShareHistoryWindow.xaml src/AudioShare.App/ShareHistoryWindow.xaml.cs src/AudioShare.App/StartupChime.cs src/AudioShare.App/MotionController.cs src/AudioShare.App/FlowCastDialogWindow.cs src/AudioShare.App/FlowCastMessageDialog.cs src/AudioShare.App/ShareHistoryStore.cs tests/AudioShare.Core.Tests/LaunchSequenceControllerTests.cs tests/AudioShare.Core.Tests/ShareSessionTests.cs
git commit -m "feat: polish FlowCast motion and history"
```

### Task 8: Transactional Updater With Health Confirmation

**Files:**
- Create: `src/AudioShare.App/UpdateHealthMarker.cs`
- Modify: `src/AudioShare.App/UpdateService.cs`
- Modify: `src/AudioShare.App/UpdateProgress.cs`
- Modify: `src/AudioShare.App/UpdateProgressWindow.cs`
- Modify: `src/AudioShare.App/App.xaml.cs`
- Modify: `tests/AudioShare.Core.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Consumes: existing release parser, Setup asset, SHA-256 validation, and update progress window.
- Produces: staged transaction ID, new-version health marker, and rollback signal.

- [ ] **Step 1: Add failing tests for changed-version health and rollback**

```csharp
[Fact]
public void ReplacementRequiresExpectedVersionHealthMarker()
{
    var script = GetReplacementScript();

    Assert.Contains("$ExpectedVersion", script);
    Assert.Contains("flowcast-update-health.json", script);
    Assert.Contains("Restore-FlowCastBackup", script);
    Assert.Contains("--update-transaction", script);
}

[Fact]
public void UpdateButtonIsHiddenForCurrentVersion()
{
    Assert.Null(ReleaseUpdateParser.TryParseNewerRelease(ReleaseJson("1.1.0"), new Version(1, 1, 0)));
}
```

- [ ] **Step 2: Run update tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~UpdateServiceTests`

Expected: FAIL because the replacement script lacks health confirmation.

- [ ] **Step 3: Write the health marker from the newly started process**

```csharp
public static async Task WriteAsync(string transactionId, Version version, CancellationToken token)
{
    var marker = new UpdateHealthRecord(transactionId, version.ToString(), DateTimeOffset.UtcNow);
    var path = Path.Combine(Path.GetTempPath(), "FlowCast", transactionId, "flowcast-update-health.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(marker), token);
}
```

`App.OnStartup` writes the marker only after the runtime host has initialized and the main window can be created.

- [ ] **Step 4: Harden the replacement helper**

Pass transaction ID and expected version to the external helper. After Setup exits, start the installed `AudioShare.App.exe --update-transaction <id>`, wait up to 30 seconds for a marker matching the expected version, and call `Restore-FlowCastBackup` before starting the old version when validation fails.

```powershell
$Health = Get-Content -LiteralPath $HealthPath -Raw | ConvertFrom-Json
if ($Health.transactionId -ne $TransactionId -or $Health.version -ne $ExpectedVersion) {
    throw "FlowCast health marker did not confirm the expected version."
}
```

- [ ] **Step 5: Keep progress determinate and prevent duplicate update actions**

Extend `UpdateStage` with `Replacing` and `ConfirmingRestart`; bind the primary action's `IsEnabled` to `!IsUpdating` and show the update entry only when `CheckForUpdateAsync` returns a higher version.

- [ ] **Step 6: Run all updater tests**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter FullyQualifiedName~UpdateServiceTests`

Expected: PASS, including existing checksum, Setup staging, full-directory replacement, failure signal, and new health rollback tests.

- [ ] **Step 7: Commit Task 8**

```powershell
git add src/AudioShare.App/UpdateHealthMarker.cs src/AudioShare.App/UpdateService.cs src/AudioShare.App/UpdateProgress.cs src/AudioShare.App/UpdateProgressWindow.cs src/AudioShare.App/App.xaml.cs tests/AudioShare.Core.Tests/UpdateServiceTests.cs
git commit -m "feat: verify and roll back FlowCast updates"
```

### Task 9: Tutorial, Installer, Package, And End-To-End Verification

**Files:**
- Modify: `src/AudioShare.App/TutorialContent.cs`
- Modify: `src/AudioShare.App/TutorialWindow.cs`
- Modify: `README.md`
- Modify: `installer/FlowCast.nsi`
- Modify: `scripts/publish-release.ps1`
- Modify: `tests/AudioShare.Core.Tests/TutorialContentTests.cs`
- Modify: `tests/AudioShare.Core.Tests/AudioOnlyPackagingTests.cs`
- Create: `docs/verification/2026-08-08-flowcast-major-redesign.md`

**Interfaces:**
- Consumes: final app output, `router-helper` manifest, and NSIS installer.
- Produces: one Setup EXE and a verification record; no GitHub publication.

- [ ] **Step 1: Write failing tutorial and package-boundary tests**

```csharp
[Fact]
public void TutorialExplainsSelectConfirmStopAndRestore()
{
    var text = TutorialContent.AllText;
    Assert.Contains("选择一个程序", text);
    Assert.Contains("确认分享", text);
    Assert.Contains("其他程序只在本机播放", text);
    Assert.Contains("停止后恢复原来的播放设备", text);
    Assert.DoesNotContain("开始分享按钮", text);
    Assert.DoesNotContain("定时", text);
}

[Fact]
public void PackageContainsAudioOnlyRuntimeAndNoLyrics()
{
    var files = PackageFiles();
    Assert.Contains("AudioShare.App.exe", files);
    Assert.Contains("router-helper/router-helper-manifest.json", files);
    Assert.DoesNotContain(files, path => path.Contains("Lyrics", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run tutorial and packaging tests and verify RED**

Run: `dotnet test tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~TutorialContentTests|FullyQualifiedName~AudioOnlyPackagingTests"`

Expected: FAIL until copy and package checks match the redesigned flow.

- [ ] **Step 3: Rewrite the three-step tutorial**

Use these exact headings and outcomes:

```text
1. 选择程序：即使现在没有声音，也可以先选择。
2. 确认分享：只有选中的程序会送给朋友，其他程序继续只在本机播放。
3. 停止分享：FlowCast 会关闭朋友端声音，并恢复开始前的播放设备。
```

Add a short “需要处理” section for Banana not running, path not confirmed, and original device missing. Do not mention Discord detection, diagnostics, Lyrics, timers, or volume controls.

- [ ] **Step 4: Keep dependencies installed but hidden**

In NSIS, preserve the application, uninstaller, runtime notices, and `router-helper`; set support files/directories hidden after copying rather than deleting them:

```nsis
SetFileAttributes "$INSTDIR\router-helper" HIDDEN
SetFileAttributes "$INSTDIR\ThirdPartyNotices.txt" HIDDEN
SetFileAttributes "$INSTDIR\DotNetRuntimeLicense.txt" HIDDEN
```

The downloadable artifact remains one `FlowCast-Setup.exe`.

- [ ] **Step 5: Run source, helper, and full solution tests**

Run: `python -m pytest router-helper/tests -q`

Expected: all router-helper tests PASS.

Run: `dotnet test AudioShare.sln -c Release --no-restore`

Expected: all .NET tests PASS with 0 failed.

Run: `dotnet build AudioShare.sln -c Release --no-restore`

Expected: build succeeds with 0 errors.

- [ ] **Step 6: Publish and exercise the packaged helper protocol**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-release.ps1 -SkipGitHubPublish`

Expected: Release app directory and one Setup EXE are produced locally.

Start the packaged helper as a subprocess, send a BOM-prefixed health request, and assert a JSON response containing `"ok":true` and a version. Record the exact command and output in the verification document.

- [ ] **Step 7: Perform installer and UI smoke tests**

Verify in order:

1. Silent install exits successfully.
2. `AudioShare.App.exe` is visible; support dependencies exist and are hidden.
3. A second launch activates the first instance.
4. Main window remains within 1080-1320 width and the app list scrolls.
5. A long multi-device route renders as “多个播放设备”.
6. Launch lasts 3 seconds, never flashes the main window, and ends with a clear vector logo.
7. Minimize stops decorative animation while route monitoring continues.

- [ ] **Step 8: Perform real-machine Discord acceptance and record only observed results**

Test Chrome and NetEase separately: start local playback, confirm share, verify the friend hears only the selected application, stop, verify the friend no longer hears it, and confirm the application's original output route returns. Then verify real microphone plus music and no remote echo. Mark each result `PASS`, `FAIL`, or `NOT TESTED`; never infer Discord success from automated tests.

- [ ] **Step 9: Commit Task 9 without generated binaries**

```powershell
git add src/AudioShare.App/TutorialContent.cs src/AudioShare.App/TutorialWindow.cs README.md installer/FlowCast.nsi scripts/publish-release.ps1 tests/AudioShare.Core.Tests/TutorialContentTests.cs tests/AudioShare.Core.Tests/AudioOnlyPackagingTests.cs docs/verification/2026-08-08-flowcast-major-redesign.md
git commit -m "docs: verify FlowCast major redesign"
```

Do not stage `dist/`, `release/`, Setup EXE files, or Python caches.
