# FlowCast Phase 1 Task 7 Report

## Source

- Base commit: `a6bfeca feat: add bounded FlowCast motion`
- Version set to `2.0.0`, `2.0.0.0`, and `2.0.0.0` in `AudioShare.App.csproj`.

## Verification

- Release tests: `168/168` passed with `dotnet test audio-share\\AudioShare.sln -c Release --no-restore`.
- Package output: `D:\\codexhome\\scratch\\flowcast-release-2.0.0`.
- Required assets created: `AudioShare-win-x64.zip`, `AudioShare-win-x64.zip.sha256`, and `FlowCast Setup.exe`.
- ZIP SHA-256: `c1faf50ab63bb32f41c79e9134b02eb18dfefb8c01276111cb1ccd28d1c5980d`.
- The declared SHA-256 exactly matches the ZIP hash.
- ZIP contains `AudioShare.App.exe` and the bundled `router-helper` runtime.
- Installed only to `%LocalAppData%\\Programs\\FlowCast` with the NSIS installer directory override.
- Installed executable SHA-256: `01aaf52a0e8d43a544f83e0cfe111a8f8512b6867dbb63ce7ad4c30813d4b62f`.
- Installed executable matches the published executable and was relaunched as version `2.0.0`.

## GitHub Release

`gh auth status` reported that no GitHub host is authenticated. No GitHub release or assets were created. Authenticate GitHub CLI with an account permitted to publish to `Rex11929282/audio-share-updates`, then upload only `AudioShare-win-x64.zip` and `AudioShare-win-x64.zip.sha256` to public release `v2.0.0`.

## Scope

No files in `audio-share/dist`, `audio-share/release`, or the scratch package output were committed.
