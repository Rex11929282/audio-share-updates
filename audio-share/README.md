# Audio Share

`Audio Share` helps you find applications that are currently playing audio and prepare them for sharing in Discord. It does not change Windows audio routing, Voicemeeter, Voicemod, or Discord settings.

## Before Using

Keep the existing working audio chain:

- Discord input: `Voicemeeter Out B1`.
- Discord and normal playback: `Voicemeeter AUX Input` to A1 headphones only.
- Music that should be shared: main `Voicemeeter Input` to A1 and B1.
- Voicemod microphone: B1.

## Use

1. Start Voicemod and Voicemeeter Banana.
2. Start playing audio in Chrome, NetEase Cloud Music, or another application.
3. Open Audio Share and select that application.
4. Windows Volume Mixer opens. Set that application's output to `Voicemeeter Input`.
5. Keep Discord excluded. It must remain on AUX/headphones so callers never hear themselves.

The selection checkmark is only stored inside Audio Share. Clearing it does not change the application's Windows output device. Change the device in Windows Volume Mixer whenever you need to stop sharing.

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
