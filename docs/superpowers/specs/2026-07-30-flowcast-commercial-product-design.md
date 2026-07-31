# FlowCast Commercial Product Design

## Goal

Turn Audio Share into FlowCast, a clear commercial Windows app for sharing selected music applications through the existing Voicemeeter Banana route.

## Scope

- Rename visible product identity to `FlowCast` with subtitle `音乐分享`.
- Add an original FlowCast logo: teal sound-wave and music-note mark pointing right.
- Replace the technical homepage emphasis with a clear sharing-state card.
- Detect whether any active, routable application is actually routed to Voicemeeter Input / B1.
- Enable `停止分享，只自己听` only when a sharing route exists; disable it with an explanation when every active routable application is already on AUX.
- Package a Windows `FlowCast Setup.exe` with uninstall, Start menu shortcut, and desktop shortcut.
- Retain the existing update feed checks and add background package download plus an explicit `立即更新` action that installs after FlowCast exits.

## Explicitly Out of Scope

- No in-app volume controls.
- No change to existing application routing behavior, device settings, Voicemeeter settings, Discord settings, or Voicemod behavior.
- No promise of Windows reputation trust without a code-signing certificate supplied by the publisher.

## State Rules

- `正在分享`: at least one active eligible application has either Console or Multimedia route equal to Voicemeeter Input.
- `只自己听到`: all active eligible applications have both roles equal to Voicemeeter AUX Input.
- `未确认`: route helper or endpoint detection is unavailable; do not enable Stop Sharing.
- The stop button is enabled only in `正在分享`.

## UI

- Use a warm white surface, ink-blue text, teal primary action, and coral status alert.
- Header: logo, FlowCast, `音乐分享`, tutorial, refresh, and update status.
- Status card: clear state label, one-sentence explanation, and safe next action.
- Application cards: selected state is described as `分享给朋友`; unselected state as `只自己听到`.
- Advanced details retain B1/AUX names for troubleshooting without putting them in the primary flow.

## Verification

- Tests cover each routing-state classification and stop-button availability.
- Tests cover FlowCast identity and no-volume-controls scope.
- Release build completes with no warnings or errors.
- Package creates `FlowCast Setup.exe` and update files.
- Pi verifies the main visual states without applying a route or changing an audio setting.
