using System.Text.Json.Serialization;

namespace AudioShare.Lyrics;

internal sealed record LiquidGlassSettings(
    [property: JsonRequired] double DisplacementScale,
    [property: JsonRequired] double BlurAmount,
    [property: JsonRequired] double Saturation,
    [property: JsonRequired] double AberrationIntensity,
    [property: JsonRequired] double Elasticity,
    [property: JsonRequired] double CornerRadius)
{
    public static LiquidGlassSettings OfficialDefaults { get; } = new(70, 0.0625, 140, 2, 0.15, 999);

    public bool IsValid =>
        IsInRange(DisplacementScale, 0, 200) &&
        IsInRange(BlurAmount, 0, 1) &&
        IsInRange(Saturation, 0, 300) &&
        IsInRange(AberrationIntensity, 0, 20) &&
        IsInRange(Elasticity, 0, 1) &&
        IsInRange(CornerRadius, 0, 999);

    private static bool IsInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}
