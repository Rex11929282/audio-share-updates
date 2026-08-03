# FlowCast Lyrics Context Menu and Tuner Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Lyrics capsule open a two-item right-click menu and make the selected Liquid Glass tuner load reliably in the self-contained Windows build.

**Architecture:** The Lyrics WPF shell owns a native context menu and opens the existing tuner only from its adjustment command. Both WPF WebView2 controls use one explicit process-local `CoreWebView2Environment`, preventing the tuner from creating a separate default environment after the overlay starts.

**Tech Stack:** .NET 8 WPF, Microsoft.Web.WebView2, React/Vite, xUnit.

## Global Constraints

- The right-click menu has exactly `Adjust Liquid Glass…` and `Close FlowCast Lyrics`; right-click must not open the tuner directly.
- Keep left-button capsule dragging, the six real `liquid-glass-react` parameters, and existing Saved/Draft behavior intact.
- Do not modify `audio-share/src/AudioShare.App/MainWindow.xaml.cs`, audio routing, Radmin discovery, or lyric transport.
- Tuner failures may be logged locally but visible text must not disclose paths or stack traces.
- `LyricsOverlayPresenterTests.cs` already imports `AudioShare.Lyrics.Contracts` and compiles in both worktrees; preserve it and verify the solution rather than inventing a production change.

---

### Task 1: Replace direct right-click tuning with a native option menu

**Files:**

- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`

**Interfaces:**

- Consumes: existing `LiquidGlassTunerWindow` and `liquidGlassSettings`.
- Produces: `AdjustLiquidGlass_OnClick(object, RoutedEventArgs)` and `CloseFlowCastLyrics_OnClick(object, RoutedEventArgs)`.

- [ ] **Step 1: Write failing tests**

```csharp
Assert.Contains("x:Name=\"CapsuleContextMenu\"", xaml);
Assert.Contains("Header=\"Adjust Liquid Glass…\"", xaml);
Assert.Contains("Header=\"Close FlowCast Lyrics\"", xaml);
Assert.Contains("Click=\"AdjustLiquidGlass_OnClick\"", xaml);
Assert.Contains("Click=\"CloseFlowCastLyrics_OnClick\"", xaml);
Assert.Contains("CapsuleContextMenu.IsOpen = true;", source);
Assert.Contains("private void AdjustLiquidGlass_OnClick", source);
Assert.Contains("private void CloseFlowCastLyrics_OnClick", source);
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~FlowCastLyricsStartupTests.MainWindow_RightClick"
```

Expected: FAIL because right-click currently creates `LiquidGlassTunerWindow` directly.

- [ ] **Step 3: Implement the smallest menu**

```xml
<Border.ContextMenu>
    <ContextMenu x:Name="CapsuleContextMenu">
        <MenuItem Header="Adjust Liquid Glass…" Click="AdjustLiquidGlass_OnClick" />
        <Separator />
        <MenuItem Header="Close FlowCast Lyrics" Click="CloseFlowCastLyrics_OnClick" />
    </ContextMenu>
</Border.ContextMenu>
```

The preview right-button handler opens `CapsuleContextMenu` and marks the event handled. The adjustment handler owns the existing single-window create-or-activate logic. The close handler calls `Close()`.

- [ ] **Step 4: Verify GREEN**

Run the Step 2 command. Expected: PASS, including existing close-order and left-drag tests.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Lyrics/MainWindow.xaml audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
git commit -m "feat: add Lyrics capsule option menu"
```

### Task 2: Share WebView2 initialization and retain diagnostic evidence

**Files:**

- Create: `audio-share/src/AudioShare.Lyrics/LyricsWebViewEnvironment.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`

**Interfaces:**

- Produces: `LyricsWebViewEnvironment.GetAsync()` returning a process-local `Task<CoreWebView2Environment>`.
- Consumes: both WPF controls call `EnsureCoreWebView2Async(await LyricsWebViewEnvironment.GetAsync())`.

- [ ] **Step 1: Write failing tests**

```csharp
Assert.Contains("LyricsWebViewEnvironment.GetAsync()", mainWindowSource);
Assert.Contains("LyricsWebViewEnvironment.GetAsync()", tunerSource);
Assert.Contains("EnsureCoreWebView2Async(environment)", tunerSource);
Assert.Contains("Trace.TraceError", tunerSource);
Assert.DoesNotContain("catch (Exception)\n        {\n            TunerStatus.Text", tunerSource);
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests|FullyQualifiedName~FlowCastLyricsStartupTests"
```

Expected: FAIL because there is no shared environment and tuner initialization hides the failure boundary.

- [ ] **Step 3: Implement one environment**

Create a private-static-`Lazy<Task<CoreWebView2Environment>>` environment. Its user data folder is `%LocalAppData%\FlowCast Lyrics\WebView2`, created before calling `CoreWebView2Environment.CreateAsync(null, userDataFolder)`. Await and pass that one environment to both controls. In the tuner, record the short stage name before initialization, setup, mapping, and navigation; catch `Exception exception`, call `Trace.TraceError` with stage and exception, then retain the generic user-facing error text.

- [ ] **Step 4: Verify GREEN**

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests|FullyQualifiedName~FlowCastLyricsStartupTests"
dotnet build audio-share\src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --no-restore
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Lyrics/LyricsWebViewEnvironment.cs audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
git commit -m "fix: share Lyrics WebView environment"
```

### Task 3: Verify IslandMode, publish Debug, and exercise the desktop flow

**Files:**

- Verify only: `audio-share/tests/AudioShare.Core.Tests/LyricsOverlayPresenterTests.cs`
- Generated: `audio-share/dist/FlowCast-Lyrics-context-menu-debug/`

**Interfaces:**

- Consumes: Tasks 1 and 2.
- Produces: a self-contained Debug EXE for manual menu and tuner verification.

- [ ] **Step 1: Verify the IslandMode reference**

```powershell
rg -n "using AudioShare.Lyrics.Contracts;|IslandMode" audio-share\tests\AudioShare.Core.Tests\LyricsOverlayPresenterTests.cs
```

Expected: contracts import and IslandMode uses are present. If this passes, do not edit the test merely to create a change.

- [ ] **Step 2: Verify frontend and solution**

```powershell
Set-Location audio-share\src\AudioShare.Lyrics\Frontend
npm.cmd test
npm.cmd run build
Set-Location ..\..\..
dotnet test AudioShare.sln --no-restore
```

Expected: frontend tests pass; the solution has no IslandMode compiler errors.

- [ ] **Step 3: Publish Debug**

```powershell
dotnet publish src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --configuration Debug --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output dist\FlowCast-Lyrics-context-menu-debug
Test-Path "dist\FlowCast-Lyrics-context-menu-debug\FlowCast Lyrics.exe"
```

Expected: publish exits 0 and `Test-Path` returns `True`.

- [ ] **Step 4: Check the desktop behavior**

Launch the Debug EXE. Verify left drag, a two-item right-click menu, opening one tuner only through `Adjust Liquid Glass…`, closing through `Close FlowCast Lyrics`, and React controls instead of the generic load failure.

- [ ] **Step 5: Keep generated output uncommitted**

```powershell
git diff --check
git status --short
```

Expected: no generated `dist` or `work` file is staged. Do not create a release ZIP, updater manifest, or GitHub release.
