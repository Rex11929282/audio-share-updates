using System.IO;

namespace AudioShare.Lyrics;

internal sealed class LyricsGlassHostStatePersistence
{
    private readonly object stateGate = new();
    private readonly LyricsGlassSettingsStore store;
    private LyricsGlassHostState current;

    internal LyricsGlassHostState Current
    {
        get
        {
            lock (stateGate)
            {
                return current;
            }
        }
    }

    internal LyricsGlassHostStatePersistence(
        LyricsGlassSettingsStore store,
        LyricsGlassHostState initialState)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(initialState);
        this.store = store;
        current = initialState;
    }

    internal void ApplySettings(LyricsGlassSettings settings) =>
        Update(state => state with { Glass = settings });

    internal void ApplyPosition(OverlayPosition position) =>
        Update(state => state with { Position = position });

    private void Update(Func<LyricsGlassHostState, LyricsGlassHostState> update)
    {
        LyricsGlassHostState snapshot;
        lock (stateGate)
        {
            current = update(current);
            snapshot = current;
        }

        try
        {
            store.Save(snapshot);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
