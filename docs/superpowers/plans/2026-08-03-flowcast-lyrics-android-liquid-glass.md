# FlowCast Lyrics AndroidLiquidGlass Renderer Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Replace the Lyrics WebView2/rdev renderer with a private Kotlin Compose Desktop helper that renders the approved C strong-liquid, lyrics-first strip through Kyant0 AndroidLiquidGlass Backdrop. FlowCast Lyrics stays the only user-facing launch point and retains its Radmin role.

**Architecture:** FlowCast Lyrics.exe becomes an invisible WPF host. It observes existing Radmin discovery, launches one packaged Compose app-image helper, and sends versioned length-prefixed JSON through a current-user Named Pipe. The helper owns the borderless topmost glass capsule, drag behavior, compact native menu, and option sheet. It never accesses Radmin or creates a network listener.

**Tech Stack:** .NET 8 WPF/x64, System.IO.Pipes, xUnit, Kotlin/JVM 2.3.21, Compose Desktop 1.11.0, io.github.kyant0:backdrop:2.0.0, Kotlin serialization, Gradle 9.6.0, JDK 21.

## Global Constraints

- Work only in C:\Users\DIOWMOW\Documents\Codex\2026-07-28\sites-project-appgprj-6a57a2aedb948191b23ee24e85e6cb92\.worktrees\flowcast-lyrics-tuner.
- Do not edit, stage, or revert audio-share/src/AudioShare.App/App.xaml.cs or audio-share/src/AudioShare.App/MainWindow.xaml.cs.
- Pin Backdrop 2.0.0, Kotlin 2.3.21, Compose 1.11.0, and Gradle wrapper 9.6.0. Do not copy, fork, or imitate AndroidLiquidGlass.
- Render the visible capsule via rememberLayerBackdrop, drawBackdrop, vibrancy, blur, lens, and Highlight.Default. No WPF, CSS, SVG, or handwritten glass fallback.
- A small internal Backdrop scene only supplies pixels to the official renderer. It must not expose gradients, colors, or fake-glass controls.
- Friends open only FlowCast Lyrics.exe. The helper has no taskbar entry, title, setup UI, TCP/UDP listener, IP, port, pairing code, or QR code.
- Frames are UTF-8 JSON prefixed by a four-byte big-endian payload length; maximum 65,536 bytes; protocol version 1; host pipe uses CurrentUserOnly and Asynchronous plus a launch token.
- Live state text is only finding-flowcast / 正在尋找 FlowCast and connected-awaiting-lyrics / 已連線，等待歌詞. Lyric-line is a reserved message only.
- Restart an unexpected helper exit once. On the second failure write 玻璃渲染器不可用 to the local diagnostic log and stop retrying.
- Remove WebView2/rdev visual code only after a real helper handshake and smoke test are green.
- Build a separate local Lyrics package; do not publish GitHub assets or alter the main FlowCast updater package.

## File Map

| Path | Responsibility |
| --- | --- |
| audio-share/lyrics-glass | Standalone Kotlin Compose helper and Gradle wrapper. |
| lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/Protocol.kt | Pipe records and bounded frame codec. |
| lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/RendererState.kt | Pure state reducer. |
| lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/GlassOverlay.kt | Real Backdrop capsule, menu, drag, option sheet. |
| lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/Main.kt | Pipe client and transparent Compose window. |
| src/AudioShare.Lyrics/LyricsGlassSettings.cs | Persisted native Backdrop controls and placement. |
| src/AudioShare.Lyrics/LyricsGlassPipeProtocol.cs | .NET frame codec. |
| src/AudioShare.Lyrics/LyricsGlassRendererSupervisor.cs | Pipe server, process lifecycle, retry. |
| src/AudioShare.Lyrics/LyricsConnectionRuntime.cs | Existing Radmin receiver exposed as connection-only state. |
| scripts/build-lyrics-glass.ps1 | Compose app-image build/copy. |
| scripts/publish-lyrics.ps1 | Separate local Lyrics product output. |

---

### Task 1: Bootstrap the pinned Compose Desktop helper

**Files:**
- Create: audio-share/lyrics-glass/settings.gradle.kts
- Create: audio-share/lyrics-glass/build.gradle.kts
- Create: audio-share/lyrics-glass/gradle/wrapper/gradle-wrapper.properties, gradlew, gradlew.bat, wrapper JAR
- Create: audio-share/lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/Main.kt
- Create: audio-share/lyrics-glass/src/test/kotlin/com/flowcast/lyrics/glass/BuildBaselineTest.kt

**Interfaces:**
- Consumes JDK 21 and Maven Central Backdrop 2.0.0.
- Produces run, test, and createDistributable tasks and main class com.flowcast.lyrics.glass.MainKt.

- [ ] **Step 1: Create Gradle configuration**

    // settings.gradle.kts
    pluginManagement {
        repositories { gradlePluginPortal(); mavenCentral() }
    }
    dependencyResolutionManagement {
        repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
        repositories { mavenCentral() }
    }
    rootProject.name = "flowcast-lyrics-glass"

    // build.gradle.kts
    import org.jetbrains.compose.desktop.application.dsl.TargetFormat

    plugins {
        kotlin("jvm") version "2.3.21"
        kotlin("plugin.serialization") version "2.3.21"
        id("org.jetbrains.compose") version "1.11.0"
    }
    repositories { mavenCentral() }
    dependencies {
        implementation(compose.desktop.currentOs)
        implementation("io.github.kyant0:backdrop:2.0.0")
        implementation("org.jetbrains.kotlinx:kotlinx-serialization-json:1.9.0")
        testImplementation(kotlin("test"))
    }
    kotlin { jvmToolchain(21) }
    tasks.test { useJUnitPlatform() }
    compose.desktop {
        application {
            mainClass = "com.flowcast.lyrics.glass.MainKt"
            nativeDistributions {
                targetFormats(TargetFormat.AppImage)
                packageName = "FlowCast Lyrics Glass"
                packageVersion = "1.1.0"
            }
        }
    }

Generate wrapper files using: gradle wrapper --gradle-version 9.6.0.

- [ ] **Step 2: Write a failing baseline test**

    package com.flowcast.lyrics.glass

    import kotlin.test.Test
    import kotlin.test.assertEquals

    class BuildBaselineTest {
        @Test
        fun applicationMetadata_usesPrivateHelperName() {
            assertEquals("FlowCast Lyrics Glass", applicationMetadata().packageName)
        }
    }

- [ ] **Step 3: Verify RED**

Run: Set-Location audio-share\lyrics-glass; .\gradlew.bat test --no-daemon

Expected: compilation fails because applicationMetadata is missing.

- [ ] **Step 4: Implement minimum entry point**

    package com.flowcast.lyrics.glass

    import androidx.compose.ui.window.application

    data class ApplicationMetadata(val packageName: String)

    fun applicationMetadata() = ApplicationMetadata("FlowCast Lyrics Glass")

    fun main() = application {
        // Task 3 installs the real transparent window.
    }

- [ ] **Step 5: Verify GREEN**

Run:

    Set-Location audio-share\lyrics-glass
    .\gradlew.bat test --no-daemon
    .\gradlew.bat tasks --all --no-daemon | Select-String "createDistributable"

Expected: test passes and Compose app-image task is listed.

- [ ] **Step 6: Commit**

    git add -- audio-share/lyrics-glass
    git commit -m "build: add FlowCast Lyrics Compose renderer skeleton"

---

### Task 2: Define the shared v1 protocol and renderer state

**Files:**
- Create: audio-share/lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/Protocol.kt
- Create: audio-share/lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/RendererState.kt
- Create: audio-share/lyrics-glass/src/test/kotlin/com/flowcast/lyrics/glass/ProtocolTest.kt
- Create: audio-share/lyrics-glass/src/test/kotlin/com/flowcast/lyrics/glass/RendererStateTest.kt

**Interfaces:**
- Consumes four-byte big-endian headers and Json with ignoreUnknownKeys and classDiscriminator = type.
- Produces HostCommand, RendererEvent, GlassSettings, OverlayPosition, RendererConnectionState, PipeFrameCodec, RendererState.reduce(command), and a two-step hello/initialize/ready handshake.

- [ ] **Step 1: Write failing decoder and reducer tests**

    @Test
    fun decoder_rejectsWrongProtocolVersion() {
        val json = """{"version":2,"type":"connection-state","state":"finding-flowcast"}"""
        assertFailsWith<ProtocolException> { PipeFrameCodec.decodeHostCommand(json) }
    }

    @Test
    fun reducer_keepsConnectedStateWithoutInventingALyric() {
        val next = RendererState.initial().reduce(
            ConnectionStateCommand(RendererProtocol.version,
                RendererConnectionState.ConnectedAwaitingLyrics))
        assertEquals(RendererConnectionState.ConnectedAwaitingLyrics, next.connectionState)
        assertEquals(null, next.lyric)
    }

    @Test
    fun settings_rejectOutOfRangeNativeValues() {
        assertFalse(GlassSettings(cornerRadiusFraction = 1.1f).isValid)
        assertFalse(GlassSettings(blurRadiusDp = 32.1f).isValid)
        assertFalse(GlassSettings(refractionHeightFraction = -0.01f).isValid)
        assertFalse(GlassSettings(refractionAmountFraction = 1.01f).isValid)
    }

- [ ] **Step 2: Verify RED**

Run: Set-Location audio-share\lyrics-glass; .\gradlew.bat test --tests "*ProtocolTest" --tests "*RendererStateTest" --no-daemon

Expected: test compilation fails because protocol/state types do not exist.

- [ ] **Step 3: Implement v1 records and codec**

    @Serializable
    data class GlassSettings(
        val cornerRadiusFraction: Float = 1f,
        val blurRadiusDp: Float = 2f,
        val refractionHeightFraction: Float = 0.42f,
        val refractionAmountFraction: Float = 0.62f,
        val chromaticAberration: Boolean = true
    ) {
        val isValid: Boolean
            get() = cornerRadiusFraction in 0f..1f &&
                blurRadiusDp in 0f..32f &&
                refractionHeightFraction in 0f..1f &&
                refractionAmountFraction in 0f..1f
    }

    @Serializable
    enum class RendererConnectionState {
        @SerialName("finding-flowcast") FindingFlowcast,
        @SerialName("connected-awaiting-lyrics") ConnectedAwaitingLyrics
    }

    @Serializable sealed interface HostCommand { val version: Int }

    @Serializable @SerialName("initialize")
    data class InitializeCommand(
        override val version: Int,
        val token: String,
        val position: OverlayPosition?,
        val glass: GlassSettings
    ) : HostCommand

    @Serializable @SerialName("connection-state")
    data class ConnectionStateCommand(
        override val version: Int,
        val state: RendererConnectionState
    ) : HostCommand

    @Serializable @SerialName("shutdown")
    data class ShutdownCommand(override val version: Int) : HostCommand

    @Serializable sealed interface RendererEvent { val version: Int }

    @Serializable @SerialName("hello")
    data class HelloEvent(override val version: Int, val token: String) : RendererEvent

    @Serializable @SerialName("ready")
    data class ReadyEvent(override val version: Int) : RendererEvent

    @Serializable @SerialName("settings-committed")
    data class SettingsCommittedEvent(
        override val version: Int,
        val glass: GlassSettings
    ) : RendererEvent

    @Serializable @SerialName("position-changed")
    data class PositionChangedEvent(
        override val version: Int,
        val x: Double,
        val y: Double
    ) : RendererEvent

    @Serializable @SerialName("close-request")
    data class CloseRequestEvent(override val version: Int) : RendererEvent

    @Serializable @SerialName("fault")
    data class FaultEvent(override val version: Int, val message: String) : RendererEvent

Use DataInputStream.readInt and DataOutputStream.writeInt with MaximumFrameBytes = 65_536. Reject negative or oversized lengths before allocating.

- [ ] **Step 4: Implement pure state**

    data class RendererState(
        val connectionState: RendererConnectionState = RendererConnectionState.FindingFlowcast,
        val lyric: LyricLine? = null,
        val position: OverlayPosition? = null,
        val glass: GlassSettings = GlassSettings()
    ) {
        fun reduce(command: HostCommand): RendererState = when (command) {
            is InitializeCommand -> copy(position = command.position, glass = command.glass)
            is ConnectionStateCommand -> copy(connectionState = command.state, lyric = null)
            is ShutdownCommand -> this
        }
        companion object { fun initial() = RendererState() }
    }

LyricLine remains reserved and no host path produces it.

- [ ] **Step 5: Verify GREEN**

Run: Set-Location audio-share\lyrics-glass; .\gradlew.bat test --no-daemon

Expected: frame/version/settings/reducer tests pass.

- [ ] **Step 6: Commit**

    git add -- audio-share/lyrics-glass/src
    git commit -m "feat: define Lyrics Glass pipe protocol and renderer state"

---

### Task 3: Render the C strong-liquid capsule and anchored option sheet

**Files:**
- Create: audio-share/lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/GlassOverlay.kt
- Create: audio-share/lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/OverlayWindow.kt
- Create: audio-share/lyrics-glass/src/test/kotlin/com/flowcast/lyrics/glass/GlassOverlayTest.kt
- Create: audio-share/lyrics-glass/src/test/kotlin/com/flowcast/lyrics/glass/PipeSmokeTest.kt
- Modify: audio-share/lyrics-glass/src/main/kotlin/com/flowcast/lyrics/glass/Main.kt

**Interfaces:**
- Consumes RendererState, GlassSettings, RendererEvent, and the official Backdrop library.
- Produces FlowCastGlassOverlay, GlassOptionsSheet, OverlayWindow, and ready, position-changed, settings-committed, and fault events.

- [ ] **Step 1: Write failing UI state tests**

    @Test
    fun connectedState_displaysOnlyHonestWaitingCopy() {
        assertEquals("已連線，等待歌詞",
            displayText(RendererConnectionState.ConnectedAwaitingLyrics, null))
    }

    @Test
    fun findingState_usesCompactCapsuleDimensions() {
        assertEquals(OverlayDimensions(190, 48),
            dimensionsFor(RendererConnectionState.FindingFlowcast, null))
    }

    @Test
    fun committingSettings_emitsValidatedNativeValues() {
        val event = commitGlassSettings(GlassSettings(refractionAmountFraction = 0.7f))
        assertEquals(0.7f, (event as SettingsCommittedEvent).glass.refractionAmountFraction)
    }

- [ ] **Step 2: Verify RED**

Run: Set-Location audio-share\lyrics-glass; .\gradlew.bat test --tests "*GlassOverlayTest" --no-daemon

Expected: compilation fails because the helpers are absent.

- [ ] **Step 3: Implement display and dimensions**

    data class OverlayDimensions(val width: Int, val height: Int)

    fun displayText(state: RendererConnectionState, lyric: LyricLine?): String = when {
        lyric != null -> lyric.current
        state == RendererConnectionState.FindingFlowcast -> "正在尋找 FlowCast"
        else -> "已連線，等待歌詞"
    }

    fun dimensionsFor(state: RendererConnectionState, lyric: LyricLine?): OverlayDimensions = when {
        lyric != null -> OverlayDimensions(420, 84)
        state == RendererConnectionState.FindingFlowcast -> OverlayDimensions(190, 48)
        else -> OverlayDimensions(240, 64)
    }

    fun commitGlassSettings(value: GlassSettings): RendererEvent {
        require(value.isValid)
        return SettingsCommittedEvent(RendererProtocol.version, value)
    }

- [ ] **Step 4: Implement actual AndroidLiquidGlass rendering**

    val backdrop = rememberLayerBackdrop()
    Box(Modifier.fillMaxSize().layerBackdrop(backdrop)) {
        BackdropSourceScene()

        Box(
            Modifier.drawBackdrop(
                backdrop = backdrop,
                shape = { Capsule() },
                effects = {
                    vibrancy()
                    blur(settings.blurRadiusDp.dp.toPx())
                    val minimum = size.minDimension
                    lens(
                        refractionHeight = settings.refractionHeightFraction * minimum * 0.5f,
                        refractionAmount = settings.refractionAmountFraction * minimum,
                        depthEffect = true,
                        chromaticAberration = settings.chromaticAberration
                    )
                },
                highlight = { Highlight.Default },
                onDrawSurface = { drawRect(Color.White.copy(alpha = 0.08f)) }
            ).size(width.dp, height.dp)
        ) {
            Text(displayText(state.connectionState, state.lyric))
        }
    }

Use only the library Capsule, drawBackdrop, vibrancy, blur, lens, and Highlight.Default for the visible glass. No WebView, CSS, SVG, shader strings, WPF acrylic, or hand-drawn backup.

- [ ] **Step 5: Implement interaction**

Use Window(undecorated = true, transparent = true, alwaysOnTop = true, resizable = false) and WindowDraggableArea over the capsule. Emit one position-changed after a completed drag.

Right click opens a compact Compose popup containing only Adjust Glass and Close FlowCast Lyrics. Adjust Glass opens an anchored borderless popup, never a framed application window. It exposes exactly five Backdrop controls: corner radius, blur radius, refraction height, refraction amount, and chromatic aberration. Controls preview locally. Reset applies defaults; Cancel returns to the last committed state; Done emits settings-committed. Close emits close-request and waits for host Shutdown, so it is not classified as a renderer crash. Do not add color, generic app, or fake-glass controls.

- [ ] **Step 6: Wire helper lifecycle**

Connect using command-line pipe and token. Send hello immediately, wait for a valid initialize with the same token, then send ready and apply subsequent commands. On shutdown or pipe EOF close the Compose application. Convert an unexpected exception into one fault event before exit.

- [ ] **Step 7: Add and run the real helper loopback-pipe smoke test**

    @Test
    fun loopbackPipe_sendsHelloThenReadyAfterValidInitialize() = runTest {
        val server = TestPipeServer()
        val client = startPipeClient(server.name, server.token)
        assertEquals("hello", server.readEvent().type)
        server.write(InitializeCommand(RendererProtocol.version, server.token,
            OverlayPosition(0.0, 0.0), GlassSettings()))
        assertEquals("ready", server.readEvent().type)
        client.cancelAndJoin()
    }

- [ ] **Step 8: Verify GREEN**

Run:

    Set-Location audio-share\lyrics-glass
    .\gradlew.bat test --no-daemon
    .\gradlew.bat run --no-daemon --args="--pipe missing-test-pipe --token test"

Expected: full Kotlin tests include the successful hello/initialize/ready loopback; the missing-pipe smoke generates a clear failure and exits rather than opening a normal titled window or hanging.

- [ ] **Step 9: Commit**

    git add -- audio-share/lyrics-glass/src
    git commit -m "feat: render FlowCast Lyrics with AndroidLiquidGlass"

---

### Task 4: Add .NET settings, framed codec, and helper supervision

**Files:**
- Create: audio-share/src/AudioShare.Lyrics/LyricsGlassSettings.cs
- Create: audio-share/src/AudioShare.Lyrics/LyricsGlassSettingsStore.cs
- Create: audio-share/src/AudioShare.Lyrics/LyricsGlassPipeProtocol.cs
- Create: audio-share/src/AudioShare.Lyrics/LyricsGlassRendererSupervisor.cs
- Create: audio-share/tests/AudioShare.Core.Tests/LyricsGlassSettingsTests.cs
- Create: audio-share/tests/AudioShare.Core.Tests/LyricsGlassPipeProtocolTests.cs
- Create: audio-share/tests/AudioShare.Core.Tests/LyricsGlassRendererSupervisorTests.cs
- Modify: audio-share/src/AudioShare.Lyrics/LyricsDiagnosticLog.cs

**Interfaces:**
- Consumes Task 2 names/ranges and helper path glass\FlowCast Lyrics Glass.exe.
- Produces settings Load/Save, codec WriteAsync/ReadAsync, and StartAsync/SetConnectionStateAsync/StopAsync.

- [ ] **Step 1: Write failing host tests**

    [Fact]
    public void Defaults_AreTheStrongLiquidPreset()
    {
        Assert.Equal(new LyricsGlassSettings(1, 2, 0.42, 0.62, true),
            LyricsGlassSettings.Defaults);
    }

    [Fact]
    public async Task FrameCodec_UsesBigEndianLengthAndRoundTripsJson()
    {
        await using var stream = new MemoryStream();
        const string json = "{\"version\":1,\"type\":\"ready\"}";
        await LyricsGlassPipeProtocol.WriteAsync(stream, json, CancellationToken.None);
        Assert.Equal(0, stream.ToArray()[0]);
        Assert.Equal(32, stream.ToArray()[3]);
        stream.Position = 0;
        Assert.Equal(json, await LyricsGlassPipeProtocol.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Supervisor_RestartsOneUnexpectedExitThenReportsFailure()
    {
        var process = new FakeRendererProcessFactory(exitImmediately: true);
        await using var supervisor = CreateSupervisor(process);
        await supervisor.StartAsync(CancellationToken.None);
        await process.WaitForLaunchesAsync(2);
        Assert.Equal(2, process.LaunchCount);
        Assert.Equal(RendererAvailability.Unavailable, supervisor.Availability);
    }

- [ ] **Step 2: Verify RED**

Run:

    dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsGlassSettingsTests|FullyQualifiedName~LyricsGlassPipeProtocolTests|FullyQualifiedName~LyricsGlassRendererSupervisorTests"

Expected: compilation fails because types are absent.

- [ ] **Step 3: Implement host-owned data and atomic store**

    internal sealed record LyricsGlassSettings(
        double CornerRadiusFraction,
        double BlurRadiusDp,
        double RefractionHeightFraction,
        double RefractionAmountFraction,
        bool ChromaticAberration)
    {
        public static LyricsGlassSettings Defaults { get; } = new(1, 2, 0.42, 0.62, true);
        public bool IsValid =>
            IsInRange(CornerRadiusFraction, 0, 1) &&
            IsInRange(BlurRadiusDp, 0, 32) &&
            IsInRange(RefractionHeightFraction, 0, 1) &&
            IsInRange(RefractionAmountFraction, 0, 1);

        private static bool IsInRange(double value, double min, double max) =>
            double.IsFinite(value) && value >= min && value <= max;
    }

    internal sealed record OverlayPosition(double X, double Y)
    {
        public bool IsValid => double.IsFinite(X) && double.IsFinite(Y);
    }

    internal sealed record LyricsGlassHostState(OverlayPosition? Position, LyricsGlassSettings Glass)
    {
        public static LyricsGlassHostState Defaults { get; } = new(null, LyricsGlassSettings.Defaults);
        public bool IsValid => (Position is null || Position.IsValid) && Glass.IsValid;
    }

Persist one camel-case JSON document at LocalAppData\FlowCast Lyrics\glass-settings.json. A null Position means center on first run; non-null positions must be finite. Missing, malformed, partial, non-finite, or out-of-range values load defaults. Save with a temporary file and atomic replacement.

- [ ] **Step 4: Implement exact .NET codec**

    internal const int CurrentVersion = 1;
    internal const int MaximumFrameBytes = 65_536;

    internal static async ValueTask WriteAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length is 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Invalid Lyrics Glass frame length.");

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

ReadAsync loops until a complete header/payload. It returns null only for clean EOF before a frame, and rejects partial, oversized, malformed UTF-8, malformed JSON, unknown type, and non-v1 inputs before supervisor dispatch.

- [ ] **Step 5: Implement secure, bounded supervision**

StartAsync creates a GUID pipe name and 32-byte Base64 token, opens a duplex byte-mode NamedPipeServerStream using CurrentUserOnly and Asynchronous, then launches:

    glass\FlowCast Lyrics Glass.exe --pipe <name> --token <token>

Use UseShellExecute = false, CreateNoWindow = true, and helper directory as WorkingDirectory. Await hello for five seconds, validate its version and token, send initialize, then await ready for five seconds before sending the latest connection state. Serialize outbound frames with one SemaphoreSlim. Surface position-changed, settings-committed, close-request, and fault as typed supervisor events; MainWindow owns persisted state and handles close-request as a normal application close.

On unexpected exit create a new pipe/token and relaunch once. On the second exit mark RendererAvailability.Unavailable, write a renderer-failure diagnostic, and never relaunch. StopAsync marks shutdown, sends shutdown, closes pipe, waits three seconds, then kills only a remaining child process tree.

- [ ] **Step 6: Verify GREEN**

Run:

    dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsGlassSettingsTests|FullyQualifiedName~LyricsGlassPipeProtocolTests|FullyQualifiedName~LyricsGlassRendererSupervisorTests"
    dotnet test audio-share\AudioShare.sln --no-restore

Expected: focused tests and full suite pass with zero failures.

- [ ] **Step 7: Commit**

    git add -- audio-share/src/AudioShare.Lyrics/LyricsGlassSettings.cs audio-share/src/AudioShare.Lyrics/LyricsGlassSettingsStore.cs audio-share/src/AudioShare.Lyrics/LyricsGlassPipeProtocol.cs audio-share/src/AudioShare.Lyrics/LyricsGlassRendererSupervisor.cs audio-share/src/AudioShare.Lyrics/LyricsDiagnosticLog.cs audio-share/tests/AudioShare.Core.Tests/LyricsGlassSettingsTests.cs audio-share/tests/AudioShare.Core.Tests/LyricsGlassPipeProtocolTests.cs audio-share/tests/AudioShare.Core.Tests/LyricsGlassRendererSupervisorTests.cs
    git commit -m "feat: supervise Lyrics Glass through a secure local pipe"

---

### Task 5: Observe Radmin connection only and migrate WPF host

**Files:**
- Create: audio-share/src/AudioShare.Lyrics/LyricsConnectionRuntime.cs
- Modify: audio-share/src/AudioShare.Lyrics/RadminLyricsRelay.cs
- Modify: audio-share/src/AudioShare.Lyrics/MainWindow.xaml
- Modify: audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs
- Modify: audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj
- Modify: audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
- Create: audio-share/tests/AudioShare.Core.Tests/LyricsConnectionRuntimeTests.cs
- Remove: DesktopBackdropCapture.cs, WindowBackdrop.cs, LyricsWebViewEnvironment.cs, LiquidGlassTunerWindow.xaml, LiquidGlassTunerWindow.xaml.cs, Frontend/

**Interfaces:**
- Consumes Task 4 supervisor and current RadminLyricsRelay discovery.
- Produces one invisible WPF host and one visible Compose capsule with connection-only state.

- [ ] **Step 1: Write failing Radmin/host tests**

    [Fact]
    public async Task ConnectionRuntime_ReportsConnectedOnlyAfterExistingReceiverConnects()
    {
        var relay = new FakeRelay();
        await using var runtime = new LyricsConnectionRuntime(relay);
        var states = new List<LyricsConnectionState>();
        runtime.ConnectionStateChanged += (_, state) => states.Add(state);

        await runtime.StartAsync(CancellationToken.None);
        relay.RaiseConnected();

        Assert.Equal(
            [LyricsConnectionState.FindingFlowcast, LyricsConnectionState.ConnectedAwaitingLyrics],
            states);
    }

    [Fact]
    public void LyricsHost_HasNoVisibleWpfOverlayOrWebViewDependency()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml"));
        var project = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "AudioShare.Lyrics.csproj"));
        Assert.Contains("ShowInTaskbar=\"False\"", xaml);
        Assert.DoesNotContain("WebView2", xaml);
        Assert.DoesNotContain("Microsoft.Web.WebView2", project);
        Assert.DoesNotContain("BuildLyricsFrontend", project);
    }

- [ ] **Step 2: Verify RED**

Run:

    dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsConnectionRuntimeTests|FullyQualifiedName~FlowCastLyricsStartupTests"

Expected: assertions fail because WebView/WPF overlay and media runtime remain.

- [ ] **Step 3: Add connection event without changing Radmin discovery**

Add event EventHandler? Connected to ILyricsRelay and RadminLyricsRelay. Raise it immediately after DiscoverAndConnectOnRadminAsync returns true and before waiting for disconnect. Do not alter adapter selection, broadcast calculation, ports, or retry behavior.

    internal enum LyricsConnectionState { FindingFlowcast, ConnectedAwaitingLyrics }

    internal sealed class LyricsConnectionRuntime : IAsyncDisposable
    {
        private readonly ILyricsRelay relay;
        public event EventHandler<LyricsConnectionState>? ConnectionStateChanged;

        public LyricsConnectionRuntime(ILyricsRelay relay)
        {
            this.relay = relay;
            relay.Connected += OnConnected;
            relay.ConnectionClosed += OnClosed;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            ConnectionStateChanged?.Invoke(this, LyricsConnectionState.FindingFlowcast);
            await relay.StartReceivingAsync(cancellationToken);
        }

        private void OnConnected(object? sender, EventArgs e) =>
            ConnectionStateChanged?.Invoke(this, LyricsConnectionState.ConnectedAwaitingLyrics);

        private void OnClosed(object? sender, EventArgs e) =>
            ConnectionStateChanged?.Invoke(this, LyricsConnectionState.FindingFlowcast);
    }

Dispose unsubscribes and disposes the relay. MainWindow uses it instead of LyricsRuntime, therefore this pass does no NetEase lookup, lyric fetching, or lyric network publishing.

- [ ] **Step 4: Replace visible WPF surface**

    <Window x:Class="AudioShare.Lyrics.MainWindow"
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Width="1" Height="1"
            WindowStyle="None"
            AllowsTransparency="True"
            ShowInTaskbar="False"
            Topmost="False"
            Opacity="0"
            ResizeMode="NoResize"
            WindowStartupLocation="Manual">
        <Grid />
    </Window>

On Loaded call Hide before awaiting. Start LyricsConnectionRuntime and LyricsGlassRendererSupervisor, map state values to wire values, and forward them to the supervisor. Persist typed position-changed and settings-committed events through LyricsGlassSettingsStore. Handle close-request by calling Close, so the supervisor shuts down normally. On Closed await supervisor StopAsync before disposing the Radmin adapter.

- [ ] **Step 5: Remove replaced rdev/WebView visual code**

Remove WebView2 package/content/targets, Frontend assets, desktop-capture classes, WebView environment, old rdev settings/tuner code, and WebView/rdev tests. Keep LyricsRuntime source unmodified but uninstantiated; it is a separate future lyrics-source concern.

- [ ] **Step 6: Verify GREEN**

Run:

    dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsConnectionRuntimeTests|FullyQualifiedName~FlowCastLyricsStartupTests"
    dotnet test audio-share\AudioShare.sln --no-restore

Expected: hidden host, connection-only state, and full suite all pass.

- [ ] **Step 7: Commit**

    git add -- audio-share/src/AudioShare.Lyrics/RadminLyricsRelay.cs audio-share/src/AudioShare.Lyrics/LyricsConnectionRuntime.cs audio-share/src/AudioShare.Lyrics/MainWindow.xaml audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj audio-share/tests/AudioShare.Core.Tests/LyricsConnectionRuntimeTests.cs audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
    git rm -r -- audio-share/src/AudioShare.Lyrics/Frontend
    git rm -- audio-share/src/AudioShare.Lyrics/DesktopBackdropCapture.cs audio-share/src/AudioShare.Lyrics/WindowBackdrop.cs audio-share/src/AudioShare.Lyrics/LyricsWebViewEnvironment.cs audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs
    git commit -m "refactor: replace Lyrics WebView overlay with Glass helper"

---

### Task 6: Package full app image and verify Windows lifecycle

**Files:**
- Create: audio-share/scripts/build-lyrics-glass.ps1
- Create: audio-share/scripts/publish-lyrics.ps1
- Modify: audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj
- Modify: audio-share/ThirdPartyNotices.txt
- Modify: audio-share/README.md
- Create: audio-share/tests/AudioShare.Core.Tests/LyricsGlassPackagingTests.cs

**Interfaces:**
- Consumes Compose createDistributable app image and self-contained Lyrics publish output.
- Produces a standalone local product folder containing FlowCast Lyrics.exe and glass\FlowCast Lyrics Glass.exe with all runtime libraries.

- [ ] **Step 1: Write failing package and smoke tests**

    [Fact]
    public void LyricsProject_CopiesCompleteGlassAppImageDuringPublish()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "AudioShare.Lyrics.csproj"));
        Assert.Contains("BuildLyricsGlassRenderer", project);
        Assert.Contains("FlowCast Lyrics Glass.exe", project);
        Assert.Contains("glass", project);
    }

    [Fact]
    public void ThirdPartyNotices_AttributeBackdropAndDoNotShipRdev()
    {
        var notices = File.ReadAllText(FindRepositoryFile("ThirdPartyNotices.txt"));
        Assert.Contains("AndroidLiquidGlass / Backdrop 2.0.0", notices);
        Assert.Contains("Apache License", notices);
        Assert.DoesNotContain("liquid-glass-react", notices);
    }

- [ ] **Step 2: Verify RED**

Run:

    dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LyricsGlassPackagingTests"

Expected: package test fails until the build scripts and publish target exist.

- [ ] **Step 3: Build/copy complete Compose app image**

build-lyrics-glass.ps1 accepts DestinationDirectory, runs gradlew.bat createDistributable --no-daemon, locates the FlowCast Lyrics Glass app-image, verifies any deleted destination remains under DestinationDirectory, then copies the entire tree under glass. Do not copy only the EXE: Compose needs its bundled runtime/libraries.

Add a Publish-only BuildLyricsGlassRenderer target to AudioShare.Lyrics.csproj. It invokes that script before Publish only when BuildLyricsGlassRenderer is true. Normal dotnet test must not invoke Gradle.

publish-lyrics.ps1 publishes AudioShare.Lyrics self-contained with BuildLyricsGlassRenderer=true, creates a separate FlowCast-Lyrics-win-x64.zip and SHA-256, and never calls publish-release.ps1 or changes the main app package.

- [ ] **Step 4: Update notices and README**

Add complete Apache-2.0 attribution/license text for AndroidLiquidGlass / Backdrop 2.0.0 and Shapes 1.2.0. Remove notices for Microsoft.Web.WebView2, liquid-glass-react, React, and React DOM only after their files are absent.

Document that FlowCast Lyrics.exe auto-discovers the same Radmin VPN, shows a draggable glass capsule, provides Adjust Glass, has no lyric source yet, and does not require a separately installed Java runtime.

- [ ] **Step 5: Verify GREEN, local package, and sandbox**

Run:

    Set-Location audio-share\lyrics-glass
    .\gradlew.bat test --no-daemon
    .\gradlew.bat createDistributable --no-daemon

    Set-Location ..\..
    dotnet test audio-share\AudioShare.sln --no-restore
    & audio-share\scripts\publish-lyrics.ps1 -OutputDirectory (Join-Path $PWD "audio-share\release\FlowCast-Lyrics-android-liquid-glass-win-x64")

In a Windows sandbox verify:
1. No WPF host appears in the taskbar; exactly one borderless topmost capsule appears.
2. Initial copy is 正在尋找 FlowCast.
3. A local pipe connection-state command changes copy to 已連線，等待歌詞 and never invents lyrics.
4. Drag, relaunch, and confirm saved position.
5. Right click exposes only Adjust Glass and Close FlowCast Lyrics; change one setting, Done, relaunch, confirm persistence.
6. Close FlowCast Lyrics.exe and confirm Get-Process "FlowCast Lyrics Glass" -ErrorAction SilentlyContinue finds no process.

- [ ] **Step 6: Commit**

    git add -- audio-share/scripts/build-lyrics-glass.ps1 audio-share/scripts/publish-lyrics.ps1 audio-share/src/AudioShare.Lyrics/AudioShare.Lyrics.csproj audio-share/ThirdPartyNotices.txt audio-share/README.md audio-share/tests/AudioShare.Core.Tests/LyricsGlassPackagingTests.cs
    git commit -m "build: package Android liquid glass renderer with Lyrics"

## Plan Self-Review

- Coverage: Tasks 1-3 implement actual C strong-liquid Backdrop visuals, capsule, drag, option sheet, and reserved lyric contract. Tasks 4-5 implement IPC, persistence, process supervision, connection-only Radmin state, and removal of rdev/WebView visuals. Task 6 completes separate packaging, notices, and Windows verification.
- Scope: no FlowCast main-app/audio routing changes, no new Radmin ports or manual addressing, and no lyric source/transport/fabricated text.
- Consistency: both languages use version 1, same state strings, big-endian 65,536-byte frames, and the same five native Backdrop settings.
- Placeholder scan: every task includes exact files, test-first behavior, expected commands/results, and a commit boundary.
