# Task 5 Report: Read-Only Health Summary

## RED

- Added `HealthSummaryTests` for missing Input and running Discord advisory behavior.
- Ran `dotnet test audio-share/AudioShare.sln -c Release --no-restore --filter FullyQualifiedName~HealthSummaryTests`.
- Result: failed with `CS0103` because `HealthSummary` and `HealthState` did not exist.

## GREEN

- Added Core `HealthSummary`, `HealthItem`, and `HealthState` with five independent checks.
- Added read-only `FlowCastHealthProbe`, which reads endpoint availability, the Windows default playback device, and Banana/Discord process state. Process handles are disposed.
- Replaced the single Banana label with five Apple-glass health chips: Banana, A1/default playback, Input, AUX, and Discord.
- Banana, Input, and AUX attention prevent starting a share through the existing availability decision. Discord remains advisory and only requests manual B1 confirmation.
- Focused tests: `2/2` passed.
- Full Release tests: `164/164` passed.
- `git diff --check` passed.

## Review Fix

- Added failing tests for Input missing while AUX remains ready, and AUX missing while Input remains ready.
- `FlowCastHealthProbe` now reports Input and AUX from separate endpoint checks; routing availability remains false unless both endpoints are present.
- Added an advisory-only `打开语音和视频` entry point on the Discord chip. It only opens `discord://-/settings/voice` and does not read or change Discord microphone settings.
- Focused tests: `7/7` passed.
- Full Release tests: `167/167` passed.
- `git diff --check` passed.
