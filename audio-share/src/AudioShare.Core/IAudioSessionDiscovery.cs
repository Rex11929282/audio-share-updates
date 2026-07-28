namespace AudioShare.Core;

public interface IAudioSessionDiscovery
{
    Task<IReadOnlyList<AudioSession>> GetActiveSessionsAsync(CancellationToken token);
}
