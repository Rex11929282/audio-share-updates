using System.Text.Json;
using System.Text.Json.Serialization;
using AudioShare.Lyrics.Contracts;

namespace AudioShare.Lyrics;

public sealed class LyricsOverlayPresenter : ILyricsOverlaySink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private IslandSnapshot currentSnapshot = new(IslandMode.Idle, "等待播放", null, LyricSource.None);

    public event EventHandler<string>? StateChanged;

    public IslandSnapshot CurrentSnapshot => currentSnapshot;

    public string DisplayText => currentSnapshot.DisplayText;

    public void Show(IslandSnapshot snapshot)
    {
        var lyricLine = snapshot.Mode is IslandMode.Playing or IslandMode.Paused &&
                        !string.IsNullOrWhiteSpace(snapshot.LyricLine?.Text)
            ? snapshot.LyricLine with { Text = snapshot.LyricLine.Text.Trim() }
            : null;
        var displayText = lyricLine?.Text ?? NormalizeDisplayText(snapshot.Mode, snapshot.DisplayText);
        currentSnapshot = snapshot with
        {
            DisplayText = displayText,
            LyricLine = lyricLine
        };
        RaiseStateChanged();
    }

    public string GetSnapshotJson() => JsonSerializer.Serialize(currentSnapshot, JsonOptions);

    private static string NormalizeDisplayText(IslandMode mode, string displayText)
    {
        if (!string.IsNullOrWhiteSpace(displayText))
        {
            return displayText.Trim();
        }

        return mode switch
        {
            IslandMode.Idle => "等待播放",
            IslandMode.Resolving => "正在取得歌詞",
            IslandMode.NoLyrics => "這首歌沒有歌詞",
            IslandMode.Unavailable => "暫時無法取得歌詞",
            IslandMode.Playing or IslandMode.Paused => "等待播放",
            _ => throw new InvalidOperationException("Unknown island mode.")
        };
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, GetSnapshotJson());
}
