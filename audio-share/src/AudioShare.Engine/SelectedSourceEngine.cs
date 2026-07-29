using AudioShare.Core;

namespace AudioShare.Engine;

public interface ISelectedProcessSourceFactory
{
    Task<IAudioSource> CreateAsync(AudioSession session, CancellationToken cancellationToken);
}

public sealed class SelectedSourceEngine : IAsyncDisposable
{
    private static readonly HashSet<string> DeniedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord",
        "discord.exe",
    };

    private readonly ISelectedProcessSourceFactory sourceFactory;
    private readonly IAudioOutputWriter writer;
    private readonly IAudioSource? microphoneSource;
    private readonly SelectedSourceEngineOptions options;
    private readonly SelectedAudioMixer mixer = new();
    private readonly Dictionary<ProcessIdentity, IAudioSource> activeSources = [];

    public SelectedSourceEngine(
        ISelectedProcessSourceFactory sourceFactory,
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
        if (selectedSessions.Any(session => session is null))
        {
            throw new ArgumentException("Selected sessions cannot contain null.", nameof(selectedSessions));
        }

        var selectedByIdentity = selectedSessions
            .Where(session => session.HasAudio && !DeniedProcessNames.Contains(session.ProcessName))
            .Select(session =>
            {
                ValidateSession(session);
                return session;
            })
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
                var source = await sourceFactory.CreateAsync(session, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                activeSources.Add(identity, source ?? throw new InvalidOperationException("The source factory returned null."));
            }
        }
    }

    public async Task MixOnceAsync(CancellationToken cancellationToken)
    {
        var frames = new List<AudioFrame>(activeSources.Count);
        foreach (var (identity, source) in activeSources.ToArray())
        {
            var frame = await source.ReadAsync(cancellationToken);
            if (frame is not null)
            {
                frames.Add(frame);
                continue;
            }

            activeSources.Remove(identity);
            await source.DisposeAsync();
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

    private static void ValidateSession(AudioSession session)
    {
        if (session.ProcessId <= 0 ||
            session.ProcessStartUtcTicks <= 0 ||
            string.IsNullOrWhiteSpace(session.ProcessName))
        {
            throw new ArgumentException("Selected sessions must have a valid process identity.", nameof(session));
        }
    }
}
