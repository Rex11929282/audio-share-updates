# Audio Share Design

## Goal

Create a Windows utility that shows applications with active audio, lets the user mark the applications they intend to share, and opens Windows Volume Mixer so the user can safely select `Voicemeeter Input` themselves.

## Scope

- Discover active render audio sessions and show their process and display names.
- Exclude Discord from selection.
- Keep local share-selection state and provide a refresh control.
- Open `ms-settings:apps-volume` for the user to select the output endpoint in Windows.
- Show whether Voicemod and Voicemeeter Banana are running.

## Out Of Scope

- Writing or restoring a per-application Windows playback endpoint.
- Capturing individual browser tabs, recording, driver management, or gain control.
- Changing Voicemod, Voicemeeter, or Discord settings.
- Starting or restarting any audio application.

## Data Flow

```text
Selected application -- user selects Voicemeeter Input in Windows Volume Mixer -->
Voicemeeter main input --> B1 --> Discord input

Discord/general playback --> Voicemeeter AUX Input --> A1 headphones only
```

Discord is excluded from the selectable list, so the remote caller is not sent to B1.

## Architecture

- `AudioSessionDiscovery` finds active Windows audio sessions.
- `RouteCoordinator` stores only local selected process IDs and rejects Discord.
- `VolumeMixerLauncher` opens the Windows Volume Mixer settings page.
- `AudioChainHealth` observes whether Voicemod and Voicemeeter Banana processes are running.
- The WPF UI lists sessions, exposes selection, and gives the user the native Windows step.

## Failure Handling

- If Voicemeeter Input is absent, display an actionable setup warning.
- If Voicemod or Banana is not running, display which application is stopped without changing it.
- If a process exits, remove it from the list and local selection.
- The only system action is opening the Windows settings page.

## Acceptance Criteria

1. Chrome and NetEase appear while actively playing audio.
2. Selecting either application marks it locally selected and opens Windows Volume Mixer.
3. Discord is non-selectable and its output continues through AUX to headphones only.
4. The app cannot modify an application playback endpoint or Voicemeeter configuration.
5. Voicemod and Banana process status refreshes within three seconds.
