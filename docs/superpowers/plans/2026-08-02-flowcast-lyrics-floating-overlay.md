# FlowCast Lyrics Floating Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a compact, borderless, topmost, draggable FlowCast Lyrics companion window using a WPF WebView2 shell and a React `liquid-glass-react` subtitle surface, with honest connection states and a future-ready lyric input interface.

**Architecture:** A dependency-free contracts project defines `ConnectionState`, `LyricLine`, and `ILyricsOverlaySink`. The WPF companion owns Radmin discovery, native window behavior, WebView2 lifecycle, and a state presenter; a bundled local React app owns rendering only. State crosses the WPF/WebView2 boundary as small JSON messages, while the existing Radmin protocol remains unchanged.

**Tech Stack:** .NET 8 WPF, Microsoft.Web.WebView2 1.0.4078.44, React 19.2.8, TypeScript 7.0.2, Vite 8.2.0, Vitest 4.1.10, liquid-glass-react 1.1.1, xUnit 2.5.3

## Global Constraints

- Target Windows x64 and `net8.0-windows10.0.19041.0`.
- The window is approximately 680 by 150 device-independent pixels, borderless, non-resizable, topmost, and draggable between monitors.
- A connected client with no real lyric displays exactly `已連線，等待歌詞`.
- Never ship sample lyrics or invent lyric, song, artist, or progress data.
- Use `liquid-glass-react` in `standard` mode; do not use the less stable `shader` mode.
- Load only bundled local web assets; do not navigate WebView2 to a remote site.
- Do not alter `audio-share/src/AudioShare.App/App.xaml.cs`, `audio-share/src/AudioShare.App/MainWindow.xaml.cs`, main-app XAML, audio routing, or the Radmin wire protocol.
- Preserve all unrelated dirty and untracked files.

---

## File Structure

### New contract files

- `audio-share/src/AudioShare.Lyrics.Contracts/AudioShare.Lyrics.Contracts.csproj`: dependency-free shared contract assembly.
- `audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs`: connection enum, lyric record, and sink interface.
- `audio-share/tests/AudioShare.Core.Tests/LyricsContractsTests.cs`: contract and normalization behavior tests.

### New WPF bridge files

- `audio-share/src/AudioShare.Lyrics/LyricsOverlayPresenter.cs`: stores the authoritative overlay state, clears stale lyrics, and emits serialized snapshots.
- `audio-share/src/AudioShare.Lyrics/WindowBackdrop.cs`: enables the Windows 11 light transient backdrop and reports whether the Windows 10 fallback is required.
- `audio-share/tests/AudioShare.Core.Tests/LyricsOverlayPresenterTests.cs`: presenter state and JSON tests.

### New React files

- `audio-share/src/AudioShare.Lyrics/Frontend/package.json`: pinned frontend dependencies and build/test scripts.
- `audio-share/src/AudioShare.Lyrics/Frontend/package-lock.json`: generated npm lockfile.
- `audio-share/src/AudioShare.Lyrics/Frontend/vite.config.ts`: deterministic build into `dist` with relative assets.
- `audio-share/src/AudioShare.Lyrics/Frontend/tsconfig.json`: strict TypeScript configuration.
- `audio-share/src/AudioShare.Lyrics/Frontend/index.html`: local WebView2 page.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx`: React entry point.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.ts`: message types and pure state reducer.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx`: visual overlay component.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css`: responsive liquid-glass visual system.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.test.ts`: honest-state reducer tests.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx`: rendered copy and no-fake-lyric tests.
- `audio-share/src/AudioShare.Lyrics/Frontend/src/testSetup.ts`: DOM test setup.

### Modified project and shell files

- `audio-share/AudioShare.sln`: add `AudioShare.Lyrics.Contracts`.
- `audio-share/Directory.Packages.props`: centrally pin Microsoft.Web.WebView2 1.0.4078.44.
- `audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj`: reference contracts and WebView2, build the frontend incrementally, and copy `dist` to publish output.
- `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`: replace the diagnostic window with the borderless composition-control shell and native drag/close layer.
- `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`: initialize local WebView2, connect Radmin state to the presenter, and provide the WPF fallback surface.
- `audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj`: reference contracts.
- `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`: retain startup coverage and add window-markup contract coverage.
- `audio-share/ThirdPartyNotices.txt`: add `liquid-glass-react` MIT and Microsoft WebView2 notices.

---

### Task 1: Define the lyric and connection contracts

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics.Contracts/AudioShare.Lyrics.Contracts.csproj`
- Create: `audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LyricsContractsTests.cs`
- Modify: `audio-share/AudioShare.sln`
- Modify: `audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj`

**Interfaces:**
- Produces: `AudioShare.Lyrics.Contracts.ConnectionState`
- Produces: `AudioShare.Lyrics.Contracts.LyricLine(string Text, long StartTimeMilliseconds, long? EndTimeMilliseconds)`
- Produces: `AudioShare.Lyrics.Contracts.ILyricsOverlaySink.SetConnectionState(ConnectionState state)`
- Produces: `AudioShare.Lyrics.Contracts.ILyricsOverlaySink.ShowLyricLine(LyricLine? line)`

- [ ] **Step 1: Write the failing contract test**

```csharp
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsContractsTests
{
    [Fact]
    public void LyricLine_PreservesReceivedTimingAndText()
    {
        var line = new LyricLine("真實歌詞", 1250, 4800);

        Assert.Equal("真實歌詞", line.Text);
        Assert.Equal(1250, line.StartTimeMilliseconds);
        Assert.Equal(4800, line.EndTimeMilliseconds);
    }

    [Fact]
    public void ConnectionState_ContainsOnlyTheApprovedStates()
    {
        Assert.Equal(
            ["Searching", "Unavailable", "Connected", "Disconnected"],
            Enum.GetNames<ConnectionState>());
    }
}
```

- [ ] **Step 2: Add the test project reference and verify RED**

Add this project reference to `AudioShare.Core.Tests.csproj`:

```xml
<ProjectReference Include="..\..\src\AudioShare.Lyrics.Contracts\AudioShare.Lyrics.Contracts.csproj" />
```

Run:

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~LyricsContractsTests" --verbosity minimal
```

Expected: build fails because `AudioShare.Lyrics.Contracts.csproj` and its public types do not exist.

- [ ] **Step 3: Create the minimal contract project and types**

`AudioShare.Lyrics.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`LyricsContracts.cs`:

```csharp
namespace AudioShare.Lyrics.Contracts;

public enum ConnectionState
{
    Searching,
    Unavailable,
    Connected,
    Disconnected
}

public sealed record LyricLine(
    string Text,
    long StartTimeMilliseconds,
    long? EndTimeMilliseconds);

public interface ILyricsOverlaySink
{
    void SetConnectionState(ConnectionState state);
    void ShowLyricLine(LyricLine? line);
}
```

Add the project to the solution:

```powershell
dotnet sln AudioShare.sln add src\AudioShare.Lyrics.Contracts\AudioShare.Lyrics.Contracts.csproj
```

- [ ] **Step 4: Verify GREEN**

Run the focused test command from Step 2. Expected: 2 tests pass.

- [ ] **Step 5: Commit Task 1**

```powershell
git add AudioShare.sln src\AudioShare.Lyrics.Contracts tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj tests\AudioShare.Core.Tests\LyricsContractsTests.cs
git commit -m "feat: define FlowCast lyric display contracts"
```

---

### Task 2: Build the state presenter and JSON bridge boundary

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/LyricsOverlayPresenter.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LyricsOverlayPresenterTests.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj`

**Interfaces:**
- Consumes: `ConnectionState`, `LyricLine`, and `ILyricsOverlaySink` from Task 1.
- Produces: `LyricsOverlayPresenter.StateChanged` carrying a complete JSON state snapshot.
- Produces: `LyricsOverlayPresenter.GetSnapshotJson()` for WebView2 initialization.

- [ ] **Step 1: Write failing presenter tests**

```csharp
using System.Text.Json;
using AudioShare.Lyrics;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Core.Tests;

public sealed class LyricsOverlayPresenterTests
{
    [Fact]
    public void ConnectedWithoutALine_UsesTheHonestWaitingCopy()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.SetConnectionState(ConnectionState.Connected);

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("connected", json.RootElement.GetProperty("connectionState").GetString());
        Assert.Equal("已連線，等待歌詞", json.RootElement.GetProperty("displayText").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
    }

    [Fact]
    public void Disconnect_ClearsAPreviouslyReceivedLine()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.SetConnectionState(ConnectionState.Connected);
        presenter.ShowLyricLine(new LyricLine("只可顯示收到的文字", 1000, null));

        presenter.SetConnectionState(ConnectionState.Disconnected);

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("與 FlowCast 的連線已中斷，正在重新連線", json.RootElement.GetProperty("displayText").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
    }

    [Fact]
    public void WhitespaceLine_ReturnsToWaitingCopy()
    {
        var presenter = new LyricsOverlayPresenter();
        presenter.SetConnectionState(ConnectionState.Connected);
        presenter.ShowLyricLine(new LyricLine("   ", 0, null));

        using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
        Assert.Equal("已連線，等待歌詞", json.RootElement.GetProperty("displayText").GetString());
    }
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~LyricsOverlayPresenterTests" --verbosity minimal
```

Expected: build fails because `LyricsOverlayPresenter` does not exist.

- [ ] **Step 3: Implement the minimal presenter**

Implement `LyricsOverlayPresenter` as an `ILyricsOverlaySink`. Store an initial `Searching` state and an optional `LyricLine`. `SetConnectionState` clears the line unless the new state is `Connected`; `ShowLyricLine` treats null or whitespace text as no line. Both methods raise `StateChanged` with `GetSnapshotJson()`.

Use this exact serialized snapshot shape:

```json
{
  "connectionState": "connected",
  "displayText": "已連線，等待歌詞",
  "lyricLine": null
}
```

Use these exact state copies:

```csharp
ConnectionState.Searching => "正在尋找 FlowCast",
ConnectionState.Unavailable => "未偵測到 Radmin VPN",
ConnectionState.Connected => line?.Text ?? "已連線，等待歌詞",
ConnectionState.Disconnected => "與 FlowCast 的連線已中斷，正在重新連線"
```

Add the contracts project reference to `AudioShare.Lyrics.csproj`.

- [ ] **Step 4: Verify GREEN**

Run the focused test command from Step 2. Expected: 3 tests pass.

- [ ] **Step 5: Commit Task 2**

```powershell
git add src\AudioShare.Lyrics\AudioShare.Lyrics.csproj src\AudioShare.Lyrics\LyricsOverlayPresenter.cs tests\AudioShare.Core.Tests\LyricsOverlayPresenterTests.cs
git commit -m "feat: add FlowCast Lyrics overlay presenter"
```

---

### Task 3: Create the React liquid-glass frontend

**Files:**
- Create all files listed under “New React files” in the File Structure section.

**Interfaces:**
- Consumes: complete JSON snapshots with `connectionState`, `displayText`, and nullable `lyricLine`.
- Produces: a `ready` WebView2 message after React mounts.

- [ ] **Step 1: Initialize pinned frontend dependencies**

Create `Frontend/package.json` with these scripts and exact versions:

```json
{
  "name": "flowcast-lyrics-overlay",
  "private": true,
  "version": "1.0.0",
  "type": "module",
  "scripts": {
    "build": "tsc --noEmit && vite build",
    "test": "vitest run"
  },
  "dependencies": {
    "liquid-glass-react": "1.1.1",
    "react": "19.2.8",
    "react-dom": "19.2.8"
  },
  "devDependencies": {
    "@testing-library/jest-dom": "7.0.0",
    "@testing-library/react": "16.3.2",
    "@types/react": "19.2.18",
    "@types/react-dom": "19.2.4",
    "@vitejs/plugin-react": "6.0.5",
    "jsdom": "30.0.1",
    "typescript": "7.0.2",
    "vite": "8.2.0",
    "vitest": "4.1.10"
  }
}
```

Run `npm.cmd install` in `AudioShare.Lyrics/Frontend` to generate `package-lock.json`.

- [ ] **Step 2: Write failing state and render tests**

`overlayState.test.ts` must assert:

```ts
expect(reduceOverlayState(initialState, {
  connectionState: 'connected',
  displayText: '已連線，等待歌詞',
  lyricLine: null,
}).displayText).toBe('已連線，等待歌詞')
```

`LyricsOverlay.test.tsx` must render a connected state with `lyricLine: null`, assert that `已連線，等待歌詞` is visible, and assert that no elements with `data-testid="lyric-line"` exist.

- [ ] **Step 3: Verify RED**

```powershell
npm.cmd test --prefix src\AudioShare.Lyrics\Frontend
```

Expected: tests fail because `overlayState.ts` and `LyricsOverlay.tsx` do not exist.

- [ ] **Step 4: Implement the frontend**

Implement a pure `reduceOverlayState` that accepts only the four approved connection-state strings and a nullable lyric line. Ignore malformed messages and retain the previous valid state.

Implement `LyricsOverlay` with:

```tsx
<LiquidGlass
  mode="standard"
  displacementScale={34}
  blurAmount={0.11}
  saturation={122}
  aberrationIntensity={1}
  elasticity={0.12}
  cornerRadius={34}
  overLight
>
  {/* FlowCast mark, connection dot, and state or received lyric */}
</LiquidGlass>
```

Use CSS to fill the 680-by-150 viewport, keep a minimum 4.5:1 text contrast, reveal the native drag/close controls on hover, and disable transitions under `prefers-reduced-motion: reduce`. Do not put any sample lyric in source code.

In `main.tsx`, subscribe to `window.chrome.webview` messages when available, render the initial `Searching` state, and send `{ type: 'ready' }` after mounting.

- [ ] **Step 5: Verify GREEN and production build**

```powershell
npm.cmd test --prefix src\AudioShare.Lyrics\Frontend
npm.cmd run build --prefix src\AudioShare.Lyrics\Frontend
```

Expected: frontend tests pass and `Frontend/dist/index.html` exists with relative asset URLs.

- [ ] **Step 6: Commit Task 3**

```powershell
git add src\AudioShare.Lyrics\Frontend
git commit -m "feat: build FlowCast liquid glass lyric surface"
```

---

### Task 4: Host the frontend in a draggable WPF overlay

**Files:**
- Modify: `audio-share/Directory.Packages.props`
- Modify: `audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`

**Interfaces:**
- Consumes: `LyricsOverlayPresenter` and existing `RadminLyricsReceiver`.
- Produces: local WebView2 rendering and native move/close behavior.

- [ ] **Step 1: Write failing WPF shell contract tests**

Extend `FlowCastLyricsStartupTests` to load `MainWindow.xaml` as XML and assert these exact properties:

```csharp
Assert.Equal("None", root.Attribute("WindowStyle")?.Value);
Assert.Equal("NoResize", root.Attribute("ResizeMode")?.Value);
Assert.Equal("True", root.Attribute("Topmost")?.Value);
Assert.Equal("680", root.Attribute("Width")?.Value);
Assert.Equal("150", root.Attribute("Height")?.Value);
Assert.Contains("WebView2CompositionControl", xaml);
```

Also assert that the XAML does not contain a production lyric example.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~FlowCastLyricsStartupTests" --verbosity minimal
```

Expected: the new markup assertions fail against the current titled 420-by-230 window.

- [ ] **Step 3: Add WebView2 and incremental frontend build integration**

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.Web.WebView2" Version="1.0.4078.44" />
```

Add to `AudioShare.Lyrics.csproj`:

```xml
<PackageReference Include="Microsoft.Web.WebView2" />
<Target Name="BuildLyricsFrontend"
        BeforeTargets="PrepareForBuild"
        Inputs="Frontend\package-lock.json;Frontend\src\**\*"
        Outputs="Frontend\dist\index.html"
        Condition="'$(DesignTimeBuild)' != 'true'">
  <Exec WorkingDirectory="Frontend" Command="npm.cmd ci" />
  <Exec WorkingDirectory="Frontend" Command="npm.cmd run build" />
</Target>
<Target Name="CopyLyricsFrontendToOutput" AfterTargets="Build" DependsOnTargets="BuildLyricsFrontend">
  <ItemGroup>
    <LyricsFrontendAsset Include="Frontend\dist\**\*" />
  </ItemGroup>
  <Copy SourceFiles="@(LyricsFrontendAsset)"
        DestinationFiles="@(LyricsFrontendAsset->'$(OutDir)wwwroot\%(RecursiveDir)%(Filename)%(Extension)')" />
</Target>
<Target Name="CopyLyricsFrontendToPublish" AfterTargets="Publish" DependsOnTargets="BuildLyricsFrontend">
  <ItemGroup>
    <LyricsFrontendPublishAsset Include="Frontend\dist\**\*" />
  </ItemGroup>
  <Copy SourceFiles="@(LyricsFrontendPublishAsset)"
        DestinationFiles="@(LyricsFrontendPublishAsset->'$(PublishDir)wwwroot\%(RecursiveDir)%(Filename)%(Extension)')" />
</Target>
```

- [ ] **Step 4: Replace the window markup**

Set `WindowStyle="None"`, `ResizeMode="NoResize"`, `Topmost="True"`, `Width="680"`, `Height="150"`, `Background="Transparent"`, and `WindowStartupLocation="CenterScreen"`. Add one `WebView2CompositionControl`, one native fallback text surface, a top drag handle, and a hover-revealed close button. Keep all controls inside the Lyrics window only.

- [ ] **Step 5: Implement the native backdrop with an honest fallback**

Create `WindowBackdrop.TryEnableLightBackdrop(Window window)`. After the window source is initialized, call `DwmSetWindowAttribute` with `DWMWA_SYSTEMBACKDROP_TYPE` (`38`) and `DWMSBT_TRANSIENTWINDOW` (`3`). Return `false` when Windows does not support the attribute or the native call fails; the XAML/React light semi-transparent background remains visible as the Windows 10 fallback. Do not use desktop capture or screen sampling.

- [ ] **Step 6: Implement WebView2 and Radmin state wiring**

In `MainWindow.xaml.cs`:

1. Create one `LyricsOverlayPresenter` and one cancellation source.
2. Initialize WebView2 with `EnsureCoreWebView2Async`.
3. Disable devtools, status bar, zoom control, browser accelerators, and the default context menu.
4. Set `DefaultBackgroundColor` to transparent.
5. Map the copied `wwwroot` folder to `https://flowcast.local/` using `SetVirtualHostNameToFolderMapping` with `CoreWebView2HostResourceAccessKind.DenyCors`.
6. Navigate only to `https://flowcast.local/index.html`.
7. On React's `ready` message, post `presenter.GetSnapshotJson()` with `PostWebMessageAsJson`.
8. Forward every `StateChanged` snapshot after WebView2 is ready.
9. Set `Searching` before Radmin discovery, `Unavailable` when no Radmin adapter exists, and `Connected` after the existing receiver handshake succeeds.
10. On WebView2 failure, show the native fallback with the presenter's current honest `displayText`.
11. On left mouse down over the drag handle, call `DragMove()`.
12. On close, cancel discovery, dispose receiver and WebView2, and close without blocking the UI thread.

- [ ] **Step 7: Verify GREEN**

Run the focused test from Step 2, then:

```powershell
dotnet build AudioShare.sln --configuration Release
```

Expected: tests pass, the solution builds without errors, and `AudioShare.Lyrics/bin/Release/.../wwwroot/index.html` exists.

- [ ] **Step 8: Commit Task 4**

```powershell
git add Directory.Packages.props src\AudioShare.Lyrics tests\AudioShare.Core.Tests\FlowCastLyricsStartupTests.cs
git commit -m "feat: host draggable FlowCast Lyrics overlay"
```

---

### Task 5: License, release build, and perceptual verification

**Files:**
- Modify: `audio-share/ThirdPartyNotices.txt`
- Test: all C# and frontend tests
- Output: `audio-share/release/lyrics-overlay/publish/FlowCast Lyrics.exe`

**Interfaces:**
- Consumes: the completed companion from Tasks 1–4.
- Produces: a verified standalone friend-side publish directory.

- [ ] **Step 1: Add dependency notices**

Add named entries and source/license links for:

```text
Microsoft.Web.WebView2 — Microsoft — BSD-3-Clause
https://www.nuget.org/packages/Microsoft.Web.WebView2/

liquid-glass-react 1.1.1 — rdev — MIT
https://github.com/rdev/liquid-glass-react
```

- [ ] **Step 2: Run all automated verification**

```powershell
npm.cmd test --prefix src\AudioShare.Lyrics\Frontend
npm.cmd run build --prefix src\AudioShare.Lyrics\Frontend
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --verbosity minimal
git diff --check
```

Expected: every command exits 0; the C# suite has no failures; the frontend suite has no failures; `git diff --check` prints nothing.

- [ ] **Step 3: Publish the standalone friend-side executable**

```powershell
dotnet publish src\AudioShare.Lyrics\AudioShare.Lyrics.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  --output release\lyrics-overlay\publish
```

Expected: `release\lyrics-overlay\publish\FlowCast Lyrics.exe` and `release\lyrics-overlay\publish\wwwroot\index.html` exist. No main `AudioShare.App.exe`, Python helper, or audio-router payload appears in this publish directory.

- [ ] **Step 4: Run the real application smoke test**

Launch the new executable, verify the process remains alive for at least five seconds, and verify the visible window is 680 by 150, borderless, rounded, and topmost. Confirm it can be dragged to a second monitor and closed using the hover control.

With no host, verify `正在尋找 FlowCast` or `未偵測到 Radmin VPN`. With the existing local Radmin host sandbox, verify the UI changes to exactly `已連線，等待歌詞` and shows no lyric line.

- [ ] **Step 5: Capture perceptual proof**

Capture the real application over one light and one dark desktop background. Inspect both images for readable dark text, clean glass edges, no clipped controls, no fake lyric, and correct DPI scaling. If WebView2 transparent composition fails, keep the same React visual hierarchy and use the specified light semi-transparent internal fallback instead of widening scope to desktop capture.

- [ ] **Step 6: Commit Task 5**

```powershell
git add ThirdPartyNotices.txt
git commit -m "chore: document FlowCast Lyrics UI dependencies"
```

Do not commit generated `release/`, `bin/`, `obj/`, `node_modules/`, or frontend `dist/` directories.

---

## Final Review Checklist

- [ ] The production window never displays a fake lyric.
- [ ] Connected-without-lyrics copy is exactly `已連線，等待歌詞`.
- [ ] The overlay is compact, borderless, topmost, and draggable across monitors.
- [ ] WebView2 loads only bundled local content.
- [ ] React uses `liquid-glass-react` `standard` mode.
- [ ] Missing WebView2 or Radmin produces an honest native fallback instead of a crash.
- [ ] Main FlowCast audio routing and protected app files have no diff.
- [ ] Frontend tests, C# tests, Release build, standalone publish, and visual QA all pass.
