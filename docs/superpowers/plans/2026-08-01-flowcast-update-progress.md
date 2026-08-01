# FlowCast Update Progress Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show update download progress and automatically relaunch FlowCast after a verified package is installed.

**Architecture:** `UpdateService` streams the ZIP to disk and emits typed progress updates. A small owned WPF progress window renders those updates. `App` starts the existing replacement helper after validation, then closes FlowCast so the helper replaces and relaunches it.

**Tech Stack:** .NET 8, WPF, `HttpClient`, `System.IO.Compression`, xUnit.

## Global Constraints

- Preserve SHA-256 validation and the existing all-package replacement transaction.
- Do not modify audio routing during the update flow.
- A failed update leaves the current version runnable.

---

### Task 1: Stream Update Progress

**Files:**
- Create: `audio-share/src/AudioShare.App/UpdateProgress.cs`
- Modify: `audio-share/src/AudioShare.App/UpdateService.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Produces `UpdateProgress(UpdateStage Stage, string Message, int? Percentage)`.
- Adds `DownloadAndStageAsync(ReleaseUpdate update, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken = default)` while retaining the current overload.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task DownloadAndStageAsync_ReportsDownloadAndValidationProgress()
{
    var updates = new List<UpdateProgress>();
    await service.DownloadAndStageAsync(update, new Progress<UpdateProgress>(updates.Add));
    Assert.Contains(updates, item => item.Stage == UpdateStage.Downloading && item.Percentage == 100);
    Assert.Contains(updates, item => item.Stage == UpdateStage.Verifying);
    Assert.Contains(updates, item => item.Stage == UpdateStage.ReadyToRestart);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test audio-share/AudioShare.sln --no-restore --filter FullyQualifiedName~DownloadAndStageAsync_ReportsDownloadAndValidationProgress`

Expected: FAIL because the progress type and overload do not exist.

- [ ] **Step 3: Implement the minimal streaming download**

```csharp
using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
await using var destination = File.Create(path);
// Copy fixed-size buffers and report 0-100 when Content-Length is available.
```

Emit `Downloading`, `Verifying`, `Extracting`, and `ReadyToRestart` around the existing validation and extraction stages.

- [ ] **Step 4: Verify GREEN and commit**

Run: `dotnet test audio-share/AudioShare.sln --no-restore --filter FullyQualifiedName~DownloadAndStageAsync_ReportsDownloadAndValidationProgress`

Expected: PASS.

```powershell
git add audio-share/src/AudioShare.App/UpdateProgress.cs audio-share/src/AudioShare.App/UpdateService.cs audio-share/tests/AudioShare.Core.Tests/UpdateServiceTests.cs
git commit -m "feat: report FlowCast update progress"
```

### Task 2: Render Progress And Auto-Restart

**Files:**
- Create: `audio-share/src/AudioShare.App/UpdateProgressWindow.cs`
- Modify: `audio-share/src/AudioShare.App/UpdateAvailableDialog.cs`
- Modify: `audio-share/src/AudioShare.App/App.xaml.cs`
- Test: `audio-share/tests/AudioShare.Core.Tests/UpdateServiceTests.cs`

**Interfaces:**
- Consumes `UpdateProgress` and `IProgress<UpdateProgress>` from Task 1.
- Produces `UpdateProgressWindow.Update(UpdateProgress progress)`.
- Uses existing `UpdateService.BeginStagedReplacementAndRestart(StagedUpdate update)` unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void UpdateProgress_UsesIndeterminateProgressWhenByteCountIsUnavailable()
{
    Assert.Null(new UpdateProgress(UpdateStage.Downloading, "正在下载更新", null).Percentage);
}

[Fact]
public void ReplacementScript_AutomaticallyStartsTheUpdatedExecutable()
{
    Assert.Contains("Start-Process -FilePath $TargetPath", GetReplacementScript());
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test audio-share/AudioShare.sln --no-restore --filter "FullyQualifiedName~UpdateProgress_UsesIndeterminateProgressWhenByteCountIsUnavailable|FullyQualifiedName~ReplacementScript_AutomaticallyStartsTheUpdatedExecutable"`

Expected: the new progress test FAILS before Task 1 is implemented; the restart-contract test PASSES to protect the existing helper behavior.

- [ ] **Step 3: Implement the update window and application flow**

```csharp
progressBar.IsIndeterminate = progress.Percentage is null;
progressBar.Value = progress.Percentage ?? 0;
messageText.Text = progress.Message;
```

After user confirmation, show an owned non-resizable progress window, pass `new Progress<UpdateProgress>(progressWindow.Update)` to the service, set the final message to `正在重新启动 FlowCast`, start the existing replacement helper, and immediately call `owner.Close()`. Do not show a success message box. Close the progress window only on failure.

- [ ] **Step 4: Verify GREEN and commit**

Run: `dotnet test audio-share/AudioShare.sln --no-restore && dotnet build audio-share/AudioShare.sln --no-restore -c Release && git diff --check`

Expected: all tests pass, the release build has zero warnings and errors, and the diff has no whitespace errors.

```powershell
git add audio-share/src/AudioShare.App/UpdateProgressWindow.cs audio-share/src/AudioShare.App/UpdateAvailableDialog.cs audio-share/src/AudioShare.App/App.xaml.cs audio-share/tests/AudioShare.Core.Tests/UpdateServiceTests.cs
git commit -m "feat: show update progress and restart FlowCast"
```
