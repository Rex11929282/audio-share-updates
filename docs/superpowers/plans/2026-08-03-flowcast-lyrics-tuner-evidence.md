# FlowCast Lyrics Tuner Root-Cause Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record the exact local WebView2 exception behind the existing generic Liquid Glass tuner failure, without changing the capsule workflow or exposing technical details in the UI.

**Architecture:** The existing `LiquidGlassTunerWindow` catch boundary already records a short stage name. Add one small local-file logger that appends the stage, exception type, and exception message under `%LocalAppData%\\FlowCast Lyrics`; call it from that boundary before the existing generic status is shown. The logger has a path-injected constructor so an xUnit test can verify real file output without writing into a user's profile.

**Tech Stack:** .NET 8 WPF, Microsoft.Web.WebView2, xUnit.

## Global Constraints

- Do not modify `audio-share/src/AudioShare.App/MainWindow.xaml.cs`, audio routing, Radmin discovery, lyric transport, the capsule interaction, or the six `liquid-glass-react` parameters.
- The user-facing text remains exactly `Unable to load the Liquid Glass tuner.`; it must not disclose paths, exception text, or stack traces.
- The diagnostic record is local only at `%LocalAppData%\\FlowCast Lyrics\\diagnostics.log`; do not add a settings page, dialog, menu item, telemetry, or network upload.
- A failed diagnostic write must not replace or prevent the existing generic failure state.
- `LyricsOverlayPresenterTests.cs` already has its `IslandMode` import and full-solution verification must continue to pass; do not manufacture an unrelated change.

---

### Task 1: Persist the caught tuner initialization evidence

**Files:**

- Create: `audio-share/src/AudioShare.Lyrics/LyricsDiagnosticLog.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LyricsDiagnosticLogTests.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs`

**Interfaces:**

- Produces: `internal sealed class LyricsDiagnosticLog` with `CreateDefault()` and `WriteTunerInitializationFailure(string stage, Exception exception)`.
- Consumes: `LiquidGlassTunerWindow.OnLoaded` supplies its current `stage` and caught `Exception`.

- [ ] **Step 1: Write the failing logger-output test**

Create `LyricsDiagnosticLogTests.cs` with this focused test:

```csharp
[Fact]
public void WriteTunerInitializationFailure_AppendsStageAndExceptionToTheConfiguredFile()
{
    var path = Path.Combine(directory, "diagnostics.log");
    var log = new LyricsDiagnosticLog(path);

    log.WriteTunerInitializationFailure("mapping", new InvalidOperationException("expected test failure"));

    var text = File.ReadAllText(path);
    Assert.Contains("tuner-webview", text);
    Assert.Contains("stage=mapping", text);
    Assert.Contains("System.InvalidOperationException", text);
    Assert.Contains("expected test failure", text);
}
```

Extend `LiquidGlassTunerWindowTests.TunerWindow_UsesTheSharedWebViewEnvironmentAndTracesInitializationFailures` with:

```csharp
Assert.Contains("diagnosticLog.WriteTunerInitializationFailure(stage, exception);", source);
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsDiagnosticLogTests|FullyQualifiedName~LiquidGlassTunerWindowTests.TunerWindow_UsesTheSharedWebViewEnvironmentAndTracesInitializationFailures"
```

Expected: FAIL because `LyricsDiagnosticLog` and the tuner call do not yet exist.

- [ ] **Step 3: Implement the minimal local logger and call site**

Create `LyricsDiagnosticLog.cs` with a path constructor and this behavior:

```csharp
internal sealed class LyricsDiagnosticLog
{
    private readonly string path;

    internal LyricsDiagnosticLog(string path) => this.path = path;

    internal static LyricsDiagnosticLog CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics");
        return new LyricsDiagnosticLog(Path.Combine(directory, "diagnostics.log"));
    }

    internal void WriteTunerInitializationFailure(string stage, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"{DateTimeOffset.UtcNow:O} tuner-webview stage={stage} {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

Add one field to `LiquidGlassTunerWindow`:

```csharp
private static readonly LyricsDiagnosticLog diagnosticLog = LyricsDiagnosticLog.CreateDefault();
```

In the existing `catch (Exception exception)` in `OnLoaded`, place this line directly before `Trace.TraceError`:

```csharp
diagnosticLog.WriteTunerInitializationFailure(stage, exception);
```

Do not change the generic `TunerStatus` text or add any other catch clauses.

- [ ] **Step 4: Verify GREEN**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsDiagnosticLogTests|FullyQualifiedName~LiquidGlassTunerWindowTests"
dotnet build audio-share\src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --no-restore
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Lyrics/LyricsDiagnosticLog.cs audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs audio-share/tests/AudioShare.Core.Tests/LyricsDiagnosticLogTests.cs audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs
git commit -m "fix: retain Lyrics tuner initialization evidence"
```

### Task 2: Reproduce once and classify the failing boundary

**Files:**

- Generated only: `audio-share/dist/FlowCast-Lyrics-tuner-evidence-debug/`
- Generated at runtime only: `%LocalAppData%\\FlowCast Lyrics\\diagnostics.log`

**Interfaces:**

- Consumes: Task 1 logger and the current option-menu route.
- Produces: one recorded `tuner-webview` line with a stage and exception that identifies the next repair's target boundary.

- [ ] **Step 1: Publish the evidence build**

Run:

```powershell
Set-Location audio-share
dotnet publish src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --configuration Debug --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output dist\FlowCast-Lyrics-tuner-evidence-debug
Test-Path "dist\FlowCast-Lyrics-tuner-evidence-debug\FlowCast Lyrics.exe"
```

Expected: publish exits 0 and `Test-Path` returns `True`.

- [ ] **Step 2: Start from a clean diagnostic record and reproduce the existing failure once**

Run:

```powershell
$log = Join-Path $env:LOCALAPPDATA 'FlowCast Lyrics\diagnostics.log'
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
Start-Process -FilePath '.\dist\FlowCast-Lyrics-tuner-evidence-debug\FlowCast Lyrics.exe'
```

Open `Adjust Liquid Glass…` once through the capsule's right-click option menu. Do not alter any Liquid Glass setting.

- [ ] **Step 3: Read the evidence and stop before a speculative repair**

Run:

```powershell
Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'FlowCast Lyrics\diagnostics.log') -Tail 1
```

Expected: one line beginning with an ISO timestamp and `tuner-webview stage=`. The next repair plan must target that exact stage and exception, rather than assuming a WebView2 environment collision.

- [ ] **Step 4: Keep artifacts uncommitted**

Run:

```powershell
git diff --check
git status --short
```

Expected: no `dist`, runtime log, `work`, or `.superpowers` artifact is staged.
