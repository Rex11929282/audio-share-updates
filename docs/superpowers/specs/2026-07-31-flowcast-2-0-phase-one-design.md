# FlowCast 2.0 First Phase Design

## Goal

Make selected-program sharing safer and easier to understand without changing the existing Voicemeeter Banana and Discord routing architecture.

## Included

### Program exclusions

- Add a persisted user exclusion list for process names.
- Excluded programs are never shown, selected, routed, or included in share summaries.
- The existing built-in protected programs remain permanently excluded.
- The program list provides an explicit action to exclude or restore an application.

### Share confirmation and route result

- Starting sharing opens a confirmation dialog before any route is changed.
- The dialog lists selected applications that will be routed to `Voicemeeter Input` and audible unselected applications that will remain on `Voicemeeter AUX Input`.
- After applying the route, show a compact success or failure result in the main status card.
- Verification continues to use the existing route verifier. Failure immediately closes B1 and restores local-only routing.

### B1 preview and safe stop

- Replace the B1 text-only presentation with a read-only B1 level meter fed by the existing Banana signal poller.
- The meter reports the level sent to Discord, not an application volume control.
- Stopping sharing must verify that B1 is disabled and that the applied route returned to local-only before the UI reports success.
- A failed stop remains actionable with a visible retry button and does not clear the error state.

### Health summary

- At startup and refresh, show independent status for Banana, A1 playback, Input endpoint, AUX endpoint, and whether Discord is running.
- Each status is `ready`, `attention`, or `unknown` and has a brief Chinese explanation.
- Discord does not expose a reliable public local interface for FlowCast to read the selected microphone. The Discord status therefore directs users to its voice settings for manual B1 confirmation and never claims the microphone was automatically verified.

### Motion system

- Add an optional `Reduce motion` preference.
- The normal mode includes a 0.9-second logo/sound-wave launch sequence, staggered program card entry, a one-time sharing route flow, B1 meter motion, and status-card transitions.
- Visible-window animation is capped at 30 FPS. Background timers and all visual animation stop when the window is minimized.
- Motion conveys a state change; it must not block a route action or conceal errors.

## Excluded

- Keyboard shortcuts.
- In-app volume controls, fading, or ducking.
- A required Voicemod integration.
- Automatically selecting a program or automatically starting sharing.
- Multiple output targets or changes to the Discord/Voicemod architecture.

## Architecture

- `AudioShare.Core`: process-name normalization, exclusion preferences, confirmation summary model, health-state model, and stop-verification policy.
- `AudioShare.Windows`: read-only checks for Voicemeeter endpoints and Discord microphone selection.
- `AudioShare.App`: preferences persistence, confirmation dialog, state binding, and WPF storyboards.
- Existing `RouteCoordinator`, `ApplicationRouteExecutor`, `VoicemeeterSharingBusService`, and `ApplicationRouteVerifier` remain the only components that change routing.

## Safety Rules

- No route operation runs before a positive confirmation.
- An excluded or protected process is never passed to routing code.
- Route verification failure calls the existing local-only recovery path before reporting an error.
- Stop success requires both B1 disabled and local-only route verification.
- A missing device or Banana process disables share start and identifies the failed check. Discord status remains an advisory check and never changes Discord settings.

## Tests

- Core tests cover exclusion persistence and normalization, confirmation summaries, health aggregation, and stop-verification decisions.
- Existing route planner, verifier, and safety tests continue to pass unchanged.
- WPF integration tests cover disabled controls, confirmation cancellation, successful confirmation, and reduce-motion preference application where practical.
- Manual runtime checks cover opening, selecting a playing app, confirming sharing, stopping sharing, an endpoint-change failure, and minimized performance behavior.

## Acceptance Criteria

- Users can exclude a visible app and it remains absent after restart.
- Before sharing, the UI clearly shows selected and local-only apps.
- Share only starts after confirmation and only reports success after route verification.
- Stop only reports success after B1 and route recovery are verified.
- The B1 meter is read-only and reflects Banana signal state.
- Motion is optional, bounded, and disabled while minimized.
