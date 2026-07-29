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
        var factory = new FakeSourceFactory((session, format) =>
            session.ProcessId == chrome.ProcessId
                ? new FakeAudioSource("chrome", format, [0.2f, 0.2f])
                : new FakeAudioSource("netease", format, [0.7f, 0.7f]));
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
        var factory = new FakeSourceFactory((_, format) => new FakeAudioSource("discord", format, [0.3f, 0.3f]));
        await using var engine = new SelectedSourceEngine(
            factory,
            new RecordingWriter(),
            null,
            new SelectedSourceEngineOptions(new AudioFormat(48_000, 2), false));

        await engine.SynchronizeAsync([discord], CancellationToken.None);

        Assert.Empty(factory.CreatedSessions);
    }

    private sealed class FakeSourceFactory(Func<AudioSession, AudioFormat, IAudioSource> create) : ISelectedAudioSourceFactory
    {
        public List<AudioSession> CreatedSessions { get; } = [];

        public IAudioSource Create(AudioSession session, AudioFormat format)
        {
            CreatedSessions.Add(session);
            return create(session, format);
        }
    }

    private sealed class FakeAudioSource(string sourceId, AudioFormat format, float[] samples) : IAudioSource
    {
        public bool Disposed { get; private set; }

        public string SourceId { get; } = sourceId;

        public Task<AudioFrame?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AudioFrame?>(new AudioFrame(format, 1, samples));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingWriter : IAudioOutputWriter
    {
        public List<AudioFrame> Frames { get; } = [];

        public Task WriteAsync(AudioFrame frame, CancellationToken cancellationToken)
        {
            Frames.Add(frame);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
