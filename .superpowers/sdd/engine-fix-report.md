# Audio Share Engine Fix Report

## Scope

Implemented only selected-source engine contract and lifecycle coverage. The async process-source factory contract is `ISelectedProcessSourceFactory.CreateAsync(AudioSession session, CancellationToken cancellationToken)`, and `SelectedSourceEngine.SynchronizeAsync` awaits it. Discord sessions remain rejected before factory creation.

Completed process sources are removed from the active source set and disposed during `MixOnceAsync`. Tests cover optional microphone reads, completed-source removal, PID reuse replacement, factory cancellation-token forwarding, Discord rejection, and exactly one writer call.

## Verification

- `dotnet test .\audio-share\AudioShare.sln --configuration Debug --filter FullyQualifiedName~SelectedSourceEngineTests`: 12 passed.
- `dotnet test .\audio-share\AudioShare.sln --configuration Debug`: 44 passed.

## Scope Protection

The pre-existing dirty changes in `AudioShare.Core/SelectedAudioMixer.cs`, `ReleaseUpdateParserTests.cs`, and `SelectedAudioMixerTests.cs` were preserved and excluded from the commit.
