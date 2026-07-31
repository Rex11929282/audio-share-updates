# FlowCast 2.0 Final Fix Report

Baseline: `e298b5b`

## Fixed findings

1. Direct-Input safety now treats active sharing itself as a reset gate. Closing a non-routeable shared source still runs the verified stop path and disables both Input B1 and AUX B1 even when no recovery session or owned route transaction remains.
2. Share confirmation and route planning now share the same case-insensitive `ProcessName` selection identities. Identically named sessions can no longer be shown as AUX while the route plan sends them to Input.
3. A verified stop now assigns `SharingRouteState.LocalOnly`. Refreshing with no active routable sessions aggregates to `LocalOnly`, so the status and controls remain consistent.
4. Updates now require the packaged router helper and stage every package item. Replacement uses a same-volume staged application directory, preserves `Uninstall FlowCast.exe`, swaps the full directory, and rolls the original directory back if the swap fails.
5. Completed storyboards apply their final values, remove their WPF clocks, and leave `runningStoryboards`.

## Verification

- Focused Release tests: `48/48` passed.
- Full Release tests: `176/176` passed.
- Replacement PowerShell syntax parse: passed.
- Replacement transaction test: nested router helper updated, stale files removed, uninstaller preserved, no transaction directories left.
- `git diff --check`: passed.
- `audio-share/dist`, `audio-share/release`, caches, diagnostics, and other pre-existing untracked files are excluded from the fix commit.
