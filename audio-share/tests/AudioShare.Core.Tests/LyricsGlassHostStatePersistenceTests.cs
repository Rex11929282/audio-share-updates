using System.IO;

namespace AudioShare.Lyrics;

public sealed class LyricsGlassHostStatePersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-host-state-{Guid.NewGuid():N}");

    [Fact]
    public void StorageIoFailure_DoesNotEscapeOrLoseTheInMemorySettings()
    {
        Directory.CreateDirectory(directory);
        var parentFile = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(parentFile, "occupied");
        var store = new LyricsGlassSettingsStore(Path.Combine(parentFile, "glass-settings.json"));
        var persistence = new LyricsGlassHostStatePersistence(store, LyricsGlassHostState.Defaults);
        var settings = LyricsGlassSettings.Defaults with { BlurRadiusDp = 9 };

        var exception = Record.Exception(() => persistence.ApplySettings(settings));

        Assert.Null(exception);
        Assert.Equal(settings, persistence.Current.Glass);
    }

    [Fact]
    public void ValidPosition_IsFoldedIntoTheSingleHostSnapshot()
    {
        Directory.CreateDirectory(directory);
        var store = new LyricsGlassSettingsStore(Path.Combine(directory, "glass-settings.json"));
        var persistence = new LyricsGlassHostStatePersistence(store, LyricsGlassHostState.Defaults);
        var position = new OverlayPosition(43.5, -17.25);

        persistence.ApplyPosition(position);

        Assert.Equal(position, persistence.Current.Position);
        Assert.Equal(position, store.Load().Position);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
