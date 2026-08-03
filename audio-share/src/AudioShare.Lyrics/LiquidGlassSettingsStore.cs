using System;
using System.IO;
using System.Text.Json;

namespace AudioShare.Lyrics;

internal sealed class LiquidGlassSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string path;
    private readonly Func<string, string> readAllText;

    public LiquidGlassSettingsStore(string path) : this(path, File.ReadAllText)
    {
    }

    internal LiquidGlassSettingsStore(string path, Func<string, string> readAllText)
    {
        this.path = path;
        this.readAllText = readAllText;
    }

    public static LiquidGlassSettingsStore CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics");
        return new LiquidGlassSettingsStore(Path.Combine(directory, "liquid-glass-settings.json"));
    }

    public LiquidGlassSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return LiquidGlassSettings.OfficialDefaults;
            }

            var settings = JsonSerializer.Deserialize<LiquidGlassSettings>(readAllText(path), JsonOptions);
            return settings?.IsValid == true ? settings : LiquidGlassSettings.OfficialDefaults;
        }
        catch (JsonException)
        {
            return LiquidGlassSettings.OfficialDefaults;
        }
        catch (IOException)
        {
            return LiquidGlassSettings.OfficialDefaults;
        }
        catch (UnauthorizedAccessException)
        {
            return LiquidGlassSettings.OfficialDefaults;
        }
    }

    public void Save(LiquidGlassSettings settings)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
