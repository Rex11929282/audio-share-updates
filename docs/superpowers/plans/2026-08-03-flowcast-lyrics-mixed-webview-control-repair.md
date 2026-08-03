# FlowCast Lyrics Mixed WebView Control Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Allow the Liquid Glass tuner to initialize in the same Lyrics process as the capsule by using the same WPF WebView2 hosting backend as the working overlay.

**Architecture:** Both the overlay and tuner continue to use the existing single LyricsWebViewEnvironment; the tuner changes only from the standard WebView2 XAML element to WebView2CompositionControl. Because both controls implement IWebView2, existing code-behind initialization, message handling, and disposal stay unchanged.

**Tech Stack:** .NET 8 WPF, Microsoft.Web.WebView2, React/Vite, xUnit.

## Global Constraints

- Change only the tuner WebView XAML control type and its targeted regression test; do not alter its size, window style, right-click option menu, user-facing failure copy, six Liquid Glass parameters, or Saved/Draft behavior.
- Keep LyricsWebViewEnvironment and its %LocalAppData%/FlowCast Lyrics/WebView2 location unchanged.
- Do not modify audio-share/src/AudioShare.App/MainWindow.xaml.cs, audio routing, Radmin discovery, lyric transport, or the normal overlay markup.
- Do not add retries, a new user-data folder, a fallback browser, telemetry, configuration, or UI actions.
- work/, audio-share/dist/, runtime diagnostics, and .superpowers files are generated artifacts and must remain uncommitted.

---

### Task 1: Make the tuner use the overlay's composition hosting control

**Files:**

- Modify: audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml
- Modify: audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs

**Interfaces:**

- Consumes: the existing Microsoft.Web.WebView2.Wpf.IWebView2 API used by LiquidGlassTunerWindow.xaml.cs.
- Produces: TunerWebView as WebView2CompositionControl, matching OverlayWebView.

- [ ] **Step 1: Write the failing regression test**

Add this focused test to LiquidGlassTunerWindowTests:

~~~csharp
[Fact]
public void TunerWindow_UsesTheSameCompositionControlAsTheOverlay()
{
    var xaml = File.ReadAllText(
        FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml"));

    Assert.Contains("<wv2:WebView2CompositionControl x:Name=\"TunerWebView\"", xaml);
    Assert.DoesNotContain("<wv2:WebView2 x:Name=\"TunerWebView\"", xaml);
}
~~~

- [ ] **Step 2: Verify RED**

Run:

~~~powershell
dotnet test audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests.TunerWindow_UsesTheSameCompositionControlAsTheOverlay"
~~~

Expected: FAIL because the current XAML contains the standard TunerWebView element.

- [ ] **Step 3: Make the minimal XAML substitution**

Replace only:

~~~xml
<wv2:WebView2 x:Name="TunerWebView"
              Visibility="Hidden" />
~~~

with:

~~~xml
<wv2:WebView2CompositionControl x:Name="TunerWebView"
                                Visibility="Hidden" />
~~~

Do not change the containing Grid, TunerStatus, or any code-behind.

- [ ] **Step 4: Verify GREEN**

Run:

~~~powershell
dotnet test audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests"
dotnet build audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj --no-restore
~~~

Expected: both commands exit 0.

- [ ] **Step 5: Commit**

~~~powershell
git add audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs
git commit -m "fix: align Lyrics tuner WebView hosting"
~~~

### Task 2: Reproduce the controller initialization with the local probe

**Files:**

- Verify only: work/tuner-probe/
- Generated only: audio-share/dist/FlowCast-Lyrics-tuner-evidence-debug/
- Generated at runtime only: %LocalAppData%/FlowCast Lyrics/diagnostics.log

**Interfaces:**

- Consumes: Task 1, which makes the live tuner use the composition controller.
- Produces: live evidence that the tuner does not append a new tuner-webview failure record and presents its React page.

- [ ] **Step 1: Build the updated probe and capture the previous record length**

Run:

~~~powershell
Set-Location work/tuner-probe
dotnet build TunerProbe.csproj --no-restore
$log = Join-Path $env:LOCALAPPDATA 'FlowCast Lyrics/diagnostics.log'
$beforeLength = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
~~~

Expected: build exits 0 and $beforeLength records the known pre-fix evidence without deleting it.

- [ ] **Step 2: Run the probe, then inspect the normal tuner window**

Run the built TunerProbe.exe, wait up to 10 seconds, then inspect the window titled FlowCast Lyrics — Liquid Glass. It must show the React tuner controls, not Unable to load the Liquid Glass tuner. The normal tuner window is intentionally separate from the transparent capsule, so standard desktop capture is valid.

- [ ] **Step 3: Check for a new failure record and stop only the probe process**

Run:

~~~powershell
$afterLength = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
if ($afterLength -ne $beforeLength) { throw 'The tuner wrote a new initialization failure record.' }
~~~

Then terminate only a TunerProbe.exe process whose executable path is under work/tuner-probe; do not terminate a user FlowCast or other WebView process.

- [ ] **Step 4: Broader verification and artifact check**

Run:

~~~powershell
Set-Location audio-share/src/AudioShare.Lyrics/Frontend
npm.cmd test
Set-Location ../../..
dotnet test AudioShare.sln --no-restore
git diff --check
git status --short
~~~

Expected: frontend and full solution tests pass, git diff --check is clean, and generated artifacts remain unstaged.
