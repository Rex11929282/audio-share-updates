# FlowCast UI Polish Design

## Goal

Improve FlowCast's perceived responsiveness and visual hierarchy without changing any audio routing behavior.

## Design

- Add an eight-segment glass atmosphere bar to the primary status card. It reacts only while FlowCast has confirmed sharing and B1 reports output. It resets when sharing stops, the window is minimized, or reduced motion is enabled.
- Add a staged launch sequence for the atmosphere bar after the existing logo, status-card, and program-list entrance.
- Add press and release scale feedback to utility, primary, and danger buttons. Disabled buttons have no animation.
- Replace the always-visible health-chip row with a compact system-status control that expands the existing health chips on demand.

## Constraints

- Do not alter device, route, B1, Discord, timer, or update behavior.
- All motion is limited to 30 FPS, is skipped for reduced motion, and stops while minimized or closing.
- The atmosphere bar is visual-only and never treats its appearance as proof of sharing.

## Verification

- Release build has zero warnings and zero errors.
- Full test suite passes.
- `git diff --check` passes.
- Review confirms the bar resets whenever the confirmed sharing state is absent.
