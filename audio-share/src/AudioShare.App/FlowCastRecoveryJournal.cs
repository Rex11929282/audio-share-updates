using System.Text;
using System.Text.Json;
using System.IO;
using AudioShare.Core;

namespace AudioShare.App;

public sealed class FlowCastRecoveryJournal : IShareRecoveryJournal
{
    private const string JournalFileName = "share-recovery.json";
    private const string TempFileName = "share-recovery.tmp";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly string root;
    private readonly string journalPath;
    private readonly string tempPath;

    public FlowCastRecoveryJournal(string? root = null)
    {
        this.root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast");
        journalPath = Path.Combine(this.root, JournalFileName);
        tempPath = Path.Combine(this.root, TempFileName);
    }

    public async Task<ShareRecoveryRecord?> ReadAsync(CancellationToken token)
    {
        if (!File.Exists(journalPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(journalPath, token);
            return JsonSerializer.Deserialize<ShareRecoveryRecord>(json, JsonOptions);
        }
        catch (JsonException)
        {
            QuarantineCorruptJournal();
            return null;
        }
    }

    public async Task WriteAsync(ShareRecoveryRecord record, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(record);
        Directory.CreateDirectory(root);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), token);
        File.Move(tempPath, journalPath, overwrite: true);
    }

    public Task ClearAsync()
    {
        if (File.Exists(journalPath))
        {
            File.Delete(journalPath);
        }

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        return Task.CompletedTask;
    }

    private void QuarantineCorruptJournal()
    {
        Directory.CreateDirectory(root);
        var corruptPath = Path.Combine(
            root,
            $"share-recovery.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Move(journalPath, corruptPath, overwrite: true);
    }
}
