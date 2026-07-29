# Audio Share

`Audio Share` helps you find applications that are currently playing audio and prepare them for sharing in Discord. It does not change Windows audio routing, Voicemeeter, Voicemod, or Discord settings.

## Selected-Source Engine

This build contains the tested selected-source mixer core. It does not install a virtual microphone driver and does not yet capture live application audio. It cannot be used as a Discord input until the separately signed driver and Windows process-loopback adapter are released.

Audio Share does not change an application's Windows output device; the user must choose the device manually in Windows Volume Mixer.

## Before Using

Keep the existing working audio chain:

- Discord input: `Voicemeeter Out B1`.
- Discord and normal playback: `Voicemeeter AUX Input` to A1 headphones only.
- Music that should be shared: main `Voicemeeter Input` to A1 and B1.
- Voicemod microphone: B1.

## Use

1. Start Voicemod and Voicemeeter Banana.
2. Start playing audio in Chrome, NetEase Cloud Music, or another application.
3. Open Audio Share and select that application. Selection only stores your intent.
4. Click `Set up selected apps` to open Windows Volume Mixer, then set that application's output to `Voicemeeter Input`.
5. Keep Discord excluded. It must remain on AUX/headphones so callers never hear themselves.

The selection checkmark is only stored inside Audio Share. Clearing it does not change the application's Windows output device. Change the device in Windows Volume Mixer whenever you need to stop sharing.

## Experimental External Routing

The experimental controls are disabled until a refresh verifies the integrity and health of the bundled routing helper and finds exactly one `Voicemeeter Input` share-bus endpoint and one `Voicemeeter AUX Input` local-only endpoint. The helper is packaged with the release; Audio Share never downloads a routing runtime while it is running.

Selecting applications and refreshing only discover state. They never write an audio route. `Apply selected audio routing` first shows the selected-to-Input and unselected-to-AUX process counts and requires an explicit confirmation. The transaction routes selected supported applications to `Voicemeeter Input` and other supported active applications to `Voicemeeter AUX Input`.

Discord, Voicemod, Voicemeeter, and VoicemeeterPro are always excluded. Routing applies at application identity scope, so selecting an application such as Chrome affects all concurrently active Chrome audio processes. After a successful Apply, use `Restore this routing` and confirm again to restore only the transaction created during the current app run. A failed or cancelled Apply never enables Restore.

The manual `Set up selected apps` workflow remains available when the helper is unavailable or when you prefer to manage devices yourself. Do not perform a live routing test without explicit user consent. After consent, test only one non-Discord music app and then restore it before testing anything else.

## Build And Run

```powershell
dotnet test .\audio-share\AudioShare.sln --configuration Debug
dotnet run --project .\audio-share\src\AudioShare.App\AudioShare.App.csproj
```

## Updates And Publishing

The app checks `https://api.github.com/repos/Rex11929282/audio-share-updates/releases/latest` after startup. It only offers an update when a newer GitHub Release contains both HTTPS assets with these exact names:

- `AudioShare-win-x64.zip`
- `AudioShare-win-x64.zip.sha256`

It never installs automatically on first detection. Selecting `立即更新` downloads both assets, verifies SHA-256, then replaces and relaunches the portable executable after the current process exits. A failed validation leaves the running application unchanged.

Create release assets locally without uploading source code or artifacts:

```powershell
.\audio-share\scripts\publish-release.ps1 -OutputDirectory C:\release\audio-share-1.0.0
```

Upload only the two generated assets to a GitHub Release in the public `audio-share-updates` repository. The helper does not upload anything.
