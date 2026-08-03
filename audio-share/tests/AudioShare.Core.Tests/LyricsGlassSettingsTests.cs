using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AudioShare.Core.Tests;

public sealed class LyricsGlassSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-glass-settings-{Guid.NewGuid():N}");

    [Fact]
    public void Defaults_AreTheExpectedValidCenteredState()
    {
        var settings = global::AudioShare.Lyrics.LyricsGlassSettings.Defaults;
        var state = global::AudioShare.Lyrics.LyricsGlassHostState.Defaults;

        Assert.Equal(new global::AudioShare.Lyrics.LyricsGlassSettings(1, 2, 0.42, 0.62, true), settings);
        Assert.Equal(new global::AudioShare.Lyrics.LyricsGlassHostState(null, settings), state);
        Assert.True(settings.IsValid);
        Assert.True(state.IsValid);
    }

    [Theory]
    [InlineData(double.NaN, 2, 0.42, 0.62)]
    [InlineData(double.PositiveInfinity, 2, 0.42, 0.62)]
    [InlineData(-0.01, 2, 0.42, 0.62)]
    [InlineData(1.01, 2, 0.42, 0.62)]
    [InlineData(1, -0.01, 0.42, 0.62)]
    [InlineData(1, 32.01, 0.42, 0.62)]
    [InlineData(1, 2, -0.01, 0.62)]
    [InlineData(1, 2, 0.42, 1.01)]
    public void Settings_IsInvalidOutsideFiniteInclusiveRanges(double corner, double blur, double refractionHeight, double refractionAmount)
    {
        var settings = new global::AudioShare.Lyrics.LyricsGlassSettings(corner, blur, refractionHeight, refractionAmount, true);

        Assert.False(settings.IsValid);
    }

    [Fact]
    public void Position_IsValidOnlyWhenBothCoordinatesAreFinite()
    {
        Assert.True(new global::AudioShare.Lyrics.OverlayPosition(12.5, -3.5).IsValid);
        Assert.False(new global::AudioShare.Lyrics.OverlayPosition(double.NaN, 0).IsValid);
        Assert.False(new global::AudioShare.Lyrics.OverlayPosition(0, double.NegativeInfinity).IsValid);
    }

    [Theory]
    [InlineData("{ broken json")]
    [InlineData("{\"glass\":{\"cornerRadiusFraction\":1,\"blurRadiusDp\":2,\"refractionHeightFraction\":0.42,\"refractionAmountFraction\":0.62,\"chromaticAberration\":true}}")]
    [InlineData("{\"position\":null,\"glass\":{\"cornerRadiusFraction\":1,\"blurRadiusDp\":2,\"refractionHeightFraction\":0.42,\"refractionAmountFraction\":0.62,\"chromaticAberration\":true},\"extra\":1}")]
    [InlineData("{\"position\":null,\"glass\":{\"cornerRadiusFraction\":\"NaN\",\"blurRadiusDp\":2,\"refractionHeightFraction\":0.42,\"refractionAmountFraction\":0.62,\"chromaticAberration\":true}}")]
    [InlineData("{\"position\":null,\"glass\":{\"cornerRadiusFraction\":1.1,\"blurRadiusDp\":2,\"refractionHeightFraction\":0.42,\"refractionAmountFraction\":0.62,\"chromaticAberration\":true}}")]
    public void Load_ReturnsDefaultsForMalformedIncompleteUnknownOrInvalidDocuments(string contents)
    {
        var path = Path.Combine(directory, "glass-settings.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, contents);

        var loaded = new global::AudioShare.Lyrics.LyricsGlassSettingsStore(path).Load();

        Assert.Equal(global::AudioShare.Lyrics.LyricsGlassHostState.Defaults, loaded);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenTheDocumentIsMissing()
    {
        var loaded = new global::AudioShare.Lyrics.LyricsGlassSettingsStore(Path.Combine(directory, "glass-settings.json")).Load();

        Assert.Equal(global::AudioShare.Lyrics.LyricsGlassHostState.Defaults, loaded);
    }

    [Fact]
    public void CreateDefault_UsesTheFlowCastLyricsSettingsPath()
    {
        var store = global::AudioShare.Lyrics.LyricsGlassSettingsStore.CreateDefault();

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlowCast Lyrics",
                "glass-settings.json"),
            GetConfiguredPath(store));
    }

    [Fact]
    public void Save_RoundTripsCompleteCamelCaseStateAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(directory, "glass-settings.json");
        var expected = new global::AudioShare.Lyrics.LyricsGlassHostState(
            new global::AudioShare.Lyrics.OverlayPosition(123.25, 456.5),
            new global::AudioShare.Lyrics.LyricsGlassSettings(0.9, 16, 0.5, 0.4, false));
        var store = new global::AudioShare.Lyrics.LyricsGlassSettingsStore(path);

        store.Save(expected);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.TryGetProperty("position", out var position));
        Assert.True(position.TryGetProperty("x", out _));
        Assert.True(position.TryGetProperty("y", out _));
        Assert.True(document.RootElement.TryGetProperty("glass", out var glass));
        Assert.True(glass.TryGetProperty("cornerRadiusFraction", out _));
        Assert.True(glass.TryGetProperty("blurRadiusDp", out _));
        Assert.Equal(expected, store.Load());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Save_CreatesTheSettingsDocumentAndRoundTripsANullPosition()
    {
        var path = Path.Combine(directory, "FlowCast Lyrics", "glass-settings.json");
        var expected = new global::AudioShare.Lyrics.LyricsGlassHostState(
            null,
            new global::AudioShare.Lyrics.LyricsGlassSettings(0.25, 4, 0.5, 0.75, false));
        var store = new global::AudioShare.Lyrics.LyricsGlassSettingsStore(path);

        store.Save(expected);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("position").ValueKind);
        Assert.Equal(expected, store.Load());
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenTheSettingsDocumentCannotBeRead()
    {
        var path = Path.Combine(directory, "glass-settings.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "{\"position\":null,\"glass\":{\"cornerRadiusFraction\":1,\"blurRadiusDp\":2,\"refractionHeightFraction\":0.42,\"refractionAmountFraction\":0.62,\"chromaticAberration\":true}}");

        using var lockedFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var loaded = new global::AudioShare.Lyrics.LyricsGlassSettingsStore(path).Load();

        Assert.Equal(global::AudioShare.Lyrics.LyricsGlassHostState.Defaults, loaded);
    }

    [Fact]
    public void Save_RejectsInvalidStates()
    {
        var invalid = new global::AudioShare.Lyrics.LyricsGlassHostState(
            new global::AudioShare.Lyrics.OverlayPosition(double.NaN, 0),
            global::AudioShare.Lyrics.LyricsGlassSettings.Defaults);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new global::AudioShare.Lyrics.LyricsGlassSettingsStore(Path.Combine(directory, "glass-settings.json")).Save(invalid));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GetConfiguredPath(global::AudioShare.Lyrics.LyricsGlassSettingsStore store)
    {
        var field = typeof(global::AudioShare.Lyrics.LyricsGlassSettingsStore)
            .GetField("path", BindingFlags.Instance | BindingFlags.NonPublic);

        return Assert.IsType<string>(field?.GetValue(store));
    }
}
