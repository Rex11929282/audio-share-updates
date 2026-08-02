using System.IO;
using System.Text;
using System.Text.Json;

namespace AudioShare.App;

internal sealed record ShareHistoryEntry(DateTimeOffset StartedAt, string ProgramName, TimeSpan Duration, string StopReason);

internal sealed class ShareHistoryStore
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowCast",
        "share-history.json");

    public IReadOnlyList<ShareHistoryEntry> Load()
    {
        try
        {
            return File.Exists(HistoryPath)
                ? JsonSerializer.Deserialize<List<ShareHistoryEntry>>(File.ReadAllText(HistoryPath)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<ShareHistoryEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        var retained = entries.OrderByDescending(entry => entry.StartedAt).Take(100).ToArray();
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(retained), new UTF8Encoding(false));
    }
}
