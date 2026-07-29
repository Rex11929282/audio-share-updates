# Audio Share Selected-Source Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a testable Audio Share engine that emits only explicitly selected application audio and the enabled microphone to an abstract virtual-microphone writer.

**Architecture:** Keep process selection and PCM mixing in `AudioShare.Core`, independent from Windows capture and drivers. `AudioShare.Engine` owns source lifecycle: it starts sources only for selected process identities, removes them immediately when deselected or exited, and delivers the mixer output to one output writer. The existing WPF application remains a selector until the later native-capture and virtual-driver plan is approved and implemented.

**Tech Stack:** .NET 8, C#, xUnit, WPF, existing NAudio 2.2.1 dependency.

## Global Constraints

- Windows 10 build 20348 or later is the minimum for the future process-loopback implementation.
- Do not change Windows default playback devices, Voicemeeter buses, Voicemod settings, Discord settings, or per-app endpoint assignments.
- `discord` and `discord.exe` are never selectable, capturable, or mixable.
- Use PID, process start time, and executable name as one process identity.
- First-phase output is an in-memory writer only; it must not claim to create a real virtual microphone.
- Every production behavior starts with a failing xUnit test.

---

## File Structure

- Create `audio-share/src/AudioShare.Core/AudioFormat.cs`: immutable PCM format definition.
- Create `audio-share/src/AudioShare.Core/AudioFrame.cs`: timestamped interleaved float PCM data.
- Create `audio-share/src/AudioShare.Core/IAudioSource.cs`: asynchronous source contract.
- Create `audio-share/src/AudioShare.Core/IAudioOutputWriter.cs`: output contract for a future virtual microphone writer.
- Create `audio-share/src/AudioShare.Core/SelectedAudioMixer.cs`: selected-source plus microphone mixer.
- Create `audio-share/src/AudioShare.Engine/AudioShare.Engine.csproj`: orchestration project.
- Create `audio-share/src/AudioShare.Engine/SelectedSourceEngine.cs`: source lifecycle coordinator.
- Create `audio-share/src/AudioShare.Engine/SelectedSourceEngineOptions.cs`: microphone-enabled state and output format.
- Modify `audio-share/AudioShare.sln`: add the engine project and test reference.
- Modify `audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj`: reference `AudioShare.Engine`.
- Create `audio-share/tests/AudioShare.Core.Tests/SelectedAudioMixerTests.cs`: pure PCM mixing tests.
- Create `audio-share/tests/AudioShare.Core.Tests/SelectedSourceEngineTests.cs`: source-selection lifecycle tests using deterministic sources.
- Modify `audio-share/tests/AudioShare.Core.Tests/RouteCoordinatorTests.cs`: expose selected identities for engine handoff.
- Modify `audio-share/src/AudioShare.Core/RouteCoordinator.cs`: add a read-only selected-process snapshot API.

## Interfaces

```csharp
public sealed record AudioFormat(int SampleRate, int Channels)
{
    public int SamplesPerFrame => Channels;
}

public sealed record AudioFrame(AudioFormat Format, long TimestampTicks, float[] Samples)
{
    public int FrameCount => Samples.Length / Format.SamplesPerFrame;
}

public interface IAudioSource : IAsyncDisposable
{
    string SourceId { get; }
    Task<AudioFrame?> ReadAsync(CancellationToken cancellationToken);
}

public interface IAudioOutputWriter : IAsyncDisposable
{
    Task WriteAsync(AudioFrame frame, CancellationToken cancellationToken);
}

public interface ISelectedProcessSourceFactory
{
    Task<IAudioSource> CreateAsync(AudioSession session, CancellationToken cancellationToken);
}

public sealed record SelectedSourceEngineOptions(AudioFormat OutputFormat, bool IncludeMicrophone);
```

`SelectedSourceEngine` consumes the selected `AudioSession` objects, an `ISelectedProcessSourceFactory`, an optional microphone `IAudioSource`, and one `IAudioOutputWriter`. Its `SynchronizeAsync` method starts newly selected process sources and disposes sources that are deselected or no longer active.

## Task 1: Add Audio Frame Contracts

**Files:**

- Create: `audio-share/src/AudioShare.Core/AudioFormat.cs`
- Create: `audio-share/src/AudioShare.Core/AudioFrame.cs`
- Create: `audio-share/src/AudioShare.Core/IAudioSource.cs`
- Create: `audio-share/src/AudioShare.Core/IAudioOutputWriter.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/AudioFrameTests.cs`

**Consumes:** none.

**Produces:** `AudioFormat`, `AudioFrame`, `IAudioSource`, and `IAudioOutputWriter` for Tasks 2 and 3.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void AudioFrame_RejectsSamplesThatDoNotFillWholeFrames()
{
    var format = new AudioFormat(48_000, 2);

    Assert.Throws<ArgumentException>(() => new AudioFrame(format, 0, [0.1f, 0.2f, 0.3f]));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioFrameTests`

Expected: FAIL because `AudioFormat` and `AudioFrame` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace AudioShare.Core;

public sealed record AudioFormat(int SampleRate, int Channels)
{
    public int SamplesPerFrame => Channels;
}

public sealed record AudioFrame(AudioFormat Format, long TimestampTicks, float[] Samples)
{
    public int FrameCount => Samples.Length / Format.SamplesPerFrame;
}
```

Add constructor validation so a null format, sample rate below one, channel count below one, null samples, and a non-divisible sample count throw `ArgumentException` or `ArgumentNullException` before exposing `FrameCount`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~AudioFrameTests`

Expected: PASS with one test.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Core/AudioFormat.cs audio-share/src/AudioShare.Core/AudioFrame.cs audio-share/src/AudioShare.Core/IAudioSource.cs audio-share/src/AudioShare.Core/IAudioOutputWriter.cs audio-share/tests/AudioShare.Core.Tests/AudioFrameTests.cs
git commit -m "feat: add audio frame contracts"
```

## Task 2: Mix Only Allowed Sources

**Files:**

- Create: `audio-share/src/AudioShare.Core/SelectedAudioMixer.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/SelectedAudioMixerTests.cs`

**Consumes:** `AudioFormat` and `AudioFrame` from Task 1.

**Produces:** `SelectedAudioMixer.Mix(AudioFormat format, long timestampTicks, IReadOnlyList<AudioFrame> selectedProcessFrames, AudioFrame? microphoneFrame)` for Task 3.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Mix_UsesOnlySelectedProcessFramesAndEnabledMicrophoneFrame()
{
    var format = new AudioFormat(48_000, 2);
    var mixer = new SelectedAudioMixer();

    var mixed = mixer.Mix(
        format,
        100,
        [new AudioFrame(format, 100, [0.2f, 0.2f])],
        new AudioFrame(format, 100, [0.3f, 0.3f]));

    Assert.Equal([0.5f, 0.5f], mixed.Samples);
}
```

Add a second test where `selectedProcessFrames` is empty and `microphoneFrame` is null; expected output is a zero-length silent frame with the requested format. Add a third test asserting a source frame with another format throws `ArgumentException`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~SelectedAudioMixerTests`

Expected: FAIL because `SelectedAudioMixer` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed class SelectedAudioMixer
{
    public AudioFrame Mix(
        AudioFormat format,
        long timestampTicks,
        IReadOnlyList<AudioFrame> selectedProcessFrames,
        AudioFrame? microphoneFrame)
    {
        // Validate every included frame has the requested format, sum only those frames,
        // then clamp every output sample to [-1f, 1f].
    }
}
```

The output sample count comes from the first included frame. If no frame exists, return an empty sample array with the requested format. Never accept an unselected source argument in this API.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~SelectedAudioMixerTests`

Expected: PASS for all mixer cases.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Core/SelectedAudioMixer.cs audio-share/tests/AudioShare.Core.Tests/SelectedAudioMixerTests.cs
git commit -m "feat: mix selected audio sources"
```

## Task 3: Expose Safe Selected Process Snapshots

**Files:**

- Modify: `audio-share/src/AudioShare.Core/RouteCoordinator.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/RouteCoordinatorTests.cs`

**Consumes:** existing `AudioSession` and the existing process identity behavior.

**Produces:** `IReadOnlyList<AudioSession>` through `RouteCoordinator.GetSelectedSessions`, which is safe for the engine project to consume directly.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GetSelectedSessions_ReturnsOnlyActiveNonDiscordSelections()
{
    var coordinator = new RouteCoordinator();
    var chrome = new AudioSession(10, 100, "chrome.exe", "Chrome", true);
    var discord = new AudioSession(11, 101, "discord.exe", "Discord", true);

    await coordinator.ShareAsync(chrome, CancellationToken.None);
    await coordinator.ShareAsync(discord, CancellationToken.None);

    var selected = coordinator.GetSelectedSessions([chrome, discord]);

    Assert.Equal([chrome], selected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~GetSelectedSessions`

Expected: FAIL because `GetSelectedSessions` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public IReadOnlyList<AudioSession> GetSelectedSessions(IEnumerable<AudioSession> activeSessions) =>
    activeSessions
        .Where(session => !DeniedProcessNames.Contains(session.ProcessName))
        .Where(IsSelected)
        .ToArray();
```

Keep the existing PID-reuse identity check. Do not expose mutable dictionaries or add endpoint-routing calls.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~RouteCoordinatorTests`

Expected: PASS for all route-coordinator tests.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/src/AudioShare.Core/RouteCoordinator.cs audio-share/tests/AudioShare.Core.Tests/RouteCoordinatorTests.cs
git commit -m "feat: expose selected process snapshots"
```

## Task 4: Orchestrate Selected Sources Into One Writer

**Files:**

- Create: `audio-share/src/AudioShare.Engine/AudioShare.Engine.csproj`
- Create: `audio-share/src/AudioShare.Engine/SelectedSourceEngineOptions.cs`
- Create: `audio-share/src/AudioShare.Engine/SelectedSourceEngine.cs`
- Modify: `audio-share/AudioShare.sln`
- Modify: `audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj`
- Create: `audio-share/tests/AudioShare.Core.Tests/SelectedSourceEngineTests.cs`

**Consumes:** contracts from Task 1, mixer from Task 2, and selected sessions from Task 3.

**Produces:** `SelectedSourceEngine.SynchronizeAsync` and `SelectedSourceEngine.MixOnceAsync` for the later Windows process-loopback adapter and virtual-microphone writer.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task MixOnceAsync_WritesOnlyFramesCreatedForCurrentlySelectedProcesses()
{
    var chrome = new AudioSession(20, 200, "chrome.exe", "Chrome", true);
    var netease = new AudioSession(21, 201, "cloudmusic.exe", "NetEase", true);
    var factory = new FakeSourceFactory((session, format) =>
        session.ProcessId == chrome.ProcessId
            ? new FakeAudioSource("chrome", format, [0.2f, 0.2f])
            : new FakeAudioSource("netease", format, [0.7f, 0.7f]));
    var writer = new RecordingWriter();
    await using var engine = new SelectedSourceEngine(factory, writer, null, new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

    await engine.SynchronizeAsync([chrome], CancellationToken.None);
    await engine.MixOnceAsync(CancellationToken.None);

    Assert.Equal([0.2f, 0.2f], Assert.Single(writer.Frames).Samples);
}
```

Add a second test that synchronizes from `[chrome]` to `[]`, then asserts the Chrome fake source was disposed and the next write is silent. Add a third test that passes Discord to `SynchronizeAsync` and asserts the factory was never called for it.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~SelectedSourceEngineTests`

Expected: FAIL because `SelectedSourceEngine` and its contracts do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed class SelectedSourceEngine : IAsyncDisposable
{
    public Task SynchronizeAsync(IReadOnlyList<AudioSession> selectedSessions, CancellationToken cancellationToken);
    public Task MixOnceAsync(CancellationToken cancellationToken);
    public ValueTask DisposeAsync();
}
```

Store active sources by the same `(ProcessId, ProcessStartUtcTicks, ProcessName)` identity used by `RouteCoordinator`. `SynchronizeAsync` rejects Discord names before source creation, creates sources for new identities, disposes removed identities, and leaves unchanged identities running. `MixOnceAsync` reads one frame from each active process source and the microphone only when `IncludeMicrophone` is true, passes only those returned frames to `SelectedAudioMixer`, and writes exactly one mixed frame.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~SelectedSourceEngineTests`

Expected: PASS for selected-only, deselection, and Discord-exclusion cases.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/AudioShare.sln audio-share/src/AudioShare.Engine audio-share/tests/AudioShare.Core.Tests/AudioShare.Core.Tests.csproj audio-share/tests/AudioShare.Core.Tests/SelectedSourceEngineTests.cs
git commit -m "feat: add selected source engine"
```

## Task 5: Verify the Phase-One Boundary

**Files:**

- Modify: `audio-share/README.md`
- Modify: `audio-share/COMMERCIAL.md`

**Consumes:** completed engine from Task 4.

**Produces:** accurate documentation that distinguishes the tested selection/mixer core from the not-yet-installed driver.

- [ ] **Step 1: Write the failing documentation assertion**

Add a test in `audio-share/tests/AudioShare.Core.Tests/ReleaseUpdateParserTests.cs` that reads `README.md` and asserts it contains `does not install a virtual microphone driver`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug --filter FullyQualifiedName~ReleaseUpdateParserTests`

Expected: FAIL because the README does not yet state the phase-one limitation.

- [ ] **Step 3: Write minimal documentation**

Add this exact paragraph to `README.md` under a `## Selected-Source Engine` heading:

```markdown
This build contains the tested selected-source mixer core. It does not install a virtual microphone driver and does not yet capture live application audio. It cannot be used as a Discord input until the separately signed driver and Windows process-loopback adapter are released.
```

Add the same limitation to `COMMERCIAL.md` so a commercial customer is not promised a driver that is not bundled.

- [ ] **Step 4: Run full verification**

Run: `dotnet test .\\audio-share\\AudioShare.sln --configuration Debug`

Expected: PASS for all tests.

Run: `dotnet build .\\audio-share\\AudioShare.sln --configuration Release`

Expected: `0` warnings and `0` errors.

- [ ] **Step 5: Commit**

```powershell
git add audio-share/README.md audio-share/COMMERCIAL.md audio-share/tests/AudioShare.Core.Tests/ReleaseUpdateParserTests.cs
git commit -m "docs: define selected source engine boundary"
```

## Separate Follow-Up Plans

The following are intentionally not part of this plan because each needs its own test environment and release review:

1. A native Windows process-loopback adapter using the official `ActivateAudioInterfaceAsync` application-loopback path, tested on Windows build 20348 or later.
2. A signed virtual microphone driver with an installer, uninstaller, device-removal recovery, and clean-machine validation.
3. End-to-end Discord verification with a remote participant and a final commercial installer/signing release checklist.

## Self-Review

- Spec coverage: Tasks 1-4 cover source identity, selected-only mixing, microphone inclusion, Discord exclusion, and immediate source removal. Task 5 prevents the current portable build from misrepresenting these core changes as a shipping virtual microphone. The driver, installer, and signing requirements are correctly isolated as separate plans because no unsigned driver should be installed on the current workstation.
- Placeholder scan: no `TODO`, `TBD`, or undefined implementation directives remain.
- Type consistency: `AudioFormat`, `AudioFrame`, `IAudioSource`, `IAudioOutputWriter`, `ISelectedProcessSourceFactory`, `SelectedSourceEngineOptions`, and `SelectedSourceEngine` are introduced before later tasks consume them.
