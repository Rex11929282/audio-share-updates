using System;
using System.IO;
using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class LiquidGlassSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-liquid-{Guid.NewGuid():N}");

    [Fact]
    public void OfficialDefaults_MatchTheInstalledGitHubPackage()
    {
        Assert.Equal(new LiquidGlassSettings(70, 0.0625, 140, 2, 0.15, 999), LiquidGlassSettings.OfficialDefaults);
    }

    [Theory]
    [InlineData(-1, 0.0625, 140, 2, 0.15, 999)]
    [InlineData(70, 1.1, 140, 2, 0.15, 999)]
    [InlineData(70, 0.0625, 301, 2, 0.15, 999)]
    [InlineData(70, 0.0625, 140, 21, 0.15, 999)]
    [InlineData(70, 0.0625, 140, 2, 1.1, 999)]
    [InlineData(70, 0.0625, 140, 2, 0.15, 1000)]
    public void IsValid_RejectsValuesOutsideTheApprovedRanges(
        double displacementScale,
        double blurAmount,
        double saturation,
        double aberrationIntensity,
        double elasticity,
        double cornerRadius)
    {
        Assert.False(new LiquidGlassSettings(
            displacementScale,
            blurAmount,
            saturation,
            aberrationIntensity,
            elasticity,
            cornerRadius).IsValid);
    }

    [Fact]
    public void Store_RoundTripsOneCompleteSettingsDocument()
    {
        var path = Path.Combine(directory, "liquid-glass-settings.json");
        var store = new LiquidGlassSettingsStore(path);
        var settings = new LiquidGlassSettings(88, 0.2, 155, 4, 0.4, 80);

        store.Save(settings);

        Assert.Equal(settings, store.Load());
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"displacementScale\":70}")]
    [InlineData("{\"displacementScale\":-1,\"blurAmount\":0.1,\"saturation\":140,\"aberrationIntensity\":2,\"elasticity\":0.15,\"cornerRadius\":999}")]
    public void Store_LoadsOfficialDefaultsForInvalidFiles(string json)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "liquid-glass-settings.json");
        File.WriteAllText(path, json);

        Assert.Equal(LiquidGlassSettings.OfficialDefaults, new LiquidGlassSettingsStore(path).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
