using System.IO;
using System.Text;
using System.Text.Json;
using AudioShare.Core;

namespace AudioShare.App;

public sealed class FlowCastPreferencesStore
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowCast",
        "preferences.json");

    public FlowCastPreferences Load()
    {
        try
        {
            if (!File.Exists(PreferencesPath))
            {
                return FlowCastPreferences.Empty;
            }

            var saved = JsonSerializer.Deserialize<SavedPreferences>(File.ReadAllText(PreferencesPath));
            return saved is null
                ? FlowCastPreferences.Empty
                : new FlowCastPreferences(saved.ExcludedProcesses, saved.ReduceMotion);
        }
        catch (Exception)
        {
            return FlowCastPreferences.Empty;
        }
    }

    public void Save(FlowCastPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        var saved = new SavedPreferences(preferences.ExcludedProcesses.Order(StringComparer.OrdinalIgnoreCase).ToArray(), preferences.ReduceMotion);
        File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(saved), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed record SavedPreferences(string[]? ExcludedProcesses, bool ReduceMotion);
}
