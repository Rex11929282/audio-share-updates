# FlowCast

FlowCast is a Windows desktop app for sharing audio from selected applications in Discord. It detects applications that are actively producing sound, sends only the applications you select to the Voicemeeter B1 sharing path, and keeps unselected applications local.

## Requirements

- Windows 10 version 2004 or newer.
- Voicemeeter Banana installed and running.
- In Discord, set the microphone to `Voicemeeter Out B1` and the speaker to `Voicemeeter AUX Input`.

FlowCast never lets Discord, Voicemeeter, or its own helper be selected for sharing. Windows saves routing by application executable identity, so all active processes from the same application can be affected together.

## Use

1. Open Voicemeeter Banana and choose your headphones or speakers as A1.
2. Open Chrome, NetEase Cloud Music, or another audio application. FlowCast keeps its inactive audio session visible so you can choose a route before playback begins.
3. Choose the application output route directly in FlowCast: Windows default, Voicemeeter Input (share path), Voicemeeter AUX Input (local only), or an enabled speaker/headphone device. Pause and resume the application after changing it. Choosing a route never starts sharing.
4. Start playback, select the programs you want friends to hear, click `开始分享`, then confirm the prompt. FlowCast changes only the selected application routes.
5. Use `静音分享` to mute only the shared program path. Your local listening continues.
6. Click `停止分享` to restore local-only playback. Closing the main window lets you choose the system tray or a full exit.

Selected program order can be changed by dragging selected cards. FlowCast preserves that order for routing and restores local playback in reverse order.

## Route Safety

FlowCast verifies the bundled routing helper, Voicemeeter Banana, `Voicemeeter Input`, and `Voicemeeter AUX Input` before enabling sharing. It records only routes it changes. On a normal stop or an unexpected disconnect, it disables the B1 music path and restores local playback when that setting is enabled.

If a selected application closes, a required endpoint disappears, or Windows changes a sharing route, FlowCast stops sharing, shows the reason and final session duration, and can show a local notification. Do not start a new share while the status says that attention is required.

## Session Controls

- Session timer: starts only after sharing actually begins and keeps running while sharing is muted.
- Timed stop: stops sharing and clears selections after the selected duration.
- End sound: optional confirmation after a normal stop or disconnect.
- System tray: open, start, mute or resume, stop, or exit FlowCast without reopening the main window.

## FlowCast Lyrics

FlowCast Lyrics is a separately packaged and separately updated Windows companion. Friends open only `FlowCast Lyrics.exe`; it automatically discovers FlowCast on the same Radmin VPN and does not ask for an IP address, port, pairing code, or QR code.

The companion shows one draggable, borderless glass capsule. Right-click the capsule to choose `調整玻璃` or `結束 FlowCast Lyrics`. `調整玻璃` opens a native Windows options bar for changing the glass parameters, which are saved with the capsule position. The private renderer is bundled inside the package, so FlowCast Lyrics does not require a separately installed Java runtime.

This version reports only whether it is looking for FlowCast or connected and waiting. It does not provide a lyric source yet and never invents lyric text.

## Updates And Publishing

FlowCast checks the latest GitHub release in the background after startup. It shows an update dialog only when a newer approved release exists. Choosing `立即更新` downloads the signed release asset, verifies SHA-256, safely stops an active share, installs the update, and restarts FlowCast.

Only approved major releases are published. Small local changes are not uploaded and therefore do not create an update notice for users. The FlowCast installer and FlowCast Lyrics package are separate products with separate update payloads. A FlowCast release must include:

- `FlowCast-Setup.exe`
- `FlowCast-Setup.exe.sha256` or a GitHub-provided SHA-256 digest

Create an installer locally without uploading build output:

```powershell
.\audio-share\scripts\publish-release.ps1 -OutputDirectory C:\release\FlowCast-1.1.0
```

## Build And Test

```powershell
dotnet test .\audio-share\AudioShare.sln --configuration Release
dotnet run --project .\audio-share\src\AudioShare.App\AudioShare.App.csproj
```
