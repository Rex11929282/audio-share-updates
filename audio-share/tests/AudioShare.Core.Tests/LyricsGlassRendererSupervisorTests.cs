using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AudioShare.Core.Tests;

public sealed class LyricsGlassRendererSupervisorTests : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-lyrics-renderer-{Guid.NewGuid():N}");
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task StartAsync_HandshakesOverANewSecurePipeAndSendsTheLatestState()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);

        var start = supervisor.StartAsync(timeout.Token);
        var launch = await launcher.NextLaunchAsync(timeout.Token);

        Assert.False(launch.StartInfo.UseShellExecute);
        Assert.True(launch.StartInfo.CreateNoWindow);
        Assert.Equal("FlowCast Lyrics Glass.exe", Path.GetFileName(launch.StartInfo.FileName));
        Assert.Equal(Path.GetDirectoryName(launch.StartInfo.FileName), launch.StartInfo.WorkingDirectory);
        Assert.Equal(["--pipe", launch.PipeName, "--token", launch.Token], launch.StartInfo.ArgumentList);
        Assert.NotEqual(Guid.Empty, Guid.ParseExact(launch.PipeName["flowcast-lyrics-".Length..], "N"));
        Assert.Equal(32, Convert.FromBase64String(launch.Token).Length);

        await supervisor.SetConnectionStateAsync(
            global::AudioShare.Lyrics.LyricsGlassConnectionState.ConnectedAwaitingLyrics,
            timeout.Token);

        await using var client = await RendererClient.ConnectAsync(launch, timeout.Token);
        await client.SendEventAsync($"{{\"version\":1,\"type\":\"hello\",\"token\":{JsonSerializer.Serialize(launch.Token)}}}", timeout.Token);

        using (var initialize = JsonDocument.Parse(await client.ReadHostCommandAsync(timeout.Token)))
        {
            var root = initialize.RootElement;
            Assert.Equal("initialize", root.GetProperty("type").GetString());
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal(launch.Token, root.GetProperty("token").GetString());
            Assert.Equal(120.5, root.GetProperty("position").GetProperty("x").GetDouble());
            Assert.Equal(-42.25, root.GetProperty("position").GetProperty("y").GetDouble());
            Assert.Equal(0.62, root.GetProperty("glass").GetProperty("refractionAmountFraction").GetDouble());
        }

        await client.SendEventAsync("{\"version\":1,\"type\":\"ready\"}", timeout.Token);
        var initialStateCommand = client.ReadHostCommandAsync(timeout.Token);
        await start.WaitAsync(timeout.Token);
        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Available, supervisor.Availability);

        using (var initialState = JsonDocument.Parse(await initialStateCommand))
        {
            Assert.Equal("connection-state", initialState.RootElement.GetProperty("type").GetString());
            Assert.Equal("connected-awaiting-lyrics", initialState.RootElement.GetProperty("state").GetString());
        }

        var updatedStateCommand = client.ReadHostCommandAsync(timeout.Token);
        await supervisor.SetConnectionStateAsync(
            global::AudioShare.Lyrics.LyricsGlassConnectionState.FindingFlowcast,
            timeout.Token);

        using var state = JsonDocument.Parse(await updatedStateCommand);
        Assert.Equal("connection-state", state.RootElement.GetProperty("type").GetString());
        Assert.Equal("finding-flowcast", state.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task StartAsync_WrongHelloTokenMarksTheRendererUnavailableAndWritesADiagnostic()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);

        var start = supervisor.StartAsync(timeout.Token);
        var launch = await launcher.NextLaunchAsync(timeout.Token);
        await using var client = await RendererClient.ConnectAsync(launch, timeout.Token);
        await client.SendEventAsync("{\"version\":1,\"type\":\"hello\",\"token\":\"wrong\"}", timeout.Token);

        await start.WaitAsync(timeout.Token);

        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Unavailable, supervisor.Availability);
        var diagnostic = await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.log"), timeout.Token);
        Assert.Contains("renderer", diagnostic);
        Assert.Contains("stage=handshake", diagnostic);
        Assert.True(launch.Process.HasExited);
        Assert.Equal(1, launch.Process.KillCount);
    }

    [Fact]
    public async Task StartAsync_ImmediateProcessExitCleansUpOnceWithoutHanging()
    {
        var launcher = new FakeRendererLauncher(exitImmediately: true);
        await using var supervisor = CreateSupervisor(launcher);

        await supervisor.StartAsync(timeout.Token).WaitAsync(timeout.Token);

        var launch = Assert.Single(launcher.Launches);
        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Unavailable, supervisor.Availability);
        Assert.Equal(1, launch.Process.DisposeCount);
        Assert.True(File.Exists(Path.Combine(directory, "diagnostics.log")));
    }

    [Fact]
    public async Task CancelledStartCanBeStartedAgain()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        using var cancelledStart = new CancellationTokenSource();

        var firstStart = supervisor.StartAsync(cancelledStart.Token);
        await launcher.NextLaunchAsync(timeout.Token);
        cancelledStart.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstStart.WaitAsync(timeout.Token));

        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Stopped, supervisor.Availability);

        var secondStart = supervisor.StartAsync(timeout.Token);
        var secondLaunch = await launcher.NextLaunchAsync(timeout.Token);
        await using var client = await RendererClient.ConnectAsync(secondLaunch, timeout.Token);
        await client.SendEventAsync($"{{\"version\":1,\"type\":\"hello\",\"token\":{JsonSerializer.Serialize(secondLaunch.Token)}}}", timeout.Token);
        await client.ReadHostCommandAsync(timeout.Token);
        await client.SendEventAsync("{\"version\":1,\"type\":\"ready\"}", timeout.Token);
        await client.ReadHostCommandAsync(timeout.Token);
        await secondStart.WaitAsync(timeout.Token);

        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Available, supervisor.Availability);
    }

    [Fact]
    public async Task ReadyRenderer_RaisesTypedEventsAndRejectsInvalidRendererPayloads()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        var settingsCommitted = new TaskCompletionSource<global::AudioShare.Lyrics.LyricsGlassSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var positionChanged = new TaskCompletionSource<global::AudioShare.Lyrics.OverlayPosition>(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.SettingsCommitted += (_, settings) => settingsCommitted.TrySetResult(settings.Settings);
        supervisor.PositionChanged += (_, position) => positionChanged.TrySetResult(position.Position);

        await using var client = await StartReadyAsync(supervisor, launcher);
        await client.SendEventAsync("{\"version\":1,\"type\":\"settings-committed\",\"glass\":{\"cornerRadiusFraction\":0.8,\"blurRadiusDp\":4,\"refractionHeightFraction\":0.3,\"refractionAmountFraction\":0.6,\"chromaticAberration\":false}}", timeout.Token);
        await client.SendEventAsync("{\"version\":1,\"type\":\"position-changed\",\"x\":12.5,\"y\":-8.25}", timeout.Token);

        Assert.Equal(0.8, (await settingsCommitted.Task.WaitAsync(timeout.Token)).CornerRadiusFraction);
        Assert.Equal(new global::AudioShare.Lyrics.OverlayPosition(12.5, -8.25), await positionChanged.Task.WaitAsync(timeout.Token));

        var malformedLauncher = new FakeRendererLauncher();
        await using var malformedSupervisor = CreateSupervisor(malformedLauncher);
        await using var malformedClient = await StartReadyAsync(malformedSupervisor, malformedLauncher);
        await malformedClient.SendEventAsync("{\"version\":1,\"type\":\"position-changed\",\"x\":12.5,\"y\":\"not-a-number\"}", timeout.Token);

        await WaitForAvailabilityAsync(malformedSupervisor, global::AudioShare.Lyrics.RendererAvailability.Unavailable, timeout.Token);
        var diagnostic = await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.log"), timeout.Token);
        Assert.Contains("stage=protocol", diagnostic);
    }

    [Fact]
    public async Task CloseRequest_RemainsConnectedUntilHostSendsShutdownAndDoesNotRestart()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        var closeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.CloseRequested += (_, _) => closeRequested.TrySetResult();
        await using var client = await StartReadyAsync(supervisor, launcher);

        await client.SendEventAsync("{\"version\":1,\"type\":\"close-request\"}", timeout.Token);
        await closeRequested.Task.WaitAsync(timeout.Token);
        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Available, supervisor.Availability);

        var shutdownCommand = client.ReadHostCommandAsync(timeout.Token);
        await supervisor.StopAsync(timeout.Token);

        using var shutdown = JsonDocument.Parse(await shutdownCommand);
        Assert.Equal("shutdown", shutdown.RootElement.GetProperty("type").GetString());
        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Stopped, supervisor.Availability);
        Assert.Single(launcher.Launches);
        Assert.False(File.Exists(Path.Combine(directory, "diagnostics.log")));
    }

    [Fact]
    public async Task ReadyRenderer_SurfacesFaultAndRejectsInvalidSettings()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        var faulted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.Faulted += (_, fault) => faulted.TrySetResult(fault.Message);
        await using var client = await StartReadyAsync(supervisor, launcher);

        await client.SendEventAsync("{\"version\":1,\"type\":\"fault\",\"message\":\"glass render failed\"}", timeout.Token);

        Assert.Equal("glass render failed", await faulted.Task.WaitAsync(timeout.Token));
        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Available, supervisor.Availability);

        await client.SendEventAsync("{\"version\":1,\"type\":\"settings-committed\",\"glass\":{\"cornerRadiusFraction\":1.1,\"blurRadiusDp\":4,\"refractionHeightFraction\":0.3,\"refractionAmountFraction\":0.6,\"chromaticAberration\":false}}", timeout.Token);

        await WaitForAvailabilityAsync(supervisor, global::AudioShare.Lyrics.RendererAvailability.Unavailable, timeout.Token);
        var diagnostic = await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.log"), timeout.Token);
        Assert.Contains("stage=protocol", diagnostic);
    }

    [Fact]
    public async Task ReadyRenderer_RejectsUnexpectedPayloadProperties()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        await using var client = await StartReadyAsync(supervisor, launcher);

        await client.SendEventAsync("{\"version\":1,\"type\":\"position-changed\",\"x\":12.5,\"y\":-8.25,\"unexpected\":true}", timeout.Token);

        await WaitForAvailabilityAsync(supervisor, global::AudioShare.Lyrics.RendererAvailability.Unavailable, timeout.Token);
        var diagnostic = await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.log"), timeout.Token);
        Assert.Contains("stage=protocol", diagnostic);
    }

    [Fact]
    public async Task StopAsync_AfterCloseRequestAndClientDisconnectIsBestEffort()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        var closeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.CloseRequested += (_, _) => closeRequested.TrySetResult();
        var client = await StartReadyAsync(supervisor, launcher);

        await client.SendEventAsync("{\"version\":1,\"type\":\"close-request\"}", timeout.Token);
        await closeRequested.Task.WaitAsync(timeout.Token);
        await client.DisposeAsync();

        await supervisor.StopAsync(timeout.Token);

        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Stopped, supervisor.Availability);
        Assert.Single(launcher.Launches);
        Assert.False(File.Exists(Path.Combine(directory, "diagnostics.log")));
    }

    [Fact]
    public async Task SetConnectionStateAsync_RejectsUnknownHostStateWithoutRestarting()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        await using var client = await StartReadyAsync(supervisor, launcher);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => supervisor.SetConnectionStateAsync(
            (global::AudioShare.Lyrics.LyricsGlassConnectionState)int.MaxValue,
            timeout.Token));

        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Available, supervisor.Availability);
        Assert.Single(launcher.Launches);
        Assert.False(File.Exists(Path.Combine(directory, "diagnostics.log")));
    }

    [Fact]
    public async Task UnexpectedExit_RestartsOnceWithNewCredentialsThenBecomesUnavailable()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        await using var firstClient = await StartReadyAsync(supervisor, launcher);
        var firstLaunch = launcher.Launches.Single();

        firstLaunch.Process.ExitUnexpectedly();
        var secondLaunch = await launcher.NextLaunchAsync(timeout.Token);
        Assert.NotEqual(firstLaunch.PipeName, secondLaunch.PipeName);
        Assert.NotEqual(firstLaunch.Token, secondLaunch.Token);

        await using var secondClient = await RendererClient.ConnectAsync(secondLaunch, timeout.Token);
        await secondClient.SendEventAsync($"{{\"version\":1,\"type\":\"hello\",\"token\":{JsonSerializer.Serialize(secondLaunch.Token)}}}", timeout.Token);
        await secondClient.ReadHostCommandAsync(timeout.Token);
        await secondClient.SendEventAsync("{\"version\":1,\"type\":\"ready\"}", timeout.Token);
        var restartedStateCommand = secondClient.ReadHostCommandAsync(timeout.Token);
        await WaitForAvailabilityAsync(supervisor, global::AudioShare.Lyrics.RendererAvailability.Available, timeout.Token);
        await restartedStateCommand;

        secondLaunch.Process.ExitUnexpectedly();
        await WaitForAvailabilityAsync(supervisor, global::AudioShare.Lyrics.RendererAvailability.Unavailable, timeout.Token);
        Assert.Equal(2, launcher.Launches.Count);
        var diagnostic = await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.log"), timeout.Token);
        Assert.Contains("stage=process-exit", diagnostic);
    }

    [Fact]
    public async Task StopAsync_SendsShutdownWithoutRestartingTheRenderer()
    {
        var launcher = new FakeRendererLauncher();
        await using var supervisor = CreateSupervisor(launcher);
        await using var client = await StartReadyAsync(supervisor, launcher);

        var shutdownCommand = client.ReadHostCommandAsync(timeout.Token);
        await supervisor.StopAsync(timeout.Token);

        using var shutdown = JsonDocument.Parse(await shutdownCommand);
        Assert.Equal("shutdown", shutdown.RootElement.GetProperty("type").GetString());
        Assert.Equal(global::AudioShare.Lyrics.RendererAvailability.Stopped, supervisor.Availability);
        Assert.Single(launcher.Launches);
        Assert.Empty(Directory.Exists(directory) ? Directory.GetFiles(directory) : []);
    }

    public async ValueTask DisposeAsync()
    {
        timeout.Cancel();
        timeout.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        await Task.CompletedTask;
    }

    private global::AudioShare.Lyrics.LyricsGlassRendererSupervisor CreateSupervisor(FakeRendererLauncher launcher) => new(
        launcher,
        new global::AudioShare.Lyrics.LyricsDiagnosticLog(Path.Combine(directory, "diagnostics.log")),
        new global::AudioShare.Lyrics.LyricsGlassHostState(
            new global::AudioShare.Lyrics.OverlayPosition(120.5, -42.25),
            global::AudioShare.Lyrics.LyricsGlassSettings.Defaults),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromMilliseconds(100));

    private async Task<RendererClient> StartReadyAsync(
        global::AudioShare.Lyrics.LyricsGlassRendererSupervisor supervisor,
        FakeRendererLauncher launcher)
    {
        var start = supervisor.StartAsync(timeout.Token);
        var launch = await launcher.NextLaunchAsync(timeout.Token);
        var client = await RendererClient.ConnectAsync(launch, timeout.Token);
        await client.SendEventAsync($"{{\"version\":1,\"type\":\"hello\",\"token\":{JsonSerializer.Serialize(launch.Token)}}}", timeout.Token);
        await client.ReadHostCommandAsync(timeout.Token);
        await client.SendEventAsync("{\"version\":1,\"type\":\"ready\"}", timeout.Token);
        var initialStateCommand = client.ReadHostCommandAsync(timeout.Token);
        await start.WaitAsync(timeout.Token);
        await initialStateCommand;
        return client;
    }

    private static async Task WaitForAvailabilityAsync(
        global::AudioShare.Lyrics.LyricsGlassRendererSupervisor supervisor,
        global::AudioShare.Lyrics.RendererAvailability expected,
        CancellationToken cancellationToken)
    {
        while (supervisor.Availability != expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private sealed class FakeRendererLauncher(bool exitImmediately = false) : global::AudioShare.Lyrics.ILyricsGlassRendererProcessLauncher
    {
        private readonly List<FakeLaunch> launches = [];
        private readonly SemaphoreSlim launchSignal = new(0);

        public IReadOnlyList<FakeLaunch> Launches => launches;

        public global::AudioShare.Lyrics.ILyricsGlassRendererProcess Launch(ProcessStartInfo startInfo)
        {
            var arguments = startInfo.ArgumentList.ToArray();
            var process = new FakeRendererProcess();
            if (exitImmediately)
            {
                process.ExitUnexpectedly();
            }

            var launch = new FakeLaunch(
                startInfo,
                arguments[1],
                arguments[3],
                process);
            launches.Add(launch);
            launchSignal.Release();
            return launch.Process;
        }

        public async Task<FakeLaunch> NextLaunchAsync(CancellationToken cancellationToken)
        {
            await launchSignal.WaitAsync(cancellationToken);
            return launches[^1];
        }
    }

    private sealed record FakeLaunch(
        ProcessStartInfo StartInfo,
        string PipeName,
        string Token,
        FakeRendererProcess Process);

    private sealed class FakeRendererProcess : global::AudioShare.Lyrics.ILyricsGlassRendererProcess
    {
        private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? Exited;

        public bool HasExited { get; private set; }

        public int KillCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => exited.Task.WaitAsync(cancellationToken);

        public void KillTree()
        {
            KillCount++;
            ExitUnexpectedly();
        }

        public void ExitUnexpectedly()
        {
            if (HasExited)
            {
                return;
            }

            HasExited = true;
            exited.TrySetResult();
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class RendererClient : IAsyncDisposable
    {
        private readonly NamedPipeClientStream stream;

        private RendererClient(NamedPipeClientStream stream) => this.stream = stream;

        public static async Task<RendererClient> ConnectAsync(FakeLaunch launch, CancellationToken cancellationToken)
        {
            var stream = new NamedPipeClientStream(
                ".",
                launch.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await stream.ConnectAsync(cancellationToken);
            return new RendererClient(stream);
        }

        public Task SendEventAsync(string json, CancellationToken cancellationToken) =>
            global::AudioShare.Lyrics.LyricsGlassPipeProtocol.WriteAsync(stream, json, cancellationToken);

        public async Task<string> ReadHostCommandAsync(CancellationToken cancellationToken)
        {
            var header = new byte[sizeof(int)];
            await ReadExactlyAsync(stream, header, cancellationToken);
            var size = BinaryPrimitives.ReadInt32BigEndian(header);
            var body = new byte[size];
            await ReadExactlyAsync(stream, body, cancellationToken);
            return Encoding.UTF8.GetString(body);
        }

        public ValueTask DisposeAsync() => stream.DisposeAsync();

        private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("The renderer pipe closed before a host command was complete.");
                }

                offset += read;
            }
        }
    }
}
