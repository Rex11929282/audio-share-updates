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

## Publish

Publish the Voicemod edition (the default) with Voicemod and Voicemeeter Banana status:

```powershell
dotnet publish .\audio-share\src\AudioShare.App\AudioShare.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true --output .\AudioShare-Voicemod-win-x64
```

Publish the music-only edition with Voicemeeter Banana status but no Voicemod status or check:

```powershell
dotnet publish .\audio-share\src\AudioShare.App\AudioShare.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:RequireVoicemod=false --output .\AudioShare-MusicOnly-win-x64
```
