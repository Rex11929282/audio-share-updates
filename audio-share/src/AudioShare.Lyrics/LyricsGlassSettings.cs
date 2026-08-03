using System.Text.Json.Serialization;

namespace AudioShare.Lyrics;

internal sealed record LyricsGlassSettings(
    [property: JsonRequired] double CornerRadiusFraction,
    [property: JsonRequired] double BlurRadiusDp,
    [property: JsonRequired] double RefractionHeightFraction,
    [property: JsonRequired] double RefractionAmountFraction,
    [property: JsonRequired] bool ChromaticAberration)
{
    internal static LyricsGlassSettings Defaults { get; } = new(1, 2, 0.42, 0.62, true);

    internal bool IsValid =>
        IsInRange(CornerRadiusFraction, 0, 1) &&
        IsInRange(BlurRadiusDp, 0, 32) &&
        IsInRange(RefractionHeightFraction, 0, 1) &&
        IsInRange(RefractionAmountFraction, 0, 1);

    private static bool IsInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}

internal sealed record OverlayPosition(
    [property: JsonRequired] double X,
    [property: JsonRequired] double Y)
{
    internal bool IsValid => double.IsFinite(X) && double.IsFinite(Y);
}

internal sealed record LyricsGlassHostState(
    [property: JsonRequired] OverlayPosition? Position,
    [property: JsonRequired] LyricsGlassSettings Glass)
{
    internal static LyricsGlassHostState Defaults { get; } = new(null, LyricsGlassSettings.Defaults);

    internal bool IsValid => (Position?.IsValid ?? true) && Glass?.IsValid == true;
}
