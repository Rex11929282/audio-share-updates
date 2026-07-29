# Audio Share Routing Manager Design

## Goal

Make Audio Share a safe Windows 10 LTSC 19044 companion for the existing
Voicemeeter setup. It helps the user identify which currently-audible
applications are intended for sharing, while making it explicit that only the
user can assign an application's Windows output device.

## Scope

- Show active audio applications and retain their share selection.
- Permanently exclude Discord, Voicemod, and Voicemeeter processes from the
  share list and status cards.
- Give selected applications a clear `Set up sharing` action that opens the
  Windows Volume Mixer.
- Give unselected applications a clear `Keep private` state and no sharing
  action.
- Show the required target device: `Voicemeeter Input` for selected audio and
  `Voicemeeter AUX Input` for Discord and ordinary playback.
- Explain that Audio Share does not change devices automatically.

## Non-Goals

- Do not capture live application audio, create a virtual microphone, install
  a driver, or change Windows audio routing.
- Do not change Voicemeeter, Voicemod, Discord, default playback devices, or
  per-application device assignments.
- Do not claim that a selection checkmark alone sends audio to Discord.

## User Flow

1. The user starts an application such as Chrome or NetEase Cloud Music.
2. Audio Share lists it as an active audio application.
3. The user checks the application to mark it for sharing.
4. Audio Share offers `Set up sharing`, opens Windows Volume Mixer, and shows
   the exact destination the user must select: `Voicemeeter Input`.
5. If the user clears the selection, Audio Share removes the sharing action
   and tells the user to keep the application on its normal/AUX device.
6. Discord is never selectable and is always described as local AUX playback
   only, preventing remote callers from hearing themselves.

## Safety Rules

- A process is selected only when its full process identity matches the saved
  selection; a reused PID cannot inherit a previous selection.
- `discord`, `discord.exe`, `voicemod`, `voicemeeter`, and
  `voicemeeterpro` are excluded case-insensitively before display.
- The UI always labels Windows Volume Mixer as a manual step.
- If no applications are selected, the setup button is disabled.

## Verification

- Unit tests verify excluded processes are not displayed or selectable.
- Unit tests verify the selected state is keyed by process identity and clears
  when the process is no longer active.
- UI tests verify the setup action is unavailable without a selection and
  shows `Voicemeeter Input` guidance only for selected applications.
- Manual verification keeps Discord on `Voicemeeter AUX Input`, starts music
  on `Voicemeeter Input`, and confirms a remote Discord participant does not
  hear their own voice.

## Compatibility

This design supports the current Windows 10 build 19044 because it does not
use the newer Windows process-loopback API. A future native selected-process
capture module requires Windows 10 build 20348 or later and remains a separate
project.
