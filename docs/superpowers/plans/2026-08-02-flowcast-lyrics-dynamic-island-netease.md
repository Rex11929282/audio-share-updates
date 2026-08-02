# FlowCast Lyrics Dynamic Island and NetEase Standalone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone Dynamic Island-style FlowCast Lyrics executable that synchronizes real NetEase Cloud Music lyrics locally and automatically relays the same lyric state to friends who open only FlowCast Lyrics in the same Radmin VPN room.

**Architecture:** A Windows media-session adapter supplies NetEase playback snapshots to a small resolution, lyric, and synchronization pipeline. A remote-first coordinator feeds one honest island snapshot to the existing WPF/WebView2 overlay, while a versioned Radmin transport publishes and receives the same snapshots without involving FlowCast's main application or audio router.

**Tech Stack:** .NET 8, WPF, Windows.Media.Control, HttpClient/System.Text.Json, TCP/UDP on Radmin VPN, xUnit, React 19, TypeScript, Vite/Vitest, WebView2, `liquid-glass-react` 1.1.1.

## Global Constraints

- Work on the existing `codex/flowcast-1.1.0` branch in the current checkout; the user selected inline execution.
- Do not edit, stage, revert, or commit `audio-share/src/AudioShare.App/App.xaml.cs`; it contains unrelated user changes.
- Do not edit `audio-share/src/AudioShare.App/MainWindow.xaml.cs`, FlowCast main-window XAML, audio routing, or the Python router helper.
- A friend with Radmin VPN already connected opens only `FlowCast Lyrics`; no NetEase or FlowCast main application is required on the friend computer.
- The owner opens NetEase Cloud Music and `FlowCast Lyrics`; FlowCast main is optional and is not part of this implementation.
- Remote Radmin lyric state wins over local NetEase state; local state resumes immediately after remote expiry.
- No UI asks for IP addresses, ports, pairing codes, or QR codes.
- Never read or transmit NetEase cookies, credentials, account IDs, playlists, album art, audio, or music files.
- Never invent sample lyrics or retain a stale line across track change, disconnect, or source loss.
- The visible overlay is only a draggable, topmost capsule with no logo, badges, title bar, close button, or other window chrome.
- Use the existing `liquid-glass-react` 1.1.1 dependency in `standard` mode.
- Run frontend npm commands and .NET commands sequentially because the .NET build target runs `npm.cmd ci` and concurrent npm access corrupts `node_modules`.
- Commit only task-scoped files after each green test cycle.

---

### Task 1: Replace Connection-Only Presentation With an Island Snapshot Contract

**Files:**
- Modify: `audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/LyricsOverlayPresenter.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/LyricsContractsTests.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/LyricsOverlayPresenterTests.cs`

**Interfaces:**
- Produces: `IslandMode`, `LyricSource`, `IslandSnapshot`, and `ILyricsOverlaySink.Show(IslandSnapshot snapshot)`.
- Produces: `LyricsOverlayPresenter.Show(IslandSnapshot snapshot)`, `CurrentSnapshot`, `DisplayText`, and `GetSnapshotJson()`.
- Consumed by: Tasks 5, 7, and 8.

- [ ] **Step 1: Write failing contract tests**

Replace the old connection-state enum assertion with exact island modes and verify the display snapshot preserves its source:

```csharp
[Fact]
public void IslandMode_ContainsOnlyTheApprovedModes() => Assert.Equal(
    ["Idle", "Resolving", "Playing", "Paused", "NoLyrics", "Unavailable"],
    Enum.GetNames<IslandMode>());

[Fact]
public void IslandSnapshot_PreservesRealLyricAndSource()
{
    var line = new LyricLine("真實歌詞", 1250, 4800);
    var snapshot = new IslandSnapshot(IslandMode.Playing, "真實歌詞", line, LyricSource.LocalNetEase);

    Assert.Same(line, snapshot.LyricLine);
    Assert.Equal(LyricSource.LocalNetEase, snapshot.Source);
}
```

- [ ] **Step 2: Write failing presenter tests**

Add cases proving status modes reject lyrics, paused lyrics serialize honestly, and mode/source names use camel case:

```csharp
[Fact]
public void Show_PausedLyric_SerializesTheIslandSnapshot()
{
    var presenter = new LyricsOverlayPresenter();
    presenter.Show(new IslandSnapshot(
        IslandMode.Paused,
        "只可顯示收到的文字",
        new LyricLine("只可顯示收到的文字", 1000, 2400),
        LyricSource.RemoteRadmin));

    using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
    Assert.Equal("paused", json.RootElement.GetProperty("mode").GetString());
    Assert.Equal("remoteRadmin", json.RootElement.GetProperty("source").GetString());
    Assert.Equal("只可顯示收到的文字", json.RootElement.GetProperty("lyricLine").GetProperty("text").GetString());
}

[Theory]
[InlineData(IslandMode.Idle, "等待播放")]
[InlineData(IslandMode.Resolving, "正在取得歌詞")]
[InlineData(IslandMode.NoLyrics, "這首歌沒有歌詞")]
[InlineData(IslandMode.Unavailable, "暫時無法取得歌詞")]
public void Show_StatusMode_DoesNotExposeALyric(IslandMode mode, string text)
{
    var presenter = new LyricsOverlayPresenter();
    presenter.Show(new IslandSnapshot(mode, text, new LyricLine("不可顯示", 0, null), LyricSource.LocalNetEase));

    using var json = JsonDocument.Parse(presenter.GetSnapshotJson());
    Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("lyricLine").ValueKind);
}
```

- [ ] **Step 3: Run focused tests and verify red**

Run:

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~LyricsContractsTests|FullyQualifiedName~LyricsOverlayPresenterTests" --configuration Release
```

Expected: compilation fails because `IslandMode`, `LyricSource`, `IslandSnapshot`, and `Show` do not exist.

- [ ] **Step 4: Implement the minimal dependency-free contract**

Use these exact public shapes:

```csharp
public enum IslandMode { Idle, Resolving, Playing, Paused, NoLyrics, Unavailable }
public enum LyricSource { None, LocalNetEase, RemoteRadmin }

public sealed record IslandSnapshot(
    IslandMode Mode,
    string DisplayText,
    LyricLine? LyricLine,
    LyricSource Source);

public interface ILyricsOverlaySink
{
    void Show(IslandSnapshot snapshot);
}
```

Make `LyricsOverlayPresenter` default to `new(IslandMode.Idle, "等待播放", null, LyricSource.None)`. In `Show`, trim display text, remove the line unless mode is `Playing` or `Paused`, remove whitespace-only lines, store the normalized snapshot, and publish the complete JSON snapshot with camel-case enum strings.

- [ ] **Step 5: Run focused tests and verify green**

Run the command from Step 3.

Expected: all contract and presenter tests pass.

- [ ] **Step 6: Commit only Task 1 files**

```powershell
git add -- audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs audio-share/src/AudioShare.Lyrics/LyricsOverlayPresenter.cs audio-share/tests/AudioShare.Core.Tests/LyricsContractsTests.cs audio-share/tests/AudioShare.Core.Tests/LyricsOverlayPresenterTests.cs
git commit -m "refactor: model Lyrics island display states"
```

### Task 2: Parse and Cache Timed LRC Lyrics

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/LrcParser.cs`
- Create: `audio-share/src/AudioShare.Lyrics/LyricsCache.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LrcParserTests.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LyricsCacheTests.cs`

**Interfaces:**
- Produces: `LrcParser.Parse(string lrc): IReadOnlyList<LyricLine>`.
- Produces: `ILyricsCache.ReadAsync`, `WriteAsync`, and `FileLyricsCache`.
- Consumed by: Task 3.

- [ ] **Step 1: Write failing LRC tests**

```csharp
[Fact]
public void Parse_OrdersTimedLinesAndSetsEndTimes()
{
    var lines = LrcParser.Parse("[ar:歌手]\n[00:02.50]第二句\n[00:01.00]第一句");

    Assert.Collection(lines,
        first => { Assert.Equal("第一句", first.Text); Assert.Equal(1000, first.StartTimeMilliseconds); Assert.Equal(2500, first.EndTimeMilliseconds); },
        second => { Assert.Equal("第二句", second.Text); Assert.Equal(2500, second.StartTimeMilliseconds); Assert.Null(second.EndTimeMilliseconds); });
}

[Fact]
public void Parse_IgnoresMetadataMalformedAndBlankRows()
{
    Assert.Empty(LrcParser.Parse("[ti:標題]\n錯誤行\n[00:03.00]   "));
}
```

Also cover multiple timestamps on one row and one-, two-, and three-digit fractional seconds.

- [ ] **Step 2: Write failing file-cache tests**

Use an xUnit-created unique temporary directory and assert a track ID round-trips raw LRC while an absent key returns `null`. Always delete only that test directory in `finally`.

```csharp
await cache.WriteAsync("496869422", "[00:01.00]真實歌詞", CancellationToken.None);
Assert.Equal("[00:01.00]真實歌詞", await cache.ReadAsync("496869422", CancellationToken.None));
Assert.Null(await cache.ReadAsync("missing", CancellationToken.None));
```

- [ ] **Step 3: Run tests and verify red**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~LrcParserTests|FullyQualifiedName~LyricsCacheTests" --configuration Release
```

Expected: compilation fails because parser and cache types do not exist.

- [ ] **Step 4: Implement the parser**

Parse every timestamp prefix with this format and build end times from the next sorted start time:

```csharp
[GeneratedRegex(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{2})(?:[\.:](?<fraction>\d{1,3}))?\]")]
private static partial Regex TimestampRegex();
```

Normalize fractional seconds so `5`, `50`, and `500` mean 500 milliseconds. Trim text after the last timestamp match, create one line per timestamp, sort by start time, and discard blank text.

- [ ] **Step 5: Implement the cache**

Use this exact interface:

```csharp
public interface ILyricsCache
{
    Task<string?> ReadAsync(string trackId, CancellationToken cancellationToken);
    Task WriteAsync(string trackId, string lrc, CancellationToken cancellationToken);
}
```

`FileLyricsCache` accepts a root directory, validates track IDs with `^[0-9]+$`, stores UTF-8 files as `<trackId>.lrc`, creates only its own root directory, and uses `File.ReadAllTextAsync`/`WriteAllTextAsync`.

- [ ] **Step 6: Run tests and commit**

Run Step 3 and expect all focused tests to pass, then:

```powershell
git add -- audio-share/src/AudioShare.Lyrics/LrcParser.cs audio-share/src/AudioShare.Lyrics/LyricsCache.cs audio-share/tests/AudioShare.Core.Tests/LrcParserTests.cs audio-share/tests/AudioShare.Core.Tests/LyricsCacheTests.cs
git commit -m "feat: parse and cache timed lyrics"
```

### Task 3: Resolve NetEase Track IDs and Retrieve Real Lyrics

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/NeteaseTrackResolver.cs`
- Create: `audio-share/src/AudioShare.Lyrics/NeteaseLyricsClient.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/NeteaseTrackResolverTests.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/NeteaseLyricsClientTests.cs`

**Interfaces:**
- Consumes: `ILyricsCache` and `LrcParser.Parse` from Task 2.
- Produces: `NeteaseTrackCandidate`, `INeteasePlayingListReader`, `INeteaseSearchClient`, `INeteaseTrackResolver`, and `NeteaseTrackResolver.ResolveAsync`.
- Produces: `NeteaseLyricsResult`, `INeteaseLyricsProvider`, and `NeteaseLyricsClient.GetAsync`.
- Consumed by: Tasks 5 and 8.

- [ ] **Step 1: Write failing resolver tests**

Create in-memory fakes and cover local exact match, search fallback, punctuation/case normalization, artist mismatch, and ambiguity rejection:

```csharp
var resolver = new NeteaseTrackResolver(
    new FakePlayingListReader([new("496869422", "打上花火", ["Daoko", "米津玄師"])]),
    new FakeSearchClient([]));

Assert.Equal("496869422", await resolver.ResolveAsync("打上花火", ["DAOKO"], CancellationToken.None));
```

An input matching two candidates equally must return `null`.

- [ ] **Step 2: Write failing lyric-client tests**

Use a fake `HttpMessageHandler`. Cover a real timed `lrc.lyric`, `nolyric: true`, HTTP failure with cache fallback, and HTTP failure without cache:

```csharp
var result = await client.GetAsync("496869422", CancellationToken.None);
Assert.Equal(NeteaseLyricsStatus.Available, result.Status);
Assert.Equal("真實歌詞", Assert.Single(result.Lines).Text);
```

- [ ] **Step 3: Run focused tests and verify red**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~NeteaseTrackResolverTests|FullyQualifiedName~NeteaseLyricsClientTests" --configuration Release
```

Expected: compilation fails because resolver/client types do not exist.

- [ ] **Step 4: Implement exact track resolution**

Use these shapes:

```csharp
public sealed record NeteaseTrackCandidate(string Id, string Title, IReadOnlyList<string> Artists);
public interface INeteasePlayingListReader { Task<IReadOnlyList<NeteaseTrackCandidate>> ReadAsync(CancellationToken cancellationToken); }
public interface INeteaseSearchClient { Task<IReadOnlyList<NeteaseTrackCandidate>> SearchAsync(string title, IReadOnlyList<string> artists, CancellationToken cancellationToken); }
public interface INeteaseTrackResolver { Task<string?> ResolveAsync(string title, IReadOnlyList<string> artists, CancellationToken cancellationToken); }
```

Normalize Unicode with Form KC, retain only letters and digits, and compare invariant lowercase strings. Accept only exact normalized title plus at least one exact normalized artist. Search only when the local list has no unique match; return `null` for zero or multiple matches.

The concrete playing-list reader reads `%LOCALAPPDATA%\NetEase\CloudMusic\webdata\file\playingList` only. The concrete search client calls:

```text
GET https://music.163.com/api/search/get/web?s=<escaped title and artists>&type=1&limit=10&offset=0
Referer: https://music.163.com/
```

- [ ] **Step 5: Implement lyric retrieval with cache fallback**

Use:

```csharp
public enum NeteaseLyricsStatus { Available, NoLyrics, Unavailable }
public sealed record NeteaseLyricsResult(NeteaseLyricsStatus Status, IReadOnlyList<LyricLine> Lines);
public interface INeteaseLyricsProvider { Task<NeteaseLyricsResult> GetAsync(string trackId, CancellationToken cancellationToken); }
```

Request `https://music.163.com/api/song/lyric?id=<id>&lv=-1&kv=-1&tv=-1`, set the NetEase referer, parse only `lrc.lyric`, write successful non-empty raw LRC to cache, and fall back to cached raw LRC on timeout, non-success status, invalid JSON, or empty response. Return `NoLyrics` only for an explicit no-lyric response; otherwise return `Unavailable` when no valid network or cached timed lyric exists.

- [ ] **Step 6: Run tests and commit**

Run Step 3 and expect all focused tests to pass, then:

```powershell
git add -- audio-share/src/AudioShare.Lyrics/NeteaseTrackResolver.cs audio-share/src/AudioShare.Lyrics/NeteaseLyricsClient.cs audio-share/tests/AudioShare.Core.Tests/NeteaseTrackResolverTests.cs audio-share/tests/AudioShare.Core.Tests/NeteaseLyricsClientTests.cs
git commit -m "feat: resolve NetEase tracks and lyrics"
```

### Task 4: Observe NetEase Through the Windows Media Session

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/NeteaseMediaSessionSource.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/NeteaseMediaSessionSourceTests.cs`

**Interfaces:**
- Produces: `MediaPlaybackSnapshot`, `INeteaseMediaSessionSource`, and `NeteaseMediaSessionSource.WatchAsync`.
- Consumed by: Task 8.

- [ ] **Step 1: Write failing classification and timeline tests**

```csharp
[Theory]
[InlineData("cloudmusic.exe", true)]
[InlineData("NetEase.CloudMusic", true)]
[InlineData("chrome.exe", false)]
public void IsNeteaseSource_MatchesOnlyNetEase(string source, bool expected) =>
    Assert.Equal(expected, NeteaseMediaSessionSource.IsNeteaseSource(source));

[Fact]
public void AdvancePosition_FreezesWhenPaused()
{
    var captured = DateTimeOffset.Parse("2026-08-02T08:00:00Z");
    var snapshot = new MediaPlaybackSnapshot("歌", ["歌手"], 180000, 5000, captured, false);
    Assert.Equal(5000, snapshot.PositionAt(captured.AddSeconds(10)));
}
```

Also prove playing advances, never drops below zero, and never exceeds duration.
Add a metadata-mapping case proving an artist string such as `Daoko / 米津玄師` becomes two trimmed artist values.

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~NeteaseMediaSessionSourceTests" --configuration Release
```

Expected: compilation fails because the source and snapshot do not exist.

- [ ] **Step 3: Implement the playback snapshot**

```csharp
public sealed record MediaPlaybackSnapshot(
    string Title,
    IReadOnlyList<string> Artists,
    long DurationMilliseconds,
    long PositionMilliseconds,
    DateTimeOffset CapturedAtUtc,
    bool IsPlaying)
{
    public long PositionAt(DateTimeOffset now) => Math.Clamp(
        PositionMilliseconds + (IsPlaying ? (long)(now - CapturedAtUtc).TotalMilliseconds : 0),
        0,
        DurationMilliseconds);
}

public interface INeteaseMediaSessionSource
{
    IAsyncEnumerable<MediaPlaybackSnapshot?> WatchAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement the WinRT adapter**

`WatchAsync` requests `GlobalSystemMediaTransportControlsSessionManager`, selects only a session accepted by `IsNeteaseSource`, obtains title/artist through `TryGetMediaPropertiesAsync`, reads timeline/playback info, and emits when the track, play/pause state, or reported timeline changes. Split the media-session artist string on `/`, `／`, `、`, `;`, and `,`, trim every value, and discard blanks. Poll every 250 milliseconds with `PeriodicTimer`; emit `null` after the NetEase session disappears so stale lyrics clear.

`IsNeteaseSource` accepts a case-insensitive `cloudmusic` or `netease` token and rejects all other source IDs.

- [ ] **Step 5: Run focused tests and commit**

Run Step 2 and expect all tests to pass, then:

```powershell
git add -- audio-share/src/AudioShare.Lyrics/NeteaseMediaSessionSource.cs audio-share/tests/AudioShare.Core.Tests/NeteaseMediaSessionSourceTests.cs
git commit -m "feat: observe NetEase media playback"
```

### Task 5: Synchronize Lyrics and Apply Remote-First Source Priority

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/LyricsSourceCoordinator.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LyricsSourceCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IslandSnapshot`, `LyricLine`, `MediaPlaybackSnapshot`, and `NeteaseLyricsResult`.
- Produces: `LyricsSourceCoordinator.SetLocal`, `SetRemote`, `ClearRemote`, and `SnapshotAt`.
- Consumed by: Tasks 6 and 8.

- [ ] **Step 1: Write failing synchronization tests**

Cover line selection, pause, seek, track change clearing, no-lyric/unavailable states, remote priority, heartbeat expiry, and local fallback:

```csharp
[Fact]
public void SnapshotAt_RemoteWinsThenExpiresBackToLocal()
{
    var now = DateTimeOffset.Parse("2026-08-02T08:00:00Z");
    var coordinator = new LyricsSourceCoordinator(TimeSpan.FromSeconds(5));
    coordinator.SetLocal(new LocalLyricsPlayback(
        "local",
        new MediaPlaybackSnapshot("歌", ["歌手"], 180000, 1000, now, true),
        NeteaseLyricsStatus.Available,
        [new LyricLine("本機", 0, null)]));
    coordinator.SetRemote(new RemoteLyricsPlayback(
        "remote",
        1,
        IslandMode.Playing,
        new LyricLine("遠端", 0, null),
        now));

    Assert.Equal("遠端", coordinator.SnapshotAt(now).DisplayText);
    Assert.Equal(LyricSource.RemoteRadmin, coordinator.SnapshotAt(now).Source);
    Assert.Equal("本機", coordinator.SnapshotAt(now.AddSeconds(6)).DisplayText);
}
```

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~LyricsSourceCoordinatorTests" --configuration Release
```

Expected: compilation fails because coordinator playback types do not exist.

- [ ] **Step 3: Implement local line selection**

Use these exact inputs:

```csharp
public sealed record LocalLyricsPlayback(
    string TrackId,
    MediaPlaybackSnapshot Media,
    NeteaseLyricsStatus LyricsStatus,
    IReadOnlyList<LyricLine> Lines);

public sealed record RemoteLyricsPlayback(
    string SessionId,
    long Sequence,
    IslandMode Mode,
    LyricLine? LyricLine,
    DateTimeOffset ReceivedAtUtc);
```

`SetLocal(LocalLyricsPlayback? playback)` stores or clears local state. On every `SnapshotAt(now)`, use `MediaPlaybackSnapshot.PositionAt(now)` and choose the last line whose start is less than or equal to position and whose end is null or greater than position. Return `Playing` or `Paused` only with a real line; before the first timed line, return `Resolving` with `正在取得歌詞` rather than inventing text.

- [ ] **Step 4: Implement remote priority and expiry**

`SetRemote(RemoteLyricsPlayback playback)` rejects updates whose sequence is not greater than the accepted sequence for that session. `ReceivedAtUtc` is the friend's local receive time, not the owner's clock, so clock skew cannot extend a heartbeat. Treat remote as active for five seconds after that value. When it expires, clear it and return the current local snapshot in the same `SnapshotAt` call. `ClearRemote()` removes it immediately after a confirmed connection close.

- [ ] **Step 5: Run focused tests and commit**

Run Step 2 and expect all tests to pass, then:

```powershell
git add -- audio-share/src/AudioShare.Lyrics/LyricsSourceCoordinator.cs audio-share/tests/AudioShare.Core.Tests/LyricsSourceCoordinatorTests.cs
git commit -m "feat: coordinate local and remote lyrics"
```

### Task 6: Extend Radmin Discovery Into a Versioned Lyric Stream

**Files:**
- Modify: `audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs`
- Modify: `audio-share/src/AudioShare.Windows/AudioShare.Windows.csproj`
- Modify: `audio-share/src/AudioShare.Windows/RadminLyricsLink.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/RadminLyricsLinkTests.cs`

**Interfaces:**
- Consumes: island and lyric contracts from Task 1.
- Produces: `RadminLyricsFrame`, `RadminLyricsHost.PublishAsync`, `RadminLyricsReceiver.FrameReceived`, and the low-level host/receiver APIs consumed by `RadminLyricsRelay` in Task 8.
- Consumed by: Task 8.

- [ ] **Step 1: Write failing frame-delivery tests**

Extend the existing loopback test to retain the TCP session and deliver a real frame:

```csharp
var frameReceived = new TaskCompletionSource<RadminLyricsFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
receiver.FrameReceived += (_, frame) => frameReceived.TrySetResult(frame);
Assert.True(await receiver.DiscoverAndConnectAsync(discoveryAddress));

await host.PublishAsync(new RadminLyricsFrame(
    ProtocolVersion: 1,
    SessionId: "owner-a",
    Sequence: 1,
    TrackId: "496869422",
    Mode: IslandMode.Playing,
    LyricLine: new LyricLine("真實歌詞", 1000, 2500),
    PositionMilliseconds: 1200,
    CapturedAtUtc: DateTimeOffset.UtcNow,
    IsPlaying: true));

Assert.Equal("真實歌詞", (await frameReceived.Task.WaitAsync(TimeSpan.FromSeconds(2))).LyricLine?.Text);
```

Add tests for self-instance discovery rejection, monotonic sequence validation, malformed/oversized frames, and host disposal with connected receivers.

- [ ] **Step 2: Run focused tests and verify red**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~RadminLyricsLinkTests" --configuration Release
```

Expected: compilation fails because frames and publish/receive APIs do not exist.

- [ ] **Step 3: Add the wire contract and project reference**

Put `RadminLyricsFrame` in the dependency-free contracts project with the exact constructor used by the test. Add a project reference from `AudioShare.Windows` to `AudioShare.Lyrics.Contracts`; do not add a reference in the opposite direction.

The record shape is:

```csharp
public sealed record RadminLyricsFrame(
    int ProtocolVersion,
    string SessionId,
    long Sequence,
    string? TrackId,
    IslandMode Mode,
    LyricLine? LyricLine,
    long PositionMilliseconds,
    DateTimeOffset CapturedAtUtc,
    bool IsPlaying);
```

- [ ] **Step 4: Retain validated clients and publish frames**

Change the host handshake to include an instance ID, retain accepted client streams in a private synchronized collection, and serialize each frame as one UTF-8 JSON line. `PublishAsync` writes only protocol version 1 frames with a non-empty session ID, positive sequence, and a serialized size no greater than 65,536 bytes. Remove a client after a failed write.

- [ ] **Step 5: Receive and validate frames**

After the receiver handshake succeeds, start a cancellation-aware read loop. Use a bounded line reader that stops and disconnects before buffering more than 65,536 characters. Reject invalid JSON, versions other than 1, empty session IDs, non-positive sequences, regressing sequence values, and lyric text larger than 4,096 characters. Raise `FrameReceived` only for accepted frames.

Discovery request/response JSON includes sender and host instance IDs. The receiver ignores a response whose host instance ID equals its own instance ID, preventing an owner from connecting to itself.

- [ ] **Step 6: Run focused tests repeatedly and commit**

Run the focused test command five consecutive times; expect every run to pass without `ObjectDisposedException`, timeout, or port leak. Then:

```powershell
git add -- audio-share/src/AudioShare.Lyrics.Contracts/LyricsContracts.cs audio-share/src/AudioShare.Windows/AudioShare.Windows.csproj audio-share/src/AudioShare.Windows/RadminLyricsLink.cs audio-share/tests/AudioShare.Core.Tests/RadminLyricsLinkTests.cs
git commit -m "feat: stream lyrics over Radmin VPN"
```

### Task 7: Render the Four-Size Dynamic Island in React

**Files:**
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.ts`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.test.ts`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css`

**Interfaces:**
- Consumes: camel-case `IslandSnapshot` JSON from Task 1.
- Produces: one liquid-glass capsule with mode classes and no window chrome.
- Consumed by: Task 8 WebView2 hosting.

- [ ] **Step 1: Write failing reducer tests**

Replace connection-state fixtures with:

```ts
const playing = reduceOverlayState(initialState, {
  mode: 'playing',
  displayText: '真實歌詞',
  lyricLine: { text: '真實歌詞', startTimeMilliseconds: 1000, endTimeMilliseconds: 2500 },
  source: 'localNetEase',
})

expect(playing.mode).toBe('playing')
expect(playing.source).toBe('localNetEase')
```

Reject unknown mode/source combinations, status snapshots carrying lyrics, and playing snapshots without a valid lyric.

- [ ] **Step 2: Write failing component tests**

Render all six modes and assert exact classes/copy. For playing and paused, assert `data-testid="lyric-line"`; for status modes, assert it is absent. Continue asserting that `.brand-row`, `.connection-chip`, close controls, and drag handles do not exist.

- [ ] **Step 3: Run frontend tests and verify red**

```powershell
cd audio-share\src\AudioShare.Lyrics\Frontend
npm.cmd test -- --run
```

Expected: tests fail because the reducer and component still use `connectionState`.

- [ ] **Step 4: Implement exact frontend state validation**

```ts
export type IslandMode = 'idle' | 'resolving' | 'playing' | 'paused' | 'noLyrics' | 'unavailable'
export type LyricSource = 'none' | 'localNetEase' | 'remoteRadmin'
```

The initial state is `{ mode: 'idle', displayText: '等待播放', lyricLine: null, source: 'none' }`. Accept a lyric only for playing/paused modes and require it for those modes.

- [ ] **Step 5: Implement the single liquid-glass island**

Set `<main className={`overlay overlay--${state.mode}`}>`, retain one `LiquidGlass` import and `mode="standard"`, and render only the centered current text. Use mode classes for paused opacity and lyric arrival; do not add controls, title, logo, artist, album, source badge, progress bar, or sample lyric.

CSS keeps each HTML surface at 100% of the WPF window. It transitions opacity and internal transform for 320 milliseconds, and the existing reduced-motion query reduces all durations to 0.01 milliseconds.

- [ ] **Step 6: Run tests/build and commit**

Run sequentially:

```powershell
npm.cmd test -- --run
npm.cmd run build
```

Expected: all frontend tests pass and Vite emits `dist/index.html`. Then:

```powershell
git add -- audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.ts audio-share/src/AudioShare.Lyrics/Frontend/src/overlayState.test.ts audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css
git commit -m "feat: animate Lyrics Dynamic Island states"
```

### Task 8: Orchestrate Local NetEase, Radmin Relay, and Native Window Motion

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/IslandDimensions.cs`
- Create: `audio-share/src/AudioShare.Lyrics/RadminLyricsRelay.cs`
- Create: `audio-share/src/AudioShare.Lyrics/LyricsRuntime.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/IslandDimensionsTests.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LyricsRuntimeTests.cs`

**Interfaces:**
- Consumes: all local source, coordinator, presenter, and Radmin APIs from Tasks 1-6.
- Produces: the runnable standalone owner/friend workflow and WPF size animation.

- [ ] **Step 1: Write failing dimension and XAML tests**

```csharp
[Theory]
[InlineData(IslandMode.Idle, 180, 44)]
[InlineData(IslandMode.Resolving, 280, 52)]
[InlineData(IslandMode.Playing, 680, 92)]
[InlineData(IslandMode.Paused, 680, 92)]
[InlineData(IslandMode.NoLyrics, 320, 52)]
[InlineData(IslandMode.Unavailable, 320, 52)]
public void For_ReturnsApprovedSize(IslandMode mode, double width, double height) =>
    Assert.Equal(new IslandSize(width, height), IslandDimensions.For(mode));
```

Update `FlowCastLyricsStartupTests` to expect initial 180 by 44, min 180 by 44, max 680 by 92, topmost/transparent/no chrome, and the absence of old FlowCast/Radmin waiting copy.

- [ ] **Step 2: Write failing runtime tests with fakes**

Use fake `INeteaseMediaSessionSource`, `INeteaseTrackResolver`, `INeteaseLyricsProvider`, and `ILyricsRelay` implementations to prove:

- a local NetEase track produces resolving then real lyric state;
- a local line is published to connected friends;
- an incoming remote frame overrides local;
- remote expiry restores local;
- session loss clears the last lyric;
- cancellation disposes both host and receiver without an unobserved exception.

- [ ] **Step 3: Run focused tests and verify red**

```powershell
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --filter "FullyQualifiedName~IslandDimensionsTests|FullyQualifiedName~LyricsRuntimeTests|FullyQualifiedName~FlowCastLyricsStartupTests" --configuration Release
```

Expected: compilation or assertions fail because runtime, dynamic dimensions, and new XAML do not exist.

- [ ] **Step 4: Implement the runtime loop**

`RadminLyricsRelay` adapts the low-level Task 6 host and receiver behind this internal seam. It starts receiving immediately, starts the publishing host lazily on the first local publish, and carries one process instance ID into both discovery directions:

```csharp
internal interface ILyricsRelay : IAsyncDisposable
{
    event EventHandler<RadminLyricsFrame>? FrameReceived;
    Task StartReceivingAsync(CancellationToken cancellationToken);
    Task PublishAsync(RadminLyricsFrame frame, CancellationToken cancellationToken);
    Task StopPublishingAsync(CancellationToken cancellationToken);
}
```

`LyricsRuntime` accepts `INeteaseMediaSessionSource`, `INeteaseTrackResolver`, `INeteaseLyricsProvider`, `ILyricsRelay`, `LyricsSourceCoordinator`, and `TimeProvider` through its internal test constructor. It owns one cancellation scope and exposes:

```csharp
public event EventHandler<IslandSnapshot>? SnapshotChanged;
public Task StartAsync(CancellationToken cancellationToken);
public ValueTask DisposeAsync();
```

Run the 250-millisecond local media watch, a 100-millisecond display tick, and Radmin discovery concurrently. Resolve/fetch only when normalized title/artists change. Start publishing a `Resolving` frame as soon as a local NetEase media session appears, then publish resolved lyric/status frames. On local session loss, publish no stale lyric, call `StopPublishingAsync`, and clear local coordinator state. Feed incoming frames to the coordinator using the friend's local receive time. Emit `SnapshotChanged` only when the snapshot value changes.

Use `%LOCALAPPDATA%\FlowCast Lyrics\LyricsCache` for cache files and a shared `HttpClient` with a 10-second timeout. Generate one random instance ID per process. Do not read NetEase login state.

- [ ] **Step 5: Make the native window dynamically sized**

Set initial XAML dimensions to 180 by 44, min to 180 by 44, max to 680 by 92, and keep `WindowStyle="None"`, `AllowsTransparency="True"`, `ResizeMode="NoResize"`, `Topmost="True"`, and `ShowInTaskbar="True"`.

When a snapshot arrives on the dispatcher, call the presenter and animate `Width`/`Height` to `IslandDimensions.For(snapshot.Mode)`. Use a 320-millisecond `BackEase` with low amplitude only when `SystemParameters.ClientAreaAnimation` is true; otherwise assign dimensions immediately. Keep the window center fixed during size changes by adjusting `Left` and `Top` by half the size delta, then clamp the result to the current monitor work area.

Update the native fallback text and corner radius for every snapshot. Keep the entire root border as the only drag surface.

- [ ] **Step 6: Run focused and full tests**

Run Task 8 focused tests, then:

```powershell
dotnet test AudioShare.sln --configuration Release
```

Expected: all .NET tests pass, including the unchanged FlowCast main-application suite.

- [ ] **Step 7: Commit Task 8 files**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/IslandDimensions.cs audio-share/src/AudioShare.Lyrics/RadminLyricsRelay.cs audio-share/src/AudioShare.Lyrics/LyricsRuntime.cs audio-share/src/AudioShare.Lyrics/MainWindow.xaml audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs audio-share/tests/AudioShare.Core.Tests/IslandDimensionsTests.cs audio-share/tests/AudioShare.Core.Tests/LyricsRuntimeTests.cs
git commit -m "feat: run standalone Lyrics Dynamic Island"
```

### Task 9: Live Verification, Sandbox Relay, Packaging, and Delivery

**Files:**
- Modify only if required by verified packaging failure: `audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj`
- Generate ignored artifacts: `audio-share/release/FlowCast-Lyrics-1.1.0-dynamic-island-win-x64/`
- Generate ignored artifact: `audio-share/release/FlowCast-Lyrics-1.1.0-dynamic-island-win-x64.zip`
- Generate ignored QA images under: `audio-share/release/FlowCast-Lyrics-1.1.0-dynamic-island-qa/`

**Interfaces:**
- Consumes: the completed standalone executable.
- Produces: a verified ZIP delivery unit, exact EXE folder, screenshots, SHA-256, and test counts.

- [ ] **Step 1: Run fresh sequential automated verification**

From the frontend directory:

```powershell
npm.cmd test -- --run
npm.cmd run build
```

Then from `audio-share`:

```powershell
dotnet test AudioShare.sln --configuration Release
git diff --check
```

Expected: frontend tests, Vite build, and all .NET tests pass; `git diff --check` prints nothing.

- [ ] **Step 2: Perform a live NetEase smoke test**

With the installed NetEase 3.1.37 client playing a song, launch the Release executable and verify logs/state show:

- the media source ID is accepted as NetEase;
- a non-empty title and artist are obtained;
- one unambiguous NetEase track ID is resolved;
- the lyric request or cache returns at least one timed line;
- pause freezes the displayed line and seek selects the correct line.

If NetEase exposes no Windows media session, report that concrete runtime limitation and do not substitute playlist-order guessing or fake lyrics.

- [ ] **Step 3: Run the two-process sandbox relay test**

Launch one test host and one receiver using distinct instance IDs against loopback or the detected Radmin address. Verify discovery, handshake, a real `RadminLyricsFrame`, pause frame, seek frame, heartbeat, disconnect, and local fallback. Confirm no manual address or port is entered.

Also record the detected Radmin adapter name, 26.x bind address, and prefix. State clearly that final firewall/room verification still requires the user's second computer.

- [ ] **Step 4: Publish to a clean unambiguous folder**

Ensure no test process is running from the target folder, then run:

```powershell
dotnet publish src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output release\FlowCast-Lyrics-1.1.0-dynamic-island-win-x64
```

Verify the folder contains `FlowCast Lyrics.exe`, `wwwroot/index.html`, bundled frontend assets, and `ThirdPartyNotices.txt`. Delete no unrelated release folder.

- [ ] **Step 5: Create the ZIP before launching the published EXE**

```powershell
Compress-Archive -Path release\FlowCast-Lyrics-1.1.0-dynamic-island-win-x64\* -DestinationPath release\FlowCast-Lyrics-1.1.0-dynamic-island-win-x64.zip -CompressionLevel Optimal
Get-FileHash release\FlowCast-Lyrics-1.1.0-dynamic-island-win-x64.zip -Algorithm SHA256
```

Expected: the ZIP does not contain a generated `FlowCast Lyrics.exe.WebView2` runtime-cache directory.

- [ ] **Step 6: Visually QA the exact packaged executable**

Launch only the newly published EXE, record its PID, and do not terminate older user-launched Lyrics processes. Capture actual-executable screenshots over light and dark backgrounds for idle and lyric states. Verify:

- idle is 180 by 44;
- resolving is 280 by 52;
- active lyric is 680 by 92;
- unavailable/no lyric is 320 by 52;
- the capsule has no black square corners, title bar, logo, badges, close button, or drag handle;
- whole-capsule drag moves it between positions and it remains topmost.

Close only the recorded test PID.

- [ ] **Step 7: Inspect final Git and protected-file state**

```powershell
git status --short
git log --oneline -12
git diff -- audio-share/src/AudioShare.App/App.xaml.cs
```

Expected: the pre-existing user modification to `App.xaml.cs` remains unstaged and unchanged by this work; no generated release/cache file is committed.

- [ ] **Step 8: Deliver exact artifacts and evidence**

Report clickable absolute links to the ZIP, EXE folder, and QA screenshots; include frontend/.NET test counts, the local sandbox result, Radmin adapter proof, live NetEase result, ZIP size, and SHA-256. Explicitly identify the remaining real two-computer Radmin check if the second computer was not available.
