# FlowCast Lyrics AndroidLiquidGlass Renderer Design

**Status:** User-approved design, pending implementation planning
**Date:** 2026-08-03

## Goal

Replace the current `rdev/liquid-glass-react` / WebView2 visual layer with a real Kotlin Compose Desktop renderer built on [Kyant0/AndroidLiquidGlass](https://github.com/Kyant0/AndroidLiquidGlass)'s `Backdrop` library. Keep FlowCast Lyrics as a Windows product with a single user-facing launch point and retain its existing Radmin VPN discovery and connection behavior.

The desired visual is a small, draggable, borderless, topmost lyric layer: a lyric-first glass strip with strong visible liquid refraction, raised highlights, and irregular fluid edges. This is the user-selected combination of the visual directions **C: Lyrics-first glass strip** and **C: Strong liquid**.

## Scope

### Included

- A Compose Desktop renderer that uses AndroidLiquidGlass / Backdrop rather than an imitation of it.
- A packaged renderer helper process launched by `FlowCast Lyrics.exe`.
- A local Named Pipe protocol between the existing .NET host and the renderer.
- Transparent, borderless, always-on-top, draggable visual overlay behavior.
- Renderer lifecycle management, failure reporting, shutdown, and local settings/position synchronization.
- Packaging the renderer and necessary Kotlin runtime with the FlowCast Lyrics Windows product.
- Third-party notice updates for the Apache-2.0 Backdrop dependency.

### Explicitly excluded

- New Radmin networking, ports, pairing codes, QR codes, or manual address entry.
- Changing FlowCast audio routing, the FlowCast main application, `App.xaml.cs`, or `MainWindow.xaml.cs`.
- Capturing a lyric source, matching songs, transmitting real-time lyrics, or displaying fabricated lyrics.
- Rebuilding FlowCast Lyrics as an Android application.
- Publishing a GitHub release as part of this implementation pass.

## User Experience

### One launch point

Friends still open only `FlowCast Lyrics.exe`. The host starts the helper internally; the helper has no separate taskbar entry, title bar, or setup workflow.

### Visual states

| State | Surface | Content |
| --- | --- | --- |
| Finding FlowCast | compact fluid capsule | `正在尋找 FlowCast` |
| Connected, no lyrics | expanded fluid capsule | `已連線，等待歌詞` |
| Future lyric data available | lyric-first glass strip | current lyric prominent; supporting text secondary |

The first two states are the only live states in this pass. The future lyric state is a renderer interface contract only; no lyric source or network transfer is implemented.

### Visual language

- The overlay is compact by default: about 190 x 48 logical pixels while searching and about 240 x 64 while connected.
- A future lyric state may expand naturally within a 320–420 pixel width and about 108 logical pixel height.
- The glass uses the selected strong-liquid direction: noticeable refraction, convex highlights, bright edge behavior, and slightly irregular liquid contours.
- The surface remains readable over varied desktop backgrounds; text contrast is protected even with strong glass effects.
- The overlay is freely draggable on any monitor and remains topmost.
- Right-click opens only a compact native menu: `Adjust Glass` and `Close FlowCast Lyrics`.
- `Adjust Glass` opens a small borderless option sheet anchored near the overlay, not a conventional framed application window.

## Architecture

```text
FlowCast Lyrics.exe (.NET / WPF host)
  ├─ existing Radmin discovery and connection state
  ├─ settings / updater / process supervisor
  └─ Named Pipe (local, current user only)
       ├─ state and settings messages → FlowCast Lyrics Glass.exe
       └─ ready, movement, settings, fault events ← FlowCast Lyrics Glass.exe

FlowCast Lyrics Glass.exe (Kotlin Compose Desktop)
  ├─ AndroidLiquidGlass / Backdrop rendering
  ├─ transparent draggable topmost overlay window
  └─ compact option sheet
```

### Host responsibilities

- Continue to own Radmin VPN discovery and the current connection lifecycle.
- Launch exactly one renderer helper while the host is active and wait for a `Ready` event.
- Send renderer state snapshots after every meaningful connection or setting change.
- Persist only host-owned settings and overlay position.
- Restart the helper once after an unexpected exit. If it fails again, retain the host and show a truthful local status: `玻璃渲染器不可用`.
- Kill the helper during normal host shutdown; no renderer process may remain afterwards.

### Renderer responsibilities

- Render only local display state received over the pipe.
- Own the visual window, drag behavior, context menu, option sheet, and AndroidLiquidGlass configuration.
- Never access Radmin VPN adapters or create a network listener.
- Send movement and committed settings changes back to the host.
- Exit when receiving `Shutdown` or when the host pipe closes.

### Local pipe contract

The protocol is length-prefixed JSON with a protocol version field. It uses a per-user Named Pipe and no TCP/UDP listener.

Host-to-renderer commands:

```json
{ "version": 1, "type": "initialize", "settings": { "position": { "x": 0, "y": 0 }, "glass": {} } }
{ "version": 1, "type": "connection-state", "state": "finding-flowcast" }
{ "version": 1, "type": "connection-state", "state": "connected-awaiting-lyrics" }
{ "version": 1, "type": "shutdown" }
```

Reserved future command, not produced in this pass:

```json
{ "version": 1, "type": "lyric-line", "current": "…", "secondary": "…" }
```

Renderer-to-host events:

```json
{ "version": 1, "type": "ready" }
{ "version": 1, "type": "position-changed", "x": 0, "y": 0 }
{ "version": 1, "type": "settings-committed", "glass": {} }
{ "version": 1, "type": "fault", "message": "…" }
```

## Migration

1. Add a separate Kotlin/Compose Desktop renderer project and a repeatable Windows distribution build.
2. Add the .NET process supervisor and Named Pipe client without changing Radmin logic.
3. Add the AndroidLiquidGlass visual states and context-menu/options-sheet behavior.
4. Replace the existing WebView2 `rdev/liquid-glass-react` overlay and tuning panel only after the helper handshake and visual smoke test are working.
5. Remove no Radmin or connection code during the migration.

The packaged output remains one FlowCast Lyrics product folder. It may contain the helper executable and runtime internally, but the user only launches `FlowCast Lyrics.exe`.

## Error handling

- If the helper cannot start, the host does not claim it is connected or displaying lyrics; it reports the renderer failure locally.
- If the helper exits once unexpectedly, the host restarts it once and resends the latest state snapshot.
- If the second start fails, automatic restart stops to avoid a crash loop.
- A malformed, unknown, or wrong-version pipe message is discarded and logged locally; it does not crash either process.
- No state contains fabricated lyric text. Until a future lyric sender exists, the connected visual state always says `已連線，等待歌詞`.

## Verification

- .NET unit tests for renderer process launching, single-restart behavior, pipe serialization, version rejection, and host shutdown cleanup.
- Kotlin unit tests for renderer state transitions and settings decoding.
- Kotlin desktop smoke test that launches the transparent overlay and verifies receipt of the `Ready` event.
- Windows sandbox integration test: launch host + helper, verify exactly one user-facing overlay, drag it, update its state through the pipe, and close the host without leaving a helper process.
- Manual visual check on a Windows desktop with varied backgrounds for readable strong-liquid glass.
- Radmin cross-computer behavior remains a separate two-computer verification; a one-machine sandbox only verifies the existing host-to-renderer path.

## Acceptance criteria

1. The live FlowCast Lyrics visual layer uses AndroidLiquidGlass / Backdrop in the Compose helper, not `rdev/liquid-glass-react`.
2. Friends still launch one FlowCast Lyrics executable and enter no IP, port, pairing code, or QR data.
3. The renderer surface is borderless, topmost, draggable, lyric-first, and visibly strong-liquid rather than a generic glass card.
4. Without a lyric source, the only connected message is `已連線，等待歌詞`.
5. The helper closes with the host and cannot leave a background orphan.
6. The existing FlowCast main application, audio routing, and Radmin discovery behavior remain unchanged.
