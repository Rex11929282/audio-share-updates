namespace AudioShare.Core;

public interface IAudioSource : IAsyncDisposable
{
    string SourceId { get; }

    Task<AudioFrame?> ReadAsync(CancellationToken cancellationToken);
}
