# Audio Share External Application Routing Design

## Goal

Let the user share audio only from explicitly selected, currently playing applications on Windows 10. A selected application identity is routed to `Voicemeeter Input`; every other discovered playback application identity is routed to `Voicemeeter AUX Input`. Selecting Chrome affects all concurrently running Chrome audio processes because Windows stores an output preference by application identity rather than PID.

## Constraints

- Keep the existing working bus isolation: `Voicemeeter Input` may feed B1, while `Voicemeeter AUX Input` feeds A1 only.
- Never route Discord, Voicemod, or Voicemeeter itself to the share bus.
- Do not change the global default playback device, audio drivers, or Discord's microphone configuration.
- Route applications by the Windows executable/application identity. The app must show that all matching processes are affected.
- The feature must work on the user's current Windows 10 19044 system.
- Bundle a pinned Python runtime and `winappaudiorouter` 1.1.1 in the installer. The package is MIT licensed and its attribution must ship with the product.
- The application must never download, replace, or execute a routing component from the network at runtime.
- The external router is experimental because it depends on Windows' undocumented audio policy path. It remains unavailable until its local health check succeeds.

## Considered Approaches

1. Manual Windows Volume Mixer routing: safe and already available, but does not enforce the selected-only rule.
2. Bundled third-party command-line router with restrictive redistribution terms: rejected because it cannot be included in a commercial product.
3. `winappaudiorouter` 1.1.1: chosen. Its MIT license permits commercial distribution with attribution, and it supports Windows 10 1803+ application endpoint routing through a Python API. The app owns the confirmation, route plan, transaction log, and restore policy.

## User Flow

1. Audio Share scans active render sessions and hides protected processes.
2. The user checks the applications to share, such as Chrome or NetEase Cloud Music.
3. The app checks that the bundled helper is intact and can enumerate the currently active `Voicemeeter Input` and `Voicemeeter AUX Input` endpoints.
4. The user presses `Apply selected audio routing`, reads the experimental notice, and confirms the displayed change list.
5. The helper sends selected application identities to Input and every discovered, unselected application identity to AUX.
6. The app shows each changed application identity, target endpoint, and rollback status.
7. The user presses `Restore this routing` to return the identities changed by the latest transaction to their previous endpoints. A later instance of the same executable follows the active Windows application rule until it is restored or changed by a later explicit Apply.

## Safety Model

### Protected Processes

`discord`, `discord.exe`, `voicemod`, `voicemod.exe`, `voicemeeter`, `voicemeeter.exe`, `voicemeeterpro`, and `voicemeeterpro.exe` are excluded from discovery, selection, and helper commands. Audio Share does not modify their routes; Discord must remain on the user's existing AUX/headphone route. A protected process can never receive an Input command.

### Transaction and Restore

Before a route command, the helper reads the previous endpoint preference for every target application identity. Audio Share validates the entire route plan before writing. If any write fails, it asks the helper to restore every successful prior write in reverse order. The UI retains the latest successful transaction and exposes `Restore this routing` for a user-initiated rollback.

No global default device is read or written. If either Voicemeeter endpoint is absent, ambiguous, inactive, or the helper health check fails, Audio Share makes no change and explains the reason.

### Application-Identity Ownership

Route ownership is bound to the Windows application identity used by its persisted output preference, not a PID. The UI must state this before confirmation and list every active matching process. Selecting an application is intent only until the user explicitly confirms Apply. Audio Share never applies a route to an application identity that was not in that confirmation.

## Architecture

### Core

The existing `AudioRoutingPolicy` remains the pure protected-process gate. A new pure route-plan builder receives discovered sessions, checked application identities, and resolved endpoint identifiers. It emits deterministic commands, rejects protected targets, and is unit tested without Windows audio hardware.

### External Routing Helper

A separately contained Python helper wraps the pinned `winappaudiorouter` package. It accepts only line-delimited JSON commands from Audio Share: `health`, `list-devices`, `get-route`, `set-route`, and `clear-route`. It must reject unknown commands, protected process names, missing device identifiers, and malformed input. It writes JSON responses only to standard output and diagnostic logs only to standard error. Audio Share verifies the helper's packaged hash before launching it and makes no routing change if the health check or endpoint checks fail.

### Application UI

The WPF app adds a clearly labelled experimental routing section. It shows selected and unselected route destinations before confirmation, disables Apply when the helper or either endpoint is unavailable, and logs the exact application identities changed. The existing `Open Windows Volume Mixer` action remains as the safe fallback.

## Failure Handling

- Helper integrity or health check failure: keep all routing unchanged and offer the manual mixer fallback.
- Endpoint missing or duplicate: keep all routing unchanged and report the endpoint name.
- Protected process in a plan: reject the plan before writing.
- Partial apply failure: restore successfully changed targets and show the final restore result.
- Restore failure: retain the transaction log and direct the user to the manual mixer; do not issue further automatic writes.

## Verification

1. Unit tests prove selected sessions map to Input, unselected sessions map to AUX, and protected sessions cannot map to Input.
2. Unit tests cover duplicate application identities, multiple active processes for one executable, endpoint lookup failures, and explicit restore.
3. Tests cover malformed helper output, helper timeout, non-zero exit, and a partial apply rollback.
4. Build and all existing tests pass with external routing disabled.
5. On the user's machine, run the helper health and endpoint listing commands only; they must not alter routing.
6. Only after an explicit user confirmation, run a controlled live test with one non-Discord music app and verify: music reaches B1, Discord friend audio remains audible locally, and friend audio never reaches B1.

## Non-Goals

- PID-specific routing or isolation between concurrent instances of the same executable.
- Capturing a process's audio directly.
- Automatically downloading third-party tools, runtimes, or drivers.
- Reconfiguring Voicemeeter, Voicemod, Discord, or Windows global playback settings.
