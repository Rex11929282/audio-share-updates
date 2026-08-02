# FlowCast Lyrics Floating Overlay Design

Date: 2026-08-02
Status: Approved direction; implementation has not started

## Goal

Turn FlowCast Lyrics into a small Windows companion overlay that a friend can place anywhere on any monitor. The window uses a light Apple-inspired liquid-glass treatment, stays above ordinary windows, and displays the current connection state or a real lyric line supplied through a future transport.

This iteration builds the overlay shell, its visual frontend, and the minimal data boundary needed to receive lyrics later. It does not acquire lyrics, change the Radmin protocol, or alter FlowCast's audio routing.

## Confirmed Requirements

- FlowCast Lyrics remains the friend-side Windows companion application.
- The window is compact, borderless, always on top, and freely draggable across monitors.
- It is not a full-screen wallpaper or background window.
- The visual language is light liquid glass with clear FlowCast branding and readable typography.
- React runs inside a WPF WebView2 shell and uses `rdev/liquid-glass-react`.
- A connected client without a lyric line displays exactly `已連線，等待歌詞`.
- The production UI must never display invented or sample lyrics.
- This iteration defines a receiving interface but does not implement a real lyric source or lyric transport protocol.
- `AudioShare.App/App.xaml.cs`, `AudioShare.App/MainWindow.xaml.cs`, and all main-app audio-routing code are excluded from the change set.

## Approaches Considered

### 1. Hybrid native backdrop and React glass surface — selected

WPF owns the real desktop window, topmost behavior, multi-monitor movement, native backdrop, and WebView2 lifecycle. React owns the visual glass card and subtitle presentation. `liquid-glass-react` runs in stable `standard` mode over a restrained internal color field, while Windows 11 supplies a native translucent backdrop where supported.

This gives the best balance of a convincing desktop overlay, correct Windows behavior, and the specifically requested React library. Windows versions without the native backdrop use a light semi-transparent fallback.

### 2. React-only glass inside a standard WebView2

This is simpler, but the React effect can only refract pixels inside the webpage. It cannot truly refract arbitrary desktop pixels behind the native window, so the result would look more like a glossy web card than a desktop glass overlay.

### 3. WPF-only acrylic overlay

This would provide the smallest runtime and easiest native drag behavior, but it would not use `liquid-glass-react` and therefore does not meet the confirmed requirement.

## Window Design

The initial window is a fixed compact surface approximately 680 by 150 device-independent pixels. It has rounded corners, no system title bar, does not appear as a full-screen surface, and remains topmost. The user can move it by dragging a narrow hover-revealed handle along the upper edge. A hover-revealed close button is provided because removing the system frame also removes the normal close control.

The WPF shell owns drag and close interactions. It uses `WebView2CompositionControl` instead of the standard WPF WebView2 control so native WPF controls can remain above the web surface without the normal airspace problem.

The first iteration does not add resizing, click-through mode, position persistence, settings, or a system tray menu. These are separate behaviors and are not required to establish the overlay.

## Visual Design

The overlay uses one light glass capsule with:

- a cool white and pale cyan body;
- a subtle FlowCast teal highlight along the upper-left edge;
- restrained refraction and frosting;
- dark navy text for contrast;
- `Segoe UI Variable` with the Windows fallback stack;
- a small FlowCast mark and connection indicator;
- short transition animations only when connection state or lyric content changes.

`liquid-glass-react` uses `standard` mode. The more accurate `shader` mode is explicitly excluded from the first implementation because the upstream project describes it as less stable. Continuous decorative animation is avoided; reduced-motion preferences disable movement transitions.

## Display States

The overlay supports five honest states:

1. `Searching`: show `正在尋找 FlowCast`.
2. `Unavailable`: show `未偵測到 Radmin VPN`.
3. `Connected` with no lyric: show `已連線，等待歌詞`.
4. `Connected` with a lyric: show only the received lyric text.
5. `Disconnected`: show `與 FlowCast 的連線已中斷，正在重新連線`.

No sample song title, artist, lyric, or playback progress appears in production. Test fixtures may provide sample data only inside automated tests and development previews that are not shipped as the default state.

## Architecture

### WPF shell

`AudioShare.Lyrics` continues to own startup and Radmin discovery. It creates the borderless topmost window, initializes WebView2 asynchronously, loads bundled local frontend assets, and forwards state changes as JSON messages.

The shell never navigates to a remote webpage. WebView2 development tools and the default context menu are disabled in release builds.

### React frontend

A Vite React TypeScript frontend lives under `AudioShare.Lyrics/Frontend`. Its single `LyricsOverlay` component renders the connection state and current lyric inside `LiquidGlass`. The built static assets are copied into the Lyrics publish output and loaded locally.

React has no Radmin, socket, file-system, or audio responsibilities.

### Shared data contract

A dependency-free `AudioShare.Lyrics.Contracts` class library defines only the future host/client boundary:

```csharp
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
```

The companion also exposes an `ILyricsOverlaySink` boundary with two operations: update the connection state and display or clear a `LyricLine`. FlowCast does not reference this project in this iteration; the contract is ready for the future transport without modifying the main application now.

## Data Flow

1. Existing Radmin discovery reports the current connection state to the Lyrics window.
2. The WPF presenter updates its local overlay state.
3. Once WebView2 reports that the React page is ready, WPF sends the complete current state using `PostWebMessageAsJson`.
4. React validates the message type and renders the matching honest state.
5. A future lyric transport will call `ILyricsOverlaySink.ShowLyricLine`; this iteration provides that seam but does not create the transport.

The WebView2 message shapes are deliberately small:

```json
{ "type": "connection-state", "state": "connected" }
{ "type": "lyric-line", "line": { "text": "...", "startTimeMilliseconds": 0, "endTimeMilliseconds": null } }
```

## Failure Handling

- Missing or disabled Radmin VPN becomes `Unavailable`; it does not crash the overlay or claim that discovery is still active.
- WebView2 initialization failure falls back to a small native WPF status surface with the same honest connection wording.
- Malformed or unknown web messages are ignored and logged locally; they never become visible lyric text.
- Empty or whitespace-only lyric text clears the lyric and returns the connected display to `已連線，等待歌詞`.
- A disconnected Radmin session clears the current lyric so stale text is not presented as current.

## Packaging

Node.js is a build-time dependency only. The friend does not need Node.js installed. The Lyrics release contains:

- the WPF executable and .NET runtime files according to the existing publish mode;
- the WebView2 loader dependency;
- the compiled local React assets;
- third-party license notices for WebView2 and `liquid-glass-react`.

The Lyrics installer and updater remain separate from the main FlowCast installer. No FlowCast audio router or Python helper is included in the Lyrics package.

## Planned File Scope

Files modified:

- `audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj`
- `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`

Files added:

- `audio-share/src/AudioShare.Lyrics.Contracts/AudioShare.Lyrics.Contracts.csproj`
- `audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs`
- `audio-share/src/AudioShare.Lyrics/LyricsOverlayPresenter.cs`
- `audio-share/src/AudioShare.Lyrics/Frontend/package.json`
- `audio-share/src/AudioShare.Lyrics/Frontend/package-lock.json`
- `audio-share/src/AudioShare.Lyrics/Frontend/vite.config.ts`
- `audio-share/src/AudioShare.Lyrics/Frontend/tsconfig.json`
- `audio-share/src/AudioShare.Lyrics/Frontend/index.html`
- `audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx`
- `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx`
- `audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css`
- focused C# and frontend tests for contracts, bridge behavior, and display states

Explicitly untouched:

- `audio-share/src/AudioShare.App/App.xaml.cs`
- `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- all main-app XAML and audio-routing implementation
- the Radmin discovery protocol during this UI iteration

## Verification

- C# contract and presenter tests verify state serialization, empty-line handling, and disconnect clearing.
- Frontend tests verify every connection state and ensure the connected-without-lyrics state contains exactly `已連線，等待歌詞`.
- A release build verifies React assets are present and WebView2 loads only local content.
- Startup smoke testing verifies the borderless window remains open and the Radmin receiver still connects.
- Visual QA captures the actual application over light and dark desktop content, checks text contrast, confirms no fake lyric appears, and checks the Windows 10 fallback.
- Interaction QA verifies dragging between monitors, topmost behavior, close behavior, and DPI scaling.

## Success Criteria

The work is complete when a friend can launch FlowCast Lyrics, see a compact draggable liquid-glass overlay, connect through the existing Radmin discovery path, and truthfully see `已連線，等待歌詞` until a real `LyricLine` is supplied through the defined interface. The main FlowCast application and its audio-routing files remain unchanged.

## Technical References

- `rdev/liquid-glass-react`: https://github.com/rdev/liquid-glass-react
- Microsoft WebView2 for WPF: https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/wpf
- WebView2 composition control and WPF airspace: https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf
