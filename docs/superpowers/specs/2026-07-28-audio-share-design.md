# Audio Share Design

## Goal

Create a Windows desktop utility that lets the user select currently active audio applications, such as Chrome and NetEase Cloud Music, and route only those applications into the existing Voicemeeter B1 Discord input.

## Scope

- Discover active Windows audio sessions and present their process name, icon, and sharing state.
- Exclude Discord from selection and routing.
- Route selected processes to `Voicemeeter Input (VB-Audio Voicemeeter VAIO)`.
- Restore a process to the physical default playback endpoint when sharing is disabled or the utility exits.
- Verify the requested endpoint after every routing change and display errors.
- Provide per-process share toggles and a refresh control.
- Show whether Voicemod and Voicemeeter Banana are currently running.

## Out Of Scope

- Capturing individual Chrome tabs.
- Recording audio.
- Installing or managing virtual audio drivers.
- Replacing Voicemod, Voicemeeter, or Discord settings.
- Automatically starting or restarting Voicemod or Voicemeeter.
- Per-process gain controls in the first release.

## Data Flow

```text
Selected application -> Voicemeeter Input -> Voicemeeter B1 -> Discord input
Discord output -> physical headphones
Unselected applications -> physical default playback device
```

Discord is excluded from the selectable application list. Its output remains physical, so remote callers are never fed back into B1.

## Architecture

- `AudioSessionDiscovery`: enumerates active Windows audio sessions and maps them to processes.
- `EndpointRouter`: reads and writes the selected process playback endpoint using Windows audio policy APIs.
- `RouteCoordinator`: applies share and restore actions, stores only routes created by this utility, and verifies the result.
- `Desktop UI`: lists sessions, shows status, and lets the user toggle sharing.
- `AudioChainHealth`: checks for `Voicemod.exe` and `voicemeeterpro.exe` and reports their running state.

The target endpoint is discovered by endpoint ID and display name at startup. The physical restore endpoint is the current Windows default playback device, captured before the first route change.

## Failure Handling

- If Voicemeeter Input is unavailable, sharing controls are disabled with an actionable error.
- If Voicemod or Voicemeeter Banana is not running, the UI identifies the stopped application and explains that shared audio or B1 may be unavailable.
- If a process closes, its route is removed from the list without changing any other process.
- If routing fails, the UI retains the prior state and shows the Windows error.
- On normal utility exit, only routes created by this utility are restored.

## Acceptance Criteria

1. Chrome and NetEase Cloud Music appear while they have active audio sessions.
2. Selecting either application routes it to Voicemeeter Input and marks it shared.
3. Discord cannot be selected and continues to output to physical headphones.
4. A Discord call receives selected application audio through B1 but never receives the remote caller audio.
5. Unselecting an application or exiting the utility restores its playback endpoint.
6. Closing Voicemod or Voicemeeter Banana changes the corresponding health indicator within three seconds without changing audio settings.
