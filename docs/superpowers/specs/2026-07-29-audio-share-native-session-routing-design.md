# Audio Share Native Session Routing Design

## Goal

Let the user share audio only from explicitly selected, currently playing applications on Windows 10. A selected application identity is routed to `Voicemeeter Input`; every other discovered playback application identity is routed to `Voicemeeter AUX Input`. Selecting Chrome affects all concurrently running Chrome audio processes because Windows stores an output preference by application identity rather than PID.

## Constraints

- Keep the existing working bus isolation: `Voicemeeter Input` may feed B1, while `Voicemeeter AUX Input` feeds A1 only.
- Never route Discord, Voicemod, or Voicemeeter itself to the share bus.
- Do not change the global default playback device, audio drivers, or Discord's microphone configuration.
- Route applications by the Windows executable/application identity. The app must show that all matching processes are affected.
- The feature must work on the user's current Windows 10 19044 system without installing or redistributing third-party routing utilities.
- Windows does not expose a documented, stable Win10 API for this kind of per-application endpoint routing. The native router is therefore experimental and remains disabled until a local compatibility probe succeeds.

## Considered Approaches

1. Manual Windows Volume Mixer routing: safe and already available, but does not enforce the selected-only rule.
2. Bundled third-party command-line router: rejected because the available utility's commercial redistribution terms prohibit bundling it.
3. Clean-room native Windows policy adapter: chosen. It can enforce routing locally without a bundled dependency, but needs strict ABI validation, a feature gate, and rollback because the policy interface is not publicly supported.

## User Flow

1. Audio Share scans active render sessions and hides protected processes.
2. The user checks the applications to share, such as Chrome or NetEase Cloud Music.
3. The user presses `Apply selected audio routing` and confirms the displayed change list.
4. The router resolves the currently active `Voicemeeter Input` and `Voicemeeter AUX Input` endpoints.
5. It sends selected processes to Input and every discovered, unselected process to AUX.
6. It shows each changed process, target endpoint, and rollback status.
7. The user presses `Restore this routing` to return the identities changed by the latest transaction to their previous endpoints. A later instance of the same executable follows the active Windows application rule until it is restored or changed by a later explicit Apply.

## Safety Model

### Protected Processes

`discord`, `discord.exe`, `voicemod`, `voicemod.exe`, `voicemeeter`, `voicemeeter.exe`, `voicemeeterpro`, and `voicemeeterpro.exe` are excluded from discovery, selection, and native route commands. Discord is explicitly planned to AUX when it appears in an internal route plan; a protected process can never receive an Input command.

### Transaction and Restore

Before a route command, the router snapshots the previous endpoint preference for every target application identity. It validates the entire route plan before writing. If any write fails, it restores every successful prior write in reverse order. The UI retains the latest successful transaction and exposes `Restore this routing` for a user-initiated rollback.

No global default device is read or written. If either Voicemeeter endpoint is absent, ambiguous, inactive, or fails the compatibility probe, the router makes no change and explains the reason.

### Application-Identity Ownership

Route ownership is bound to the Windows application identity used by its persisted output preference, not a PID. The UI must state this before confirmation and list every active matching process. Selecting an application is intent only until the user explicitly confirms Apply. Audio Share never applies a route to an application identity that was not in that confirmation.

## Architecture

### Core

The existing `AudioRoutingPolicy` remains the pure protected-process gate. A new pure route-plan builder receives discovered sessions, checked application identities, and resolved endpoint identifiers. It emits deterministic commands, rejects protected targets, and is unit tested without Windows audio hardware.

### Windows Adapter

A separately contained experimental adapter owns Windows policy COM interop. It exposes probe, snapshot, apply, and restore operations behind an interface. It must not use code copied from GPL projects, invoke external executables, or guess an ABI at runtime. The adapter is unavailable unless its explicit local compatibility probe verifies the required interface and application-identity semantics before any route write.

### Application UI

The WPF app adds a clearly labelled experimental routing section. It shows selected and unselected route destinations before confirmation, disables Apply when scanning cannot identify both endpoints, and logs the exact application identities changed. The existing `Open Windows Volume Mixer` action remains as the safe fallback.

## Failure Handling

- Probe unavailable: keep all routing unchanged and offer the manual mixer fallback.
- Endpoint missing or duplicate: keep all routing unchanged and report the endpoint name.
- Protected process in a plan: reject the plan before writing.
- Partial apply failure: restore successfully changed targets and show the final restore result.
- Restore failure: retain the transaction log and direct the user to the manual mixer; do not issue further automatic writes.

## Verification

1. Unit tests prove selected sessions map to Input, unselected sessions map to AUX, and protected sessions cannot map to Input.
2. Unit tests cover duplicate application identities, multiple active processes for one executable, endpoint lookup failures, partial apply rollback, and explicit restore.
3. Build and all existing tests pass with the native feature disabled.
4. On the user's machine, run the compatibility probe only; it must not alter routing.
5. Only after an explicit user confirmation, run a controlled live test with one non-Discord music app and verify: music reaches B1, Discord friend audio remains audible locally, and friend audio never reaches B1.

## Non-Goals

- PID-specific routing or isolation between concurrent instances of the same executable.
- Capturing a process's audio directly.
- Automatically downloading third-party tools or drivers.
- Reconfiguring Voicemeeter, Voicemod, Discord, or Windows global playback settings.
