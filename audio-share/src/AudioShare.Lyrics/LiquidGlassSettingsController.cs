using System;
using System.Text.Json;

namespace AudioShare.Lyrics;

internal sealed class LiquidGlassSettingsController
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LiquidGlassSettingsStore store;

    public LiquidGlassSettingsController(LiquidGlassSettingsStore store)
    {
        this.store = store;
        Saved = store.Load();
        Draft = Saved;
    }

    public event EventHandler<LiquidGlassSettings>? SettingsChanged;

    public LiquidGlassSettings Saved { get; private set; }

    public LiquidGlassSettings Draft { get; private set; }

    public bool Preview(LiquidGlassSettings settings)
    {
        if (!settings.IsValid)
        {
            return false;
        }

        Draft = settings;
        SettingsChanged?.Invoke(this, Draft);
        return true;
    }

    public void Reset() => Preview(LiquidGlassSettings.OfficialDefaults);

    public void Cancel() => Preview(Saved);

    public void Save()
    {
        store.Save(Draft);
        Saved = Draft;
    }

    public string GetSettingsJson() => JsonSerializer.Serialize(
        new { type = "liquid-settings", settings = Draft },
        JsonOptions);
}
