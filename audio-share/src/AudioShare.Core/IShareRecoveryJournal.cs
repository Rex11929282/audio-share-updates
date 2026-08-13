namespace AudioShare.Core;

public interface IShareRecoveryJournal
{
    Task<ShareRecoveryRecord?> ReadAsync(CancellationToken token);

    Task WriteAsync(ShareRecoveryRecord record, CancellationToken token);

    Task ClearAsync();
}
