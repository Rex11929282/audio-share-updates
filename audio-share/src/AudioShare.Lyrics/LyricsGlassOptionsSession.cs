namespace AudioShare.Lyrics;

internal sealed class LyricsGlassOptionsSession
{
    private readonly LyricsGlassSettings initial;

    internal LyricsGlassOptionsSession(LyricsGlassSettings initial)
    {
        this.initial = initial;
        Current = initial;
    }

    internal LyricsGlassSettings Current { get; set; }

    internal void Reset() => Current = LyricsGlassSettings.Defaults;
    internal void Cancel() => Current = initial;
    internal LyricsGlassSettings Complete() => Current;
}
