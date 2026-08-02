# FlowCast

FlowCast is a Windows desktop app for sharing audio from selected applications in Discord. It detects applications that are actively producing sound, sends only the applications you select to the Voicemeeter B1 sharing path, and keeps unselected applications local.

## Requirements

- Windows 10 version 2004 or newer.
- Voicemeeter Banana installed and running.
- In Discord, set the microphone to `Voicemeeter Out B1` and the speaker to `Voicemeeter AUX Input`.

FlowCast never lets Discord, Voicemeeter, or its own helper be selected for sharing. Windows saves routing by application executable identity, so all active processes from the same application can be affected together.

## Use

1. Open Voicemeeter Banana and choose your headphones or speakers as A1.
2. Start playing audio in Chrome, NetEase Cloud Music, or another application.
3. Open FlowCast, select the programs you want friends to hear, then click `开始分享`.
4. Confirm the prompt. FlowCast then changes only the selected application routes.
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

## Updates And Publishing

FlowCast checks the latest GitHub release in the background after startup. It shows an update dialog only when a newer approved release exists. Choosing `立即更新` downloads the signed release asset, verifies SHA-256, safely stops an active share, installs the update, and restarts FlowCast.

Only approved major releases are published. Small local changes are not uploaded and therefore do not create an update notice for users. A release must include:

- `FlowCast-Setup.exe`
- `FlowCast-Setup.exe.sha256` or a GitHub-provided SHA-256 digest

Create an installer locally without uploading build output:

```powershell
.\audio-share\scripts\publish-release.ps1 -OutputDirectory C:\release\FlowCast-1.0.0
```

## Build And Test

```powershell
dotnet test .\audio-share\AudioShare.sln --configuration Release
dotnet run --project .\audio-share\src\AudioShare.App\AudioShare.App.csproj
```
