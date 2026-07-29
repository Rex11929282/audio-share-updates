using AudioShare.Core;
using AudioShare.Engine;

namespace AudioShare.Core.Tests;

public sealed class SelectedSourceEngineTests
{
    [Fact]
    public async Task MixOnceAsync_WritesOnlyFramesCreatedForCurrentlySelectedProcesses()
    {
        var chrome = new AudioSession(20, 200, "chrome.exe", "Chrome", true);
        var netease = new AudioSession(21, 201, "cloudmusic.exe", "NetEase", true);
        var factory = new FakeSourceFactory((session, _) =>
            session.ProcessId == chrome.ProcessId
                ? new FakeAudioSource("chrome", new AudioFormat(48_000, 2), [0.2f, 0.2f])
                : new FakeAudioSource("netease", new AudioFormat(48_000, 2), [0.7f, 0.7f]));
        var writer = new RecordingWriter();
        await using var engine = new SelectedSourceEngine(
            factory,
            writer,
            null,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

        await engine.SynchronizeAsync([chrome], CancellationToken.None);
        await engine.MixOnceAsync(CancellationToken.None);

        Assert.Equal([0.2f, 0.2f], Assert.Single(writer.Frames).Samples);
    }

    [Fact]
    public async Task SynchronizeAsync_DeselectingProcessDisposesSourceAndWritesSilence()
    {
        var chrome = new AudioSession(20, 200, "chrome.exe", "Chrome", true);
        var source = new FakeAudioSource("chrome", new AudioFormat(48_000, 2), [0.2f, 0.2f]);
        var writer = new RecordingWriter();
        await using var engine = new SelectedSourceEngine(
            new FakeSourceFactory((_, _) => source),
            writer,
            null,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

        await engine.SynchronizeAsync([chrome], CancellationToken.None);
        await engine.SynchronizeAsync([], CancellationToken.None);
        await engine.MixOnceAsync(CancellationToken.None);

        Assert.True(source.Disposed);
        Assert.Empty(Assert.Single(writer.Frames).Samples);
    }

    [Fact]
    public async Task SynchronizeAsync_RejectsDiscordBeforeCreatingSource()
    {
        var discord = new AudioSession(22, 202, "discord.exe", "Discord", true);
        var factory = new FakeSourceFactory((_, _) => new FakeAudioSource("discord", new AudioFormat(48_000, 2), [0.3f, 0.3f]));
        await using var engine = new SelectedSourceEngine(
            factory,
            new RecordingWriter(),
            null,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

        await engine.SynchronizeAsync([discord], CancellationToken.None);

        Assert.Empty(factory.CreatedSessions);
    }

    [Fact]
    public async Task SynchronizeAsync_IgnoresInactiveSessions()
    {
        var inactive = new AudioSession(23, 203, "inactive.exe", "Inactive", false);
        var factory = new FakeSourceFactory((_, _) => new FakeAudioSource("inactive", new AudioFormat(48_000, 2), [0.3f, 0.3f]));
        await using var engine = CreateEngine(factory);

        await engine.SynchronizeAsync([inactive], CancellationToken.None);

        Assert.Empty(factory.CreatedSessions);
    }

    [Fact]
    public async Task SynchronizeAsync_RejectsNullSession()
    {
        await using var engine = CreateEngine(new FakeSourceFactory((_, _) => new FakeAudioSource("x", new AudioFormat(48_000, 2), [0.1f, 0.1f])));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.SynchronizeAsync([null!], CancellationToken.None));
    }

    [Fact]
    public async Task SynchronizeAsync_RejectsDuplicateProcessIdentity()
    {
        var session = new AudioSession(24, 204, "app.exe", "App", true);
        await using var engine = CreateEngine(new FakeSourceFactory((_, _) => new FakeAudioSource("x", new AudioFormat(48_000, 2), [0.1f, 0.1f])));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.SynchronizeAsync([session, session], CancellationToken.None));
    }

    [Fact]
    public async Task SynchronizeAsync_ReplacesSourceWhenPidIsReused()
    {
        var original = new AudioSession(25, 205, "app.exe", "Original", true);
        var replacement = new AudioSession(25, 206, "app.exe", "Replacement", true);
        var sources = new Queue<FakeAudioSource>();
        var writer = new RecordingWriter();
        var factory = new FakeSourceFactory((session, _) =>
        {
            float[] samples = session.ProcessStartUtcTicks == original.ProcessStartUtcTicks
                ? [0.1f, 0.1f]
                : [0.9f, 0.9f];
            var source = new FakeAudioSource("app", new AudioFormat(48_000, 2), samples);
            sources.Enqueue(source);
            return source;
        });
        await using var engine = CreateEngine(factory, writer);

        await engine.SynchronizeAsync([original], CancellationToken.None);
        await engine.SynchronizeAsync([replacement], CancellationToken.None);
        await engine.MixOnceAsync(CancellationToken.None);

        var originalSource = sources.Dequeue();
        Assert.True(originalSource.Disposed);
        Assert.Equal(2, factory.CreatedSessions.Count);
        Assert.Equal(replacement, factory.CreatedSessions[1]);
        Assert.Equal([0.9f, 0.9f], Assert.Single(writer.Frames).Samples);
    }

    [Fact]
    public async Task SynchronizeAsync_PassesCancellationTokenToFactory()
    {
        var session = new AudioSession(27, 208, "app.exe", "App", true);
        var factory = new FakeSourceFactory((_, _) => new FakeAudioSource("app", new AudioFormat(48_000, 2), [0.1f, 0.1f]));
        await using var engine = CreateEngine(factory);
        using var cancellationSource = new CancellationTokenSource();

        await engine.SynchronizeAsync([session], cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, factory.LastCancellationToken);
    }

    [Fact]
    public async Task MixOnceAsync_DisposesAndRemovesCompletedSource()
    {
        var session = new AudioSession(26, 207, "app.exe", "App", true);
        var source = new FakeAudioSource("app", new AudioFormat(48_000, 2), null);
        var writer = new RecordingWriter();
        await using var engine = new SelectedSourceEngine(
            new FakeSourceFactory((_, _) => source), writer, null,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

        await engine.SynchronizeAsync([session], CancellationToken.None);
        await engine.MixOnceAsync(CancellationToken.None);
        await engine.MixOnceAsync(CancellationToken.None);

        Assert.True(source.Disposed);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(2, writer.Frames.Count);
    }

    [Fact]
    public async Task MixOnceAsync_ReadsMicrophoneOnlyWhenEnabled()
    {
        var microphone = new FakeAudioSource("microphone", new AudioFormat(48_000, 2), [0.4f, 0.4f]);
        await using var disabled = new SelectedSourceEngine(
            new FakeSourceFactory((_, _) => new FakeAudioSource("x", new AudioFormat(48_000, 2), null)), new RecordingWriter(), microphone,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

        await disabled.MixOnceAsync(CancellationToken.None);

        Assert.Equal(0, microphone.ReadCount);
    }

    [Fact]
    public async Task MixOnceAsync_ReadsMicrophoneWhenEnabled()
    {
        var microphone = new FakeAudioSource("microphone", new AudioFormat(48_000, 2), [0.4f, 0.4f]);
        var writer = new RecordingWriter();
        await using var engine = new SelectedSourceEngine(
            new FakeSourceFactory((_, _) => new FakeAudioSource("x", new AudioFormat(48_000, 2), null)), writer, microphone,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), true));

        await engine.MixOnceAsync(CancellationToken.None);

        Assert.Equal(1, microphone.ReadCount);
        Assert.Equal([0.4f, 0.4f], Assert.Single(writer.Frames).Samples);
    }

    [Fact]
    public async Task MixOnceAsync_WritesExactlyOneFrame()
    {
        var writer = new RecordingWriter();
        await using var engine = CreateEngine(new FakeSourceFactory((_, _) => new FakeAudioSource("x", new AudioFormat(48_000, 2), null)), writer);

        await engine.MixOnceAsync(CancellationToken.None);

        Assert.Equal(1, writer.WriteCount);
    }

    private static SelectedSourceEngine CreateEngine(FakeSourceFactory factory, RecordingWriter? writer = null) =>
        new(factory, writer ?? new RecordingWriter(), null, new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

    private sealed class FakeSourceFactory(Func<AudioSession, CancellationToken, IAudioSource> create) : ISelectedProcessSourceFactory
    {
        public List<AudioSession> CreatedSessions { get; } = [];

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<IAudioSource> CreateAsync(AudioSession session, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            CreatedSessions.Add(session);
            return Task.FromResult(create(session, cancellationToken));
        }
    }

    private sealed class FakeAudioSource(string sourceId, AudioFormat format, float[]? samples) : IAudioSource
    {
        private readonly float[]? samples = samples;

        public bool Disposed { get; private set; }

        public int ReadCount { get; private set; }

        public string SourceId { get; } = sourceId;

        public Task<AudioFrame?> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<AudioFrame?>(samples is null ? null : new AudioFrame(format, 1, samples));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingWriter : IAudioOutputWriter
    {
        public List<AudioFrame> Frames { get; } = [];

        public int WriteCount { get; private set; }

        public Task WriteAsync(AudioFrame frame, CancellationToken cancellationToken)
        {
            WriteCount++;
            Frames.Add(frame);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
