using AudioShare.Core;

namespace AudioShare.Engine;

public interface ISelectedAudioSourceFactory
{
    IAudioSource Create(AudioSession session, AudioFormat format);
}

public sealed class SelectedSourceEngine : IAsyncDisposable
{
    private static readonly HashSet<string> DeniedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord",
        "discord.exe",
    };

    private readonly ISelectedAudioSourceFactory sourceFactory;
    private readonly IAudioOutputWriter writer;
    private readonly IAudioSource? microphoneSource;
    private readonly SelectedSourceEngineOptions options;
    private readonly SelectedAudioMixer mixer = new();
    private readonly Dictionary<ProcessIdentity, IAudioSource> activeSources = [];

    public SelectedSourceEngine(
        ISelectedAudioSourceFactory sourceFactory,
        IAudioOutputWriter writer,
        IAudioSource? microphoneSource,
        SelectedSourceEngineOptions options)
    {
        this.sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.microphoneSource = microphoneSource;
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SynchronizeAsync(IReadOnlyList<AudioSession> selectedSessions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedSessions);
        cancellationToken.ThrowIfCancellationRequested();

        var selectedByIdentity = selectedSessions
            .Where(session => !DeniedProcessNames.Contains(session.ProcessName))
            .ToDictionary(ProcessIdentity.From);

        foreach (var identity in activeSources.Keys.Except(selectedByIdentity.Keys).ToArray())
        {
            var source = activeSources[identity];
            activeSources.Remove(identity);
            await source.DisposeAsync();
        }

        foreach (var (identity, session) in selectedByIdentity)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!activeSources.ContainsKey(identity))
            {
                activeSources.Add(identity, sourceFactory.Create(session, options.Format));
            }
        }
    }

    public async Task MixOnceAsync(CancellationToken cancellationToken)
    {
        var frames = new List<AudioFrame>(activeSources.Count);
        foreach (var source in activeSources.Values)
        {
            var frame = await source.ReadAsync(cancellationToken);
            if (frame is not null)
            {
                frames.Add(frame);
            }
        }

        var microphoneFrame = options.IncludeMicrophone && microphoneSource is not null
            ? await microphoneSource.ReadAsync(cancellationToken)
            : null;
        var mixedFrame = mixer.Mix(options.Format, DateTime.UtcNow.Ticks, frames, microphoneFrame);

        await writer.WriteAsync(mixedFrame, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in activeSources.Values)
        {
            await source.DisposeAsync();
        }

        activeSources.Clear();
        if (microphoneSource is not null)
        {
            await microphoneSource.DisposeAsync();
        }

        await writer.DisposeAsync();
    }

    private readonly record struct ProcessIdentity(int ProcessId, long ProcessStartUtcTicks, string ProcessName)
    {
        public static ProcessIdentity From(AudioSession session) =>
            new(session.ProcessId, session.ProcessStartUtcTicks, session.ProcessName.ToUpperInvariant());
    }
}
