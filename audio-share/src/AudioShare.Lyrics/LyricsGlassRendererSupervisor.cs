using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioShare.Lyrics;

internal enum RendererAvailability
{
    Starting,
    Available,
    Unavailable,
    Stopped
}

internal enum LyricsGlassConnectionState
{
    FindingFlowcast,
    ConnectedAwaitingLyrics
}

internal sealed class LyricsGlassSettingsCommittedEventArgs(LyricsGlassSettings settings) : EventArgs
{
    internal LyricsGlassSettings Settings { get; } = settings;
}

internal sealed class LyricsGlassPositionChangedEventArgs(OverlayPosition position) : EventArgs
{
    internal OverlayPosition Position { get; } = position;
}

internal sealed class LyricsGlassFaultedEventArgs(string message) : EventArgs
{
    internal string Message { get; } = message;
}

internal interface ILyricsGlassRendererProcess : IDisposable
{
    event EventHandler? Exited;

    bool HasExited { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void KillTree();
}

internal interface ILyricsGlassRendererProcessLauncher
{
    ILyricsGlassRendererProcess Launch(ProcessStartInfo startInfo);
}

internal sealed class LyricsGlassRendererSupervisor : IAsyncDisposable
{
    private const string HelperName = "FlowCast Lyrics Glass.exe";
    private static readonly TimeSpan ProductionHandshakeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProductionStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly object stateGate = new();
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ILyricsGlassRendererProcessLauncher processLauncher;
    private readonly LyricsDiagnosticLog diagnosticLog;
    private readonly string helperPath;
    private readonly TimeSpan handshakeTimeout;
    private readonly TimeSpan stopTimeout;
    private RendererRun? currentRun;
    private RendererAvailability availability = RendererAvailability.Stopped;
    private LyricsGlassHostState latestHostState;
    private LyricsGlassConnectionState latestConnectionState = LyricsGlassConnectionState.FindingFlowcast;
    private long latestConnectionStateRevision;
    private bool stopping;
    private bool disposeStarted;
    private bool disposed;
    private Task? disposeTask;

    internal LyricsGlassRendererSupervisor()
        : this(
            new ProcessRendererProcessLauncher(),
            LyricsDiagnosticLog.CreateDefault(),
            LyricsGlassHostState.Defaults,
            ProductionHandshakeTimeout,
            ProductionStopTimeout)
    {
    }

    internal LyricsGlassRendererSupervisor(
        ILyricsGlassRendererProcessLauncher processLauncher,
        LyricsDiagnosticLog diagnosticLog,
        LyricsGlassHostState initialState,
        TimeSpan handshakeTimeout,
        TimeSpan stopTimeout)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(diagnosticLog);
        ArgumentNullException.ThrowIfNull(initialState);
        if (!initialState.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(initialState));
        }

        if (handshakeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout));
        }

        if (stopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stopTimeout));
        }

        this.processLauncher = processLauncher;
        this.diagnosticLog = diagnosticLog;
        latestHostState = initialState;
        this.handshakeTimeout = handshakeTimeout;
        this.stopTimeout = stopTimeout;
        helperPath = Path.Combine(AppContext.BaseDirectory, "glass", HelperName);
    }

    internal event EventHandler<LyricsGlassSettingsCommittedEventArgs>? SettingsCommitted;

    internal event EventHandler<LyricsGlassPositionChangedEventArgs>? PositionChanged;

    internal event EventHandler? CloseRequested;

    internal event EventHandler<LyricsGlassFaultedEventArgs>? Faulted;

    internal RendererAvailability Availability
    {
        get
        {
            lock (stateGate)
            {
                return availability;
            }
        }
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            lock (stateGate)
            {
                ThrowIfDisposed();
                if (availability is RendererAvailability.Available or RendererAvailability.Starting)
                {
                    return;
                }

                stopping = false;
                availability = RendererAvailability.Starting;
            }

            try
            {
                await StartRunAsync(restartAttempt: 0, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (stateGate)
                {
                    if (availability == RendererAvailability.Starting)
                    {
                        availability = RendererAvailability.Stopped;
                    }
                }

                throw;
            }
            catch (Exception exception)
            {
                MarkUnavailable("handshake", exception);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    internal async Task SetConnectionStateAsync(
        LyricsGlassConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _ = ToProtocolValue(connectionState);
        RendererRun? run;
        lock (stateGate)
        {
            latestConnectionState = connectionState;
            latestConnectionStateRevision++;
            run = currentRun is { Ready: true } candidate && !stopping ? candidate : null;
        }

        if (run is null)
        {
            return;
        }

        try
        {
            await SendLatestConnectionStateAsync(run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await HandleUnexpectedTerminationAsync(run, "pipe-write", exception, allowRestart: true);
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? activeDispose = null;
        RendererRun? cancellingRun;
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            if (disposeStarted)
            {
                activeDispose = disposeTask;
                cancellingRun = null;
            }
            else
            {
                if (availability == RendererAvailability.Stopped)
                {
                    return;
                }

                stopping = true;
                cancellingRun = currentRun;
                if (cancellingRun is not null)
                {
                    cancellingRun.IntentionalShutdown = true;
                }
            }
        }

        if (activeDispose is not null)
        {
            await activeDispose;
            return;
        }

        if (cancellingRun is { Ready: false })
        {
            CancelRun(cancellingRun);
        }

        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            await StopCurrentRunUnderLifecycleAsync(cancellationToken);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        RendererRun? cancellingRun;
        lock (stateGate)
        {
            if (disposeTask is not null)
            {
                return new ValueTask(disposeTask);
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposeTask = completion.Task;
            disposeStarted = true;
            stopping = true;
            cancellingRun = currentRun;
            if (cancellingRun is not null)
            {
                cancellingRun.IntentionalShutdown = true;
            }
        }

        if (cancellingRun is { Ready: false })
        {
            CancelRun(cancellingRun);
        }

        _ = CompleteDisposeAsync(completion);
        return new ValueTask(completion.Task);
    }

    private async Task StartRunAsync(int restartAttempt, CancellationToken cancellationToken)
    {
        var run = new RendererRun(
            $"flowcast-lyrics-{Guid.NewGuid():N}",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            restartAttempt);
        lock (stateGate)
        {
            if (stopping || disposed)
            {
                throw new OperationCanceledException();
            }

            currentRun = run;
        }

        try
        {
            run.Process = processLauncher.Launch(CreateStartInfo(run));
            run.ProcessExitHandler = (_, _) => OnProcessExited(run);
            run.Process.Exited += run.ProcessExitHandler;
            if (run.Process.HasExited)
            {
                throw new InvalidOperationException("The FlowCast Lyrics renderer exited before connecting.");
            }

            using var helloCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, run.Cancellation.Token);
            helloCancellation.CancelAfter(handshakeTimeout);
            await run.Pipe.WaitForConnectionAsync(helloCancellation.Token);

            var hello = await ReadRequiredRendererEventAsync(run, helloCancellation.Token);
            ValidateHello(hello, run.Token);

            LyricsGlassHostState hostState;
            lock (stateGate)
            {
                hostState = latestHostState;
            }

            using var readyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, run.Cancellation.Token);
            readyCancellation.CancelAfter(handshakeTimeout);
            await SendCommandAsync(
                run,
                new
                {
                    version = LyricsGlassPipeProtocol.CurrentVersion,
                    type = "initialize",
                    token = run.Token,
                    position = hostState.Position,
                    glass = hostState.Glass
                },
                readyCancellation.Token);

            var ready = await ReadRequiredRendererEventAsync(run, readyCancellation.Token);
            ValidateReady(ready);

            lock (stateGate)
            {
                if (stopping || disposed || !ReferenceEquals(currentRun, run))
                {
                    throw new OperationCanceledException();
                }

                run.Ready = true;
                availability = RendererAvailability.Available;
            }

            await SendLatestConnectionStateAsync(run, readyCancellation.Token);
            _ = ReceiveEventsAsync(run);
        }
        catch (Exception exception)
        {
            if (run.Ready &&
                !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _ = ObserveUnexpectedTerminationAsync(run, "pipe-write", exception, allowRestart: true);
                return;
            }

            await CleanupRunAsync(run, waitForGracefulExit: false);
            lock (stateGate)
            {
                if (ReferenceEquals(currentRun, run))
                {
                    currentRun = null;
                }
            }

            throw;
        }
    }

    private void OnProcessExited(RendererRun run)
    {
        bool ready;
        lock (stateGate)
        {
            ready = run.Ready;
        }

        if (!ready)
        {
            try
            {
                run.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            return;
        }

        _ = ObserveUnexpectedTerminationAsync(
            run,
            "process-exit",
            new InvalidOperationException("The FlowCast Lyrics renderer exited unexpectedly."),
            allowRestart: true);
    }

    private ProcessStartInfo CreateStartInfo(RendererRun run)
    {
        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(helperPath)!
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(run.PipeName);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add(run.Token);
        return startInfo;
    }

    private async Task ReceiveEventsAsync(RendererRun run)
    {
        try
        {
            while (!run.Cancellation.IsCancellationRequested)
            {
                var json = await LyricsGlassPipeProtocol.ReadAsync(run.Pipe, run.Cancellation.Token);
                if (json is null)
                {
                    await HandleUnexpectedTerminationAsync(
                        run,
                        "pipe-closed",
                        new EndOfStreamException("The FlowCast Lyrics renderer pipe closed unexpectedly."),
                        allowRestart: true);
                    return;
                }

                await DispatchRendererEventAsync(run, json);
            }
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
        }
        catch (InvalidDataException exception)
        {
            await HandleUnexpectedTerminationAsync(run, "protocol", exception, allowRestart: false);
        }
        catch (Exception exception)
        {
            await HandleUnexpectedTerminationAsync(run, "pipe-read", exception, allowRestart: true);
        }
    }

    private Task DispatchRendererEventAsync(RendererRun run, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        switch (type)
        {
            case "settings-committed":
            {
                ValidateExactProperties(root, "version", "type", "glass");
                var settings = DeserializeRequired<LyricsGlassSettings>(root, "glass");
                if (!settings.IsValid)
                {
                    throw new InvalidDataException("Renderer glass settings are invalid.");
                }

                lock (stateGate)
                {
                    latestHostState = latestHostState with { Glass = settings };
                }

                SettingsCommitted?.Invoke(this, new LyricsGlassSettingsCommittedEventArgs(settings));
                return Task.CompletedTask;
            }

            case "position-changed":
            {
                ValidateExactProperties(root, "version", "type", "x", "y");
                var position = new OverlayPosition(
                    GetRequiredFiniteNumber(root, "x"),
                    GetRequiredFiniteNumber(root, "y"));
                if (!position.IsValid)
                {
                    throw new InvalidDataException("Renderer overlay position is invalid.");
                }

                lock (stateGate)
                {
                    latestHostState = latestHostState with { Position = position };
                }

                PositionChanged?.Invoke(this, new LyricsGlassPositionChangedEventArgs(position));
                return Task.CompletedTask;
            }

            case "close-request":
                ValidateExactProperties(root, "version", "type");
                MarkCleanClose(run);
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return Task.CompletedTask;

            case "fault":
                ValidateExactProperties(root, "version", "type", "message");
                Faulted?.Invoke(this, new LyricsGlassFaultedEventArgs(GetRequiredNonWhitespaceString(root, "message")));
                return Task.CompletedTask;

            default:
                throw new InvalidDataException("Renderer sent an event that is invalid after startup.");
        }
    }

    private async Task HandleUnexpectedTerminationAsync(
        RendererRun run,
        string stage,
        Exception exception,
        bool allowRestart)
    {
        if (Interlocked.Exchange(ref run.TerminationSignaled, 1) != 0)
        {
            return;
        }

        var shouldRestart = false;
        lock (stateGate)
        {
            if (disposed || stopping || run.IntentionalShutdown || !ReferenceEquals(currentRun, run))
            {
                return;
            }

            currentRun = null;
            shouldRestart = allowRestart && run.Ready && run.RestartAttempt == 0;
        }

        CancelRun(run);
        await CleanupRunAsync(run, waitForGracefulExit: false);
        if (!shouldRestart)
        {
            MarkUnavailable(stage, exception);
            return;
        }

        await lifecycleGate.WaitAsync();
        try
        {
            lock (stateGate)
            {
                if (disposed || stopping)
                {
                    return;
                }

                availability = RendererAvailability.Starting;
            }

            try
            {
                await StartRunAsync(run.RestartAttempt + 1, CancellationToken.None);
            }
            catch (Exception restartException)
            {
                MarkUnavailable("restart", restartException);
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task ObserveUnexpectedTerminationAsync(
        RendererRun run,
        string stage,
        Exception exception,
        bool allowRestart)
    {
        try
        {
            await HandleUnexpectedTerminationAsync(run, stage, exception, allowRestart);
        }
        catch (Exception unexpectedException)
        {
            MarkUnavailable(stage, unexpectedException);
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await lifecycleGate.WaitAsync(CancellationToken.None);
            try
            {
                await StopCurrentRunUnderLifecycleAsync(CancellationToken.None);
                lock (stateGate)
                {
                    disposed = true;
                    availability = RendererAvailability.Stopped;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }

            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (stateGate)
            {
                disposed = true;
            }

            completion.TrySetException(exception);
        }
    }

    private async Task StopCurrentRunUnderLifecycleAsync(CancellationToken shutdownCancellationToken)
    {
        RendererRun? run;
        lock (stateGate)
        {
            run = currentRun;
            if (run is null)
            {
                availability = RendererAvailability.Stopped;
                return;
            }

            run.IntentionalShutdown = true;
        }

        try
        {
            if (run.Ready && run.Pipe.IsConnected)
            {
                await SendCommandAsync(
                    run,
                    new { version = LyricsGlassPipeProtocol.CurrentVersion, type = "shutdown" },
                    shutdownCancellationToken);
            }
        }
        catch (Exception exception) when (
            shutdownCancellationToken.IsCancellationRequested ||
            run.Cancellation.IsCancellationRequested ||
            !run.Pipe.IsConnected ||
            exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            await CleanupRunAsync(run, waitForGracefulExit: true);
            lock (stateGate)
            {
                if (ReferenceEquals(currentRun, run))
                {
                    currentRun = null;
                }

                availability = RendererAvailability.Stopped;
            }
        }
    }

    private Task CleanupRunAsync(RendererRun run, bool waitForGracefulExit)
    {
        lock (run.CleanupGate)
        {
            return run.CleanupTask ??= CleanupRunCoreAsync(run, waitForGracefulExit);
        }
    }

    private async Task CleanupRunCoreAsync(RendererRun run, bool waitForGracefulExit)
    {
        CancelRun(run);
        run.Pipe.Dispose();
        var process = run.Process;
        if (process is null)
        {
            run.Cancellation.Dispose();
            return;
        }

        if (run.ProcessExitHandler is not null)
        {
            process.Exited -= run.ProcessExitHandler;
        }

        try
        {
            if (waitForGracefulExit)
            {
                using var timeoutCancellation = new CancellationTokenSource(stopTimeout);
                try
                {
                    await process.WaitForExitAsync(timeoutCancellation.Token);
                }
                catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
                {
                }
            }

            if (!process.HasExited)
            {
                await TerminateAndReapAsync(process);
            }
            else
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        finally
        {
            process.Dispose();
            run.Cancellation.Dispose();
        }
    }

    private static async Task TerminateAndReapAsync(ILyricsGlassRendererProcess process)
    {
        try
        {
            process.KillTree();
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (Win32Exception) when (process.HasExited)
        {
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }

    private static void CancelRun(RendererRun run)
    {
        try
        {
            run.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void MarkCleanClose(RendererRun run)
    {
        lock (stateGate)
        {
            if (!ReferenceEquals(currentRun, run))
            {
                return;
            }

            stopping = true;
            run.IntentionalShutdown = true;
        }

        CancelRun(run);
    }

    private void MarkUnavailable(string stage, Exception exception)
    {
        lock (stateGate)
        {
            if (disposed || stopping || availability == RendererAvailability.Unavailable)
            {
                return;
            }

            diagnosticLog.WriteRendererFailure(stage, exception);
            availability = RendererAvailability.Unavailable;
        }
    }

    private async Task<string> ReadRequiredRendererEventAsync(RendererRun run, CancellationToken cancellationToken)
    {
        var json = await LyricsGlassPipeProtocol.ReadAsync(run.Pipe, cancellationToken);
        return json ?? throw new EndOfStreamException("The FlowCast Lyrics renderer closed the pipe during its handshake.");
    }

    private async Task SendLatestConnectionStateAsync(RendererRun run, CancellationToken cancellationToken)
    {
        while (true)
        {
            LyricsGlassConnectionState connectionState;
            long revision;
            await writeGate.WaitAsync(cancellationToken);
            try
            {
                lock (stateGate)
                {
                    if (disposed || stopping || !ReferenceEquals(currentRun, run) || !run.Ready)
                    {
                        return;
                    }

                    connectionState = latestConnectionState;
                    revision = latestConnectionStateRevision;
                }

                await LyricsGlassPipeProtocol.WriteAsync(
                    run.Pipe,
                    JsonSerializer.Serialize(
                        new
                        {
                            version = LyricsGlassPipeProtocol.CurrentVersion,
                            type = "connection-state",
                            state = ToProtocolValue(connectionState)
                        },
                        JsonOptions),
                    cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }

            lock (stateGate)
            {
                if (revision == latestConnectionStateRevision ||
                    disposed ||
                    stopping ||
                    !ReferenceEquals(currentRun, run))
                {
                    return;
                }
            }
        }
    }

    private async Task SendCommandAsync(RendererRun run, object command, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await LyricsGlassPipeProtocol.WriteAsync(
                run.Pipe,
                JsonSerializer.Serialize(command, JsonOptions),
                cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static void ValidateHello(string json, string expectedToken)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateExactProperties(root, "version", "type", "token");
        if (root.GetProperty("type").GetString() != "hello" ||
            !string.Equals(GetRequiredNonWhitespaceString(root, "token"), expectedToken, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Renderer hello token is invalid.");
        }
    }

    private static void ValidateReady(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateExactProperties(root, "version", "type");
        if (root.GetProperty("type").GetString() != "ready")
        {
            throw new InvalidDataException("Renderer did not send ready after initialize.");
        }
    }

    private static T DeserializeRequired<T>(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidDataException($"Renderer event is missing {propertyName}.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(property.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException($"Renderer event has an invalid {propertyName}.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Renderer event has an invalid {propertyName}.", exception);
        }
    }

    private static double GetRequiredFiniteNumber(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw new InvalidDataException($"Renderer event has an invalid {propertyName}.");
        }

        return value;
    }

    private static string GetRequiredNonWhitespaceString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Renderer event has an invalid {propertyName}.");
        }

        return property.GetString()!;
    }

    private static void ValidateExactProperties(JsonElement root, params string[] expectedProperties)
    {
        var actualPropertyCount = 0;
        foreach (var property in root.EnumerateObject())
        {
            actualPropertyCount++;
            if (!expectedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Renderer event has an unexpected {property.Name} property.");
            }
        }

        if (actualPropertyCount != expectedProperties.Length ||
            expectedProperties.Any(propertyName => !root.TryGetProperty(propertyName, out _)))
        {
            throw new InvalidDataException("Renderer event properties are invalid.");
        }
    }

    private static string ToProtocolValue(LyricsGlassConnectionState connectionState) => connectionState switch
    {
        LyricsGlassConnectionState.FindingFlowcast => "finding-flowcast",
        LyricsGlassConnectionState.ConnectedAwaitingLyrics => "connected-awaiting-lyrics",
        _ => throw new ArgumentOutOfRangeException(nameof(connectionState))
    };

    private void ThrowIfDisposed()
    {
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposeStarted || disposed, this);
        }
    }

    private sealed class RendererRun(string pipeName, string token, int restartAttempt)
    {
        internal object CleanupGate { get; } = new();

        internal CancellationTokenSource Cancellation { get; } = new();

        internal NamedPipeServerStream Pipe { get; } = new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        internal string PipeName { get; } = pipeName;

        internal string Token { get; } = token;

        internal int RestartAttempt { get; } = restartAttempt;

        internal ILyricsGlassRendererProcess? Process { get; set; }

        internal EventHandler? ProcessExitHandler { get; set; }

        internal bool Ready { get; set; }

        internal bool IntentionalShutdown { get; set; }

        internal int TerminationSignaled;

        internal Task? CleanupTask { get; set; }
    }

    private sealed class ProcessRendererProcessLauncher : ILyricsGlassRendererProcessLauncher
    {
        public ILyricsGlassRendererProcess Launch(ProcessStartInfo startInfo)
        {
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Unable to start FlowCast Lyrics Glass.");
            process.EnableRaisingEvents = true;
            return new ProcessRendererProcess(process);
        }
    }

    private sealed class ProcessRendererProcess(Process process) : ILyricsGlassRendererProcess
    {
        public event EventHandler? Exited
        {
            add => process.Exited += value;
            remove => process.Exited -= value;
        }

        public bool HasExited => process.HasExited;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public void KillTree() => process.Kill(entireProcessTree: true);

        public void Dispose() => process.Dispose();
    }
}
