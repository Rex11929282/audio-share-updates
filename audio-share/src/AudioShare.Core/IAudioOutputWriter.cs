namespace AudioShare.Core;

public interface IAudioOutputWriter : IAsyncDisposable
{
    Task WriteAsync(AudioFrame frame, CancellationToken cancellationToken);
}
