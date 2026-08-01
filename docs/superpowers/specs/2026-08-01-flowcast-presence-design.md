# FlowCast Presence Design

## Goal
Make FlowCast feel more responsive at launch and easier to control while minimized without changing audio-routing behavior.

## Scope
- Use a three-second, visible launch sequence: logo, a glass light sweep, then a calm ready-state pulse. It never blocks controls.
- Show a B1 self-test only from the existing B1 meter reading while sharing. It must never claim Discord delivery is confirmed.
- Minimize to the Windows notification area with Show FlowCast and Stop Sharing, Only Me actions.

## Safety
- Launch motion is disabled by the existing reduce-motion preference and stops while minimized.
- A self-test reports only `B1 has signal` or `waiting for selected program audio`.
- The tray stop action reuses the normal stop-sharing confirmation path.
- Closing FlowCast remains a real close and keeps the existing reset-to-local-only flow.

## Verification
- Unit tests continue to pass.
- Release build compiles with zero warnings.
- Manual runtime inspection confirms tray show/restore and that the status text matches B1 activity.
