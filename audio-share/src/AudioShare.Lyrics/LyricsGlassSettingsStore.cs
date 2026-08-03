using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioShare.Lyrics;

internal sealed class LyricsGlassSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = true
    };

    private readonly string path;

    internal LyricsGlassSettingsStore(string path) => this.path = path;

    internal static LyricsGlassSettingsStore CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics");
        return new LyricsGlassSettingsStore(Path.Combine(directory, "glass-settings.json"));
    }

    internal LyricsGlassHostState Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return LyricsGlassHostState.Defaults;
            }

            var state = JsonSerializer.Deserialize<LyricsGlassHostState>(File.ReadAllText(path), JsonOptions);
            return state?.IsValid == true ? state : LyricsGlassHostState.Defaults;
        }
        catch (JsonException)
        {
            return LyricsGlassHostState.Defaults;
        }
        catch (IOException)
        {
            return LyricsGlassHostState.Defaults;
        }
        catch (UnauthorizedAccessException)
        {
            return LyricsGlassHostState.Defaults;
        }
    }

    internal void Save(LyricsGlassHostState state)
    {
        if (!state.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
