using AudioShare.App;
using AudioShare.Core;
using System.IO;

namespace AudioShare.Core.Tests;

public sealed class FlowCastRecoveryJournalTests
{
    [Fact]
    public async Task WriteUsesReplaceableJsonAndRoundTripsSnapshots()
    {
        using var folder = new TemporaryFolder();
        var journal = new FlowCastRecoveryJournal(folder.Path);
        var record = new ShareRecoveryRecord(
            "chrome.exe",
            "Chrome",
            "input",
            [new ApplicationRouteSnapshot(10, 20, "chrome.exe", new ApplicationRouteState("speakers", "speakers"))],
            DateTimeOffset.Parse("2026-08-08T10:00:00Z"));

        await journal.WriteAsync(record, CancellationToken.None);

        var loaded = await journal.ReadAsync(CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(record.ProcessName, loaded.ProcessName);
        Assert.Equal(record.Snapshots, loaded.Snapshots);
        Assert.False(File.Exists(Path.Combine(folder.Path, "share-recovery.tmp")));
    }

    [Fact]
    public async Task CorruptJournalIsQuarantinedInsteadOfBlockingStartup()
    {
        using var folder = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "share-recovery.json"), "{");
        var journal = new FlowCastRecoveryJournal(folder.Path);

        Assert.Null(await journal.ReadAsync(CancellationToken.None));
        Assert.Single(Directory.GetFiles(folder.Path, "share-recovery.corrupt-*.json"));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"FlowCastTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
