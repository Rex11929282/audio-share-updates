using System;
using System.IO;
using System.Text.Json;
using AudioShare.Lyrics;

namespace AudioShare.Core.Tests;

public sealed class LiquidGlassSettingsControllerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"flowcast-controller-{Guid.NewGuid():N}");

    [Fact]
    public void Preview_ChangesDraftAndRaisesSettingsWithoutSaving()
    {
        var controller = CreateController();
        var draft = new LiquidGlassSettings(90, 0.2, 150, 3, 0.3, 90);
        LiquidGlassSettings? raised = null;
        controller.SettingsChanged += (_, settings) => raised = settings;

        Assert.True(controller.Preview(draft));

        Assert.Equal(draft, controller.Draft);
        Assert.Equal(draft, raised);
        Assert.Equal(LiquidGlassSettings.OfficialDefaults, controller.Saved);
    }

    [Fact]
    public void Preview_RejectsInvalidSettingsWithoutChangingDraft()
    {
        var controller = CreateController();

        Assert.False(controller.Preview(new LiquidGlassSettings(-1, 0.2, 150, 3, 0.3, 90)));

        Assert.Equal(LiquidGlassSettings.OfficialDefaults, controller.Draft);
    }

    [Fact]
    public void Cancel_RestoresSavedSettings()
    {
        var controller = CreateController();
        controller.Preview(new LiquidGlassSettings(90, 0.2, 150, 3, 0.3, 90));

        controller.Cancel();

        Assert.Equal(controller.Saved, controller.Draft);
    }

    [Fact]
    public void Save_PersistsDraftAcrossASecondController()
    {
        var controller = CreateController();
        var draft = new LiquidGlassSettings(90, 0.2, 150, 3, 0.3, 90);
        controller.Preview(draft);
        controller.Save();

        Assert.Equal(draft, CreateController().Saved);
    }

    [Fact]
    public void Reset_PreviewsOfficialDefaultsWithoutSavingThem()
    {
        var controller = CreateControllerWithSaved(new LiquidGlassSettings(90, 0.2, 150, 3, 0.3, 90));

        controller.Reset();

        Assert.Equal(LiquidGlassSettings.OfficialDefaults, controller.Draft);
        Assert.NotEqual(controller.Draft, controller.Saved);
    }

    [Fact]
    public void GetSettingsJson_UsesTheLiquidSettingsMessageType()
    {
        var controller = CreateController();

        using var json = JsonDocument.Parse(controller.GetSettingsJson());

        Assert.Equal("liquid-settings", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(70, json.RootElement.GetProperty("settings").GetProperty("displacementScale").GetDouble());
    }

    private LiquidGlassSettingsController CreateController() =>
        new(new LiquidGlassSettingsStore(Path.Combine(directory, "liquid-glass-settings.json")));

    private LiquidGlassSettingsController CreateControllerWithSaved(LiquidGlassSettings settings)
    {
        var store = new LiquidGlassSettingsStore(Path.Combine(directory, "liquid-glass-settings.json"));
        store.Save(settings);
        return new LiquidGlassSettingsController(store);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
