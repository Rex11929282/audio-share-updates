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

    private ConnectionState connectionState = ConnectionState.Searching;
    private LyricLine? lyricLine;

    public event EventHandler<string>? StateChanged;

    public string DisplayText => connectionState switch
    {
        ConnectionState.Searching => "正在尋找 FlowCast",
        ConnectionState.Unavailable => "未偵測到 Radmin VPN",
        ConnectionState.Connected => lyricLine?.Text ?? "已連線，等待歌詞",
        ConnectionState.Disconnected => "與 FlowCast 的連線已中斷，正在重新連線",
        _ => throw new InvalidOperationException("Unknown Lyrics connection state.")
    };

    public void SetConnectionState(ConnectionState state)
    {
        connectionState = state;
        if (state != ConnectionState.Connected)
        {
            lyricLine = null;
        }

        RaiseStateChanged();
    }

    public void ShowLyricLine(LyricLine? line)
    {
        lyricLine = connectionState == ConnectionState.Connected && !string.IsNullOrWhiteSpace(line?.Text)
            ? line
            : null;
        RaiseStateChanged();
    }

    public string GetSnapshotJson() => JsonSerializer.Serialize(
        new OverlaySnapshot(connectionState, DisplayText, lyricLine),
        JsonOptions);

    private void RaiseStateChanged() => StateChanged?.Invoke(this, GetSnapshotJson());

    private sealed record OverlaySnapshot(
        ConnectionState ConnectionState,
        string DisplayText,
        LyricLine? LyricLine);
}
