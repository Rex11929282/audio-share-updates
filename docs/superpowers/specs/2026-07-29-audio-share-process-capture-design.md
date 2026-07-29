# Audio Share Process-Capture Design

## Goal

Create a commercial Windows audio-sharing product where only selected applications and the user's microphone reach Discord. If Chrome, NetEase Cloud Music, a browser tab, or any other application is not selected, its audio must not enter the shared output.

## Supported Environment

- Windows 10 build 20348 or later, x64.
- The first commercial architecture must not depend on Voicemeeter or Voicemod.
- The existing Voicemeeter AUX setup remains untouched during development and can continue to handle local Discord playback.

## User Flow

1. Audio Share lists processes currently rendering audio.
2. The user selects one or more processes to share.
3. Audio Share captures only the selected process trees and the selected physical or Voicemod microphone.
4. Audio Share mixes these sources into `Audio Share Virtual Microphone`.
5. Discord uses `Audio Share Virtual Microphone` as its input device.
6. Clearing a selection stops that process from contributing to the mix immediately.

## Audio Boundaries

| Source | Selected | Audio Share Virtual Microphone | Local playback |
| --- | --- | --- | --- |
| Chrome or another supported process | Yes | Included | Remains on its normal Windows output |
| Chrome or another supported process | No | Excluded | Remains on its normal Windows output |
| User microphone | Enabled | Included | Optional local monitoring only |
| Discord | Always excluded | Excluded | Normal local playback only |
| System or unidentified audio | Always excluded | Excluded | Normal local playback only |

The engine uses Windows process-loopback capture for each selected process tree. It must never obtain a whole-endpoint loopback stream, because that would include unselected applications.

## Components

### Process Capture Service

Creates and owns a process-loopback capture session for every selected process identity. A process identity contains PID, process start time, and executable name so that a reused PID cannot inherit a previous selection.

When a selected process exits, capture stops and its source is removed. When a selected process has no render stream, it contributes silence rather than causing the entire mixer to fail.

### Microphone Capture Service

Captures one user-selected microphone endpoint. The initial UI offers physical microphones and the Voicemod virtual microphone when installed. Only one microphone source is active at a time.

### Mixer and Monitor

Converts all sources to one internal float PCM format, mixes selected sources with clipping protection, and sends the resulting stream to the virtual microphone. Local monitoring is separate from shared output and is disabled by default to prevent feedback.

### Virtual Microphone Driver

Exposes a capture endpoint named `Audio Share Virtual Microphone`. The driver accepts the mixed PCM stream from the Audio Share engine and provides it to Discord or another communication application.

The production installer includes the driver, uninstaller, version metadata, and license notices. Release builds require a code-signing process suitable for the Windows driver installation path before sale.

### Safety Policy

- `discord` and `discord.exe` cannot be selected or captured.
- The engine excludes its own process and the virtual-driver host process.
- Stopping capture, unselecting a process, closing the app, or a capture failure removes that source from the virtual microphone.
- No component changes Windows default playback devices, Voicemeeter buses, Voicemod settings, or per-application endpoint assignments.

## Failure Handling

- Unsupported Windows builds show a blocking compatibility message and do not start capture.
- A missing virtual microphone driver shows an install/repair action; it does not silently fall back to a system microphone.
- A single process capture failure shows the affected process as unavailable while other selected sources remain active.
- Device removal or format changes restart only the affected capture source after validating the new endpoint.

## Testing

- Unit tests cover selection ownership, PID reuse, Discord/self exclusion, source removal, and mixer inclusion/exclusion rules.
- Integration tests use deterministic generated PCM sources to prove that only selected sources reach the virtual-output writer.
- Hardware validation checks microphone plus selected Chrome/NetEase audio in Discord, then verifies an unselected playing application is absent.
- A final manual test verifies that a remote Discord participant cannot hear their own voice echoed back.

## Delivery Phases

1. Add the engine contracts, process-loopback proof of concept, and selection/mixer tests without touching live routing.
2. Implement the virtual microphone driver prototype and test it on a dedicated machine or VM.
3. Integrate driver installation, signed packaging, updates, telemetry-free diagnostics, and commercial release checks.

## Non-Goals for Version 1

- Automatic manipulation of Windows per-app output-device settings.
- Capturing Discord, whole-system audio, unknown/system sessions, or protected content.
- Replacing Voicemod voice effects.
- Silently installing or modifying audio drivers.
