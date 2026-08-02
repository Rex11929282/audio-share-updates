# FlowCast Lyrics Dynamic Island and NetEase Standalone Design

Date: 2026-08-02
Status: Approved design; implementation has not started

## Goal

Evolve FlowCast Lyrics from a fixed receiver capsule into a Dynamic Island-style Windows lyric overlay that works without the FlowCast main application.

On the owner's computer, NetEase Cloud Music and FlowCast Lyrics are the only applications the owner opens for the lyric workflow. FlowCast Lyrics detects the current NetEase track, obtains real timestamped lyrics, keeps them synchronized with playback, displays them locally, and advertises the current lyric state to friends over the already-connected Radmin VPN room.

On a friend's computer, after Radmin VPN has joined the same room, the friend opens only FlowCast Lyrics. It automatically discovers the owner's Lyrics instance and shows the same lyric state. The friend does not need NetEase Cloud Music or the FlowCast main application.

Audio transport is outside this design. FlowCast or another audio-sharing method may carry the sound, but FlowCast Lyrics carries only lyric playback state.

## Current Baseline

The existing implementation already provides:

- a borderless, topmost, draggable WPF overlay;
- a React frontend hosted in WebView2;
- the requested `rdev/liquid-glass-react` dependency in `standard` mode;
- an honest no-lyric state;
- Radmin VPN adapter selection, automatic UDP discovery, and a TCP connection handshake;
- a minimal `LyricLine` presentation contract.

The existing Radmin connection does not yet transmit lyric frames. The existing window is also fixed at 680 by 92 device-independent pixels and always searches for FlowCast, so it cannot yet operate as a standalone NetEase lyric overlay.

## Confirmed Requirements

- The overlay behaves like a Dynamic Island: compact while idle and smoothly expands when useful content appears.
- The floating UI remains a pure capsule. It has no title bar, frame, logo, connection badge, close button, or other visible window chrome.
- The entire capsule remains draggable and topmost.
- The liquid-glass surface continues to use `rdev/liquid-glass-react`.
- The application automatically detects the NetEase Cloud Music Windows client.
- Changing tracks may make an outbound network request to retrieve lyrics.
- Successfully retrieved lyrics are cached locally for later reuse.
- FlowCast Lyrics must work locally without the FlowCast main application and without Radmin VPN.
- With Radmin VPN already connected, opening NetEase Cloud Music and FlowCast Lyrics on the owner computer is sufficient to share lyrics.
- With Radmin VPN already connected, the friend opens only FlowCast Lyrics to receive lyrics.
- Radmin connection remains automatic. No IP address, port, pairing code, or QR code is shown or requested.
- When a valid remote lyric share and local NetEase playback are both available, the remote share has priority. Local playback resumes automatically after the remote share disconnects.
- Production code never invents sample lyrics or presents stale text as current.
- `audio-share/src/AudioShare.App/App.xaml.cs`, `audio-share/src/AudioShare.App/MainWindow.xaml.cs`, and the main application's audio-routing code remain untouched.

## Approaches Considered

### 1. Windows media session plus NetEase lyric endpoint — selected

Use Windows `GlobalSystemMediaTransportControlsSessionManager` to detect an active NetEase media session, read its title, artist, playback state, and timeline, then resolve the NetEase track ID and retrieve the timed lyric document from NetEase.

This requires no NetEase modification or companion plugin. It is the smallest user-facing setup and matches the requirement that the owner open only NetEase Cloud Music and FlowCast Lyrics.

The NetEase lyric endpoint is not a stable public developer contract. The implementation therefore isolates it behind a small client, caches successful responses, and displays an honest unavailable state when both the endpoint and cache fail.

### 2. BetterNCM plugin bridge

A BetterNCM plugin could expose the exact internal track ID and lyric state. This can be accurate, but it adds an installation requirement, modifies the NetEase client environment, and would also require compatibility work when the NetEase client changes. It is rejected for the first release.

### 3. OCR of the NetEase desktop lyric window

Screen capture and OCR could read whatever NetEase renders. This avoids the lyric endpoint but requires the NetEase desktop lyric window to remain visible, can misread characters, and makes timing and pause handling less reliable. It is rejected.

## Dynamic Island Behavior

The native WPF window and the React glass surface change size together. WPF owns the real window dimensions; React owns the matching content and liquid-glass presentation.

| State | Size | Visible content |
| --- | ---: | --- |
| Idle | 180 x 44 | `等待播放` |
| Resolving a track | 280 x 52 | `正在取得歌詞` |
| Playing a lyric | 680 x 92 | Current real lyric line only |
| Paused | 680 x 92 | Current lyric line at reduced brightness |
| Track has no lyric | 320 x 52 | `這首歌沒有歌詞` |
| Lyric temporarily unavailable | 320 x 52 | `暫時無法取得歌詞` |

When a track changes, the island contracts to the resolving size before expanding with the first lyric. It does not flash the previous track's lyric while the new track is loading.

Size and opacity changes use a restrained approximately 320-millisecond spring-like easing. The glass remains in stable `standard` mode. When Windows reduced-motion or client-area animation settings disable motion, the window and content change immediately without interpolation.

The whole capsule is the drag target. There are no hover controls. The existing taskbar entry and `Alt+F4` remain the ways to close the application, avoiding a new tray subsystem in this scope.

## Automatic Roles

Every FlowCast Lyrics instance runs the same executable and decides its role from current state:

1. It starts local NetEase observation and Radmin discovery independently.
2. A valid remote lyric stream always becomes the active display source.
3. Without a valid remote stream, an active local NetEase session becomes the display source.
4. An instance with active local NetEase playback publishes its lyric state on the Radmin adapter so friends can discover it.
5. An instance with neither source shows the idle island.

The first valid remote broadcaster discovered is retained until it disconnects or stops sending valid heartbeats. Supporting multiple selectable broadcasters in one Radmin room is outside this release; one active owner per room is the supported model.

## Local NetEase Pipeline

### Media-session detection

A `NeteaseMediaSessionSource` owns Windows media-session access. It considers only sessions whose source application identifies as NetEase Cloud Music. It emits an immutable playback snapshot containing title, artist, duration, position, playback state, and the timestamp at which the position was measured.

If NetEase is installed but does not expose an active Windows media session, FlowCast Lyrics remains idle and does not guess from a process name or playlist order.

### Track-ID resolution

A `NeteaseTrackResolver` resolves the NetEase track ID in this order:

1. Read the NetEase `webdata/file/playingList` file as an optional local hint and match normalized title and artist values from the media session.
2. If no unambiguous local match exists, query NetEase search using the normalized title and artist.
3. Accept a result only when title and at least one artist match after normalization. Ambiguous results are rejected instead of selecting a random song.

The playlist file is read-only. FlowCast Lyrics never writes to NetEase files and never reads account tokens, cookies, or login state.

### Lyric retrieval and cache

A `NeteaseLyricsClient` requests the timestamped lyric document for the resolved track ID. A small `LrcParser` converts valid LRC timestamps into ordered `LyricLine` values and ignores metadata tags and malformed rows.

Successful lyric documents are stored in a FlowCast Lyrics application-data cache keyed by track ID. Cache files contain only the track ID, retrieval time, and lyric data. They do not contain NetEase credentials or audio.

If the network request fails, the cache is used when available. If the response states that no lyric exists, the island shows `這首歌沒有歌詞`. If the request fails and no cache exists, the island shows `暫時無法取得歌詞` at the compact 320 by 52 size.

### Playback synchronization

The local coordinator extrapolates the current playback position from the last Windows timeline snapshot only while playback is active. It freezes immediately on pause, replaces its position on seek, and selects the `LyricLine` whose timestamp range contains the current position.

The coordinator never advances beyond the last known track duration and clears the previous line immediately on track change or session loss.

## Radmin Lyric Transport

The transport binds and advertises only on the active Radmin VPN IPv4 adapter selected by the existing 26.x adapter rules. A missing Radmin adapter disables remote sharing without affecting local NetEase lyrics.

The existing discovery and TCP handshake are extended with versioned newline-delimited JSON frames. A frame contains only:

- protocol version;
- session identifier and monotonic sequence number;
- NetEase track ID;
- current `LyricLine`, or `null` when no real line is active;
- playback position and measured UTC timestamp;
- playing or paused state;
- source status such as resolving, no lyric, or unavailable.

The owner sends a frame on connection, track change, line change, pause, resume, and seek, plus a periodic heartbeat. The friend uses the newest sequence number and discards duplicate, older, malformed, oversized, or unsupported-version frames.

A remote stream is considered lost after its TCP connection closes or its heartbeat expires. The displayed remote lyric is then cleared immediately and source selection falls back to local NetEase or idle.

The transport does not send audio, album art, playlists, NetEase account data, cookies, or files. It does not bind to ordinary LAN or internet adapters.

## Component Boundaries

### `NeteaseMediaSessionSource`

- Does: observe NetEase media identity, playback state, and timeline.
- Depends on: Windows media-session APIs.
- Does not: fetch or parse lyrics, render UI, or use Radmin.

### `NeteaseTrackResolver`

- Does: map verified title and artist metadata to one NetEase track ID.
- Depends on: the optional read-only local playing-list hint and a small search client.
- Does not: select ambiguous matches.

### `NeteaseLyricsClient` and `LrcParser`

- Do: retrieve, cache, and parse timestamped lyrics.
- Depend on: HTTP and FlowCast Lyrics application data.
- Do not: inspect NetEase credentials or control playback.

### `LyricsSourceCoordinator`

- Does: apply remote-first source priority, synchronize time, clear stale lines, and publish one display state.
- Depends on: local playback snapshots, parsed lyrics, and remote frames.
- Does not: own WPF or React details.

### Radmin host and receiver

- Do: discover one another and carry validated lyric frames on Radmin VPN.
- Depend on: the existing Radmin adapter selector and versioned wire contracts.
- Do not: query NetEase or choose what the UI displays.

### WPF shell and React frontend

- Do: animate the real window size, host WebView2, render the island state, and support dragging.
- Depend on: the coordinator's display snapshot.
- Do not: perform media discovery, HTTP requests, lyric parsing, or socket protocol decisions.

## Display Data Contract

The frontend snapshot is extended without exposing transport details:

```text
IslandState
  Mode: Idle | Resolving | Playing | Paused | NoLyrics | Unavailable
  DisplayText: string
  LyricLine: LyricLine or null
  Source: None | LocalNetEase | RemoteRadmin
```

Only `Playing` and `Paused` may carry a non-empty lyric line. Status modes carry approved status copy. React validates every snapshot and keeps the last valid snapshot if it receives malformed data.

## Failure Handling

- No NetEase session and no remote host: show the idle island.
- NetEase process exists without a media session: remain idle; do not infer a current track from playlist order.
- Track resolution is ambiguous: show `暫時無法取得歌詞`; do not choose an arbitrary result.
- Network lyric request fails: use the local cache, otherwise show the unavailable state.
- Lyric response has no timed lines: show the no-lyric state.
- Radmin is absent: local mode continues normally.
- Remote handshake or frame validation fails: ignore the peer and continue discovery.
- Remote heartbeat expires: clear the remote line and fall back automatically.
- WebView2 initialization fails: use a native WPF fallback with the same island size and honest text.
- Unexpected source exceptions are contained at their component boundary and do not terminate the overlay.

## Privacy and Security

- Read only the NetEase media-session metadata and optional local playing-list file needed to identify the current track.
- Never read or transmit NetEase cookies, credentials, account identifiers, or private playlists.
- Send only the current lyric playback frame to the connected Radmin peer.
- Bind remote discovery and transport only to the selected Radmin adapter.
- Enforce a small maximum wire-frame size and reject unknown protocol versions.
- Load React assets locally in WebView2; do not navigate the embedded view to remote content.

## Packaging

FlowCast Lyrics remains a separately packaged Windows product. Its release includes the self-contained .NET executable, required local React assets, WebView2 dependencies, and third-party notices. Node.js is a build-time dependency only.

The Lyrics executable and update package are not embedded in every FlowCast main-application update. A friend receives one standalone FlowCast Lyrics ZIP or installer and launches that product directly.

## Planned File Scope

Expected modifications:

- `audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs`
- `audio-share/src/AudioShare.Windows/RadminLyricsLink.cs`
- `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- `audio-share/src/AudioShare.Lyrics/LyricsOverlayPresenter.cs`
- `audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.ts`
- `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx`
- `audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css`
- focused existing C# and frontend tests

Expected additions under `audio-share/src/AudioShare.Lyrics`:

- NetEase media-session source
- track resolver and lyric HTTP client
- LRC parser and cache
- source coordinator
- small wire/display records when they do not belong in the dependency-free contracts project
- focused unit and integration tests under `audio-share/tests`

Explicitly untouched:

- `audio-share/src/AudioShare.App/App.xaml.cs`
- `audio-share/src/AudioShare.App/MainWindow.xaml.cs`
- FlowCast main-window XAML
- all FlowCast audio-routing and helper behavior

## Verification

### Automated

- Unit tests identify only NetEase media sessions.
- Track-resolution tests accept exact normalized matches and reject ambiguous matches.
- LRC tests cover timestamp parsing, metadata removal, malformed rows, multiple timestamps, and empty lyrics.
- Synchronization tests cover play, pause, resume, seek, track change, and end of track.
- Source-priority tests prove that remote wins and local resumes after remote expiry.
- Wire tests cover versioning, ordering, frame-size limits, malformed JSON, disconnect, and heartbeat expiry.
- Existing Radmin discovery tests continue to pass.
- Frontend tests verify all six state-to-size mappings across the four approved sizes and ensure no status state renders a fake lyric.
- Full frontend and .NET release test suites pass sequentially.

### Integration and perceptual QA

- A live NetEase smoke test verifies the installed NetEase 3.1.37 client is detected, a real track ID is resolved, a timed lyric is retrieved, and pause and seek remain synchronized.
- A local two-process sandbox test verifies automatic discovery, connection, lyric-frame delivery, pause, seek, and disconnect fallback without manual addressing.
- The active Radmin adapter is verified as a valid bind address on this computer.
- A real two-computer same-room Radmin test remains required for final remote-environment proof; one computer can prove protocol and local binding but cannot prove the second machine's firewall and room state.
- Visual QA launches the exact packaged executable and verifies 180 x 44 idle, 280 x 52 resolving, 680 x 92 lyric, and 320 x 52 no-lyric states on light and dark backgrounds.
- Interaction QA verifies topmost behavior, whole-capsule dragging, multi-monitor movement, no visible window chrome, and reduced-motion behavior.

## Success Criteria

The work is complete when:

1. The owner opens NetEase Cloud Music and FlowCast Lyrics, plays a real song, and sees correctly synchronized real lyrics in the expanding island.
2. FlowCast main is not running and local lyrics still work.
3. With Radmin VPN already connected to the same room, a friend opens only FlowCast Lyrics, connects automatically, and receives the same current lyric and playback state.
4. Pause, resume, seek, track change, disconnect, and local fallback behave as specified.
5. The idle UI is a 180 by 44 pure capsule, the lyric UI expands to 680 by 92, and no traditional window chrome appears.
6. No production state invents lyrics or displays a stale lyric as current.
7. The protected FlowCast main-application files and audio-routing behavior remain unchanged.
8. Automated tests, sandbox connection testing, packaging checks, and actual-executable visual QA pass.

## Technical References

- `rdev/liquid-glass-react`: https://github.com/rdev/liquid-glass-react
- Windows `GlobalSystemMediaTransportControlsSessionManager`: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager
- Microsoft WebView2 for WPF: https://learn.microsoft.com/en-us/microsoft-edge/webview2/platforms/wpf
- Archived Binaryify NetEase API project, retained only as historical reference and not used as a runtime dependency: https://github.com/Binaryify/NeteaseCloudMusicApi
