# FlowCast Lyrics Liquid Glass Tuner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a right-click React tuning window that previews, applies, cancels, and persists the six agreed native `liquid-glass-react` props while the normal app remains only a movable Dynamic Island.

**Architecture:** The WPF process owns a validated saved/draft settings controller and persists one JSON document under LocalAppData. The existing overlay WebView and a temporary tuner WebView exchange typed JSON messages with that controller; the overlay passes the six values directly to `LiquidGlass`, and the tuner is disposed when closed.

**Tech Stack:** .NET 8 WPF, WebView2 1.0.4078.44, React 19.2.8, TypeScript 7.0.2, Vite 8.2.0, Vitest 4.1.10, `liquid-glass-react` 1.1.1, xUnit 2.5.3.

## Global Constraints

- Use `liquid-glass-react` 1.1.1 without copying, replacing, or imitating its rendering.
- Editable props are exactly `displacementScale`, `blurAmount`, `saturation`, `aberrationIntensity`, `elasticity`, and `cornerRadius`.
- Official reset values are `70`, `0.0625`, `140`, `2`, `0.15`, and `999`, respectively.
- Keep `mode="standard"`; mode selection and all other package props are outside scope.
- Slider ranges are `0–200`, `0–1`, `0–300`, `0–20`, `0–1`, and `0–999`, respectively.
- No custom gradient, color filter, shadow control, or imitation-glass setting may be applied to the capsule or exposed as a control. The tuner preview may place the real component over a sample scene because refraction requires background pixels.
- Preserve the existing real-desktop backdrop feed because the GitHub component needs pixels behind it to refract.
- Keep the normal overlay small, borderless, topmost, and freely draggable.
- Right-click opens one tuner window; left-drag behavior remains unchanged.
- Draft changes are event-driven and never write to disk until Save.
- Store settings at `%LocalAppData%\FlowCast Lyrics\liquid-glass-settings.json`.
- Missing or invalid settings load official reset values without blocking startup.
- Do not edit, stage, or revert `audio-share/src/AudioShare.App/App.xaml.cs` or `audio-share/src/AudioShare.App/MainWindow.xaml.cs`.
- Do not change FlowCast audio routing, Radmin discovery, NetEase lyric retrieval, or lyric transport.
- Do not publish GitHub or package a release EXE until the user has tuned and saved the desired values.

---

### Task 1: Preserve the authentic GitHub-glass backdrop baseline

**Files:**
- Existing working changes: `audio-share/src/AudioShare.Lyrics/DesktopBackdropCapture.cs`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/WindowBackdrop.cs`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css`
- Existing working changes: `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx`
- Existing working changes: `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`

**Interfaces:**
- Consumes: the current uncommitted real-desktop capture and fake-gradient removal already verified in the shared working tree.
- Produces: a committed baseline where `LyricsOverlay` accepts `backdropImageUrl?: string | null`, and WPF sends `{ type: "backdrop", dataUrl: "data:image/jpeg;base64,..." }`.

- [ ] **Step 1: Verify the regression test still exercises the original visual bug**

Run:

```powershell
Set-Location audio-share\src\AudioShare.Lyrics\Frontend
npm.cmd test -- --run src/LyricsOverlay.test.tsx
```

Expected: 8 tests pass, including `places the captured desktop behind the liquid glass instead of painting a fake gradient`.

- [ ] **Step 2: Verify the Windows backdrop bridge**

Run:

```powershell
Set-Location audio-share
dotnet test tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~FlowCastLyricsStartupTests.MainWindow_FeedsTheDesktopBehindTheIslandIntoTheWebGlass"
```

Expected: 1 test passes.

- [ ] **Step 3: Confirm the Git scope excludes the two protected main-app files**

Run:

```powershell
git diff --name-only -- audio-share/src/AudioShare.Lyrics audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
git diff --check -- audio-share/src/AudioShare.Lyrics audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
```

Expected: only Lyrics paths and its startup test are listed; `git diff --check` exits 0.

- [ ] **Step 4: Commit the backdrop prerequisite explicitly**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/DesktopBackdropCapture.cs audio-share/src/AudioShare.Lyrics/WindowBackdrop.cs audio-share/src/AudioShare.Lyrics/MainWindow.xaml audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs
git commit -m "fix: feed desktop pixels into Lyrics liquid glass"
```

Expected: the commit contains no `AudioShare.App` path.

---

### Task 2: Add the official settings model and atomic persistence

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/LiquidGlassSettings.cs`
- Create: `audio-share/src/AudioShare.Lyrics/LiquidGlassSettingsStore.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LiquidGlassSettingsTests.cs`

**Interfaces:**
- Consumes: `%LocalAppData%` resolved by `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`.
- Produces: `LiquidGlassSettings.OfficialDefaults`, `LiquidGlassSettings.IsValid`, `LiquidGlassSettingsStore.Load()`, and `LiquidGlassSettingsStore.Save(LiquidGlassSettings)`.

- [ ] **Step 1: Write failing model and store tests**

Create tests with these cases:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify RED**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassSettingsTests"
```

Expected: build fails because `LiquidGlassSettings` and `LiquidGlassSettingsStore` do not exist.

- [ ] **Step 3: Implement the settings record**

Create:

```csharp
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
```

- [ ] **Step 4: Implement atomic JSON persistence**

Create `LiquidGlassSettingsStore` with camel-case JSON and complete-document replacement:

```csharp
using System.Text.Json;

namespace AudioShare.Lyrics;

internal sealed class LiquidGlassSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string path;

    public LiquidGlassSettingsStore(string path)
    {
        this.path = path;
    }

    public static LiquidGlassSettingsStore CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowCast Lyrics");
        return new LiquidGlassSettingsStore(Path.Combine(directory, "liquid-glass-settings.json"));
    }

    public LiquidGlassSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return LiquidGlassSettings.OfficialDefaults;
            }

            var settings = JsonSerializer.Deserialize<LiquidGlassSettings>(File.ReadAllText(path), JsonOptions);
            return settings?.IsValid == true ? settings : LiquidGlassSettings.OfficialDefaults;
        }
        catch (JsonException)
        {
            return LiquidGlassSettings.OfficialDefaults;
        }
        catch (IOException)
        {
            return LiquidGlassSettings.OfficialDefaults;
        }
    }

    public void Save(LiquidGlassSettings settings)
    {
        if (!settings.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
```

- [ ] **Step 5: Run the focused and full .NET tests**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassSettingsTests"
dotnet test audio-share\AudioShare.sln --no-restore
```

Expected: all new tests pass; the full suite reports zero failures.

- [ ] **Step 6: Commit**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/LiquidGlassSettings.cs audio-share/src/AudioShare.Lyrics/LiquidGlassSettingsStore.cs audio-share/tests/AudioShare.Core.Tests/LiquidGlassSettingsTests.cs
git commit -m "feat: persist Lyrics liquid glass settings"
```

---

### Task 3: Add saved/draft settings semantics

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/LiquidGlassSettingsController.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LiquidGlassSettingsControllerTests.cs`

**Interfaces:**
- Consumes: `LiquidGlassSettingsStore.Load()` and `.Save(settings)` from Task 2.
- Produces: `Saved`, `Draft`, `Preview(settings)`, `Reset()`, `Cancel()`, `Save()`, `GetSettingsJson()`, and `SettingsChanged`.

- [ ] **Step 1: Write failing controller tests**

Create the test fixture with these members and cases:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify RED**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassSettingsControllerTests"
```

Expected: build fails because `LiquidGlassSettingsController` does not exist.

- [ ] **Step 3: Implement the controller**

```csharp
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
```

- [ ] **Step 4: Run focused and full tests**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassSettingsControllerTests"
dotnet test audio-share\AudioShare.sln --no-restore
```

Expected: all controller tests and the full suite pass.

- [ ] **Step 5: Commit**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/LiquidGlassSettingsController.cs audio-share/tests/AudioShare.Core.Tests/LiquidGlassSettingsControllerTests.cs
git commit -m "feat: coordinate Lyrics liquid glass drafts"
```

---

### Task 4: Pass all six settings into the real React component

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/Frontend/src/liquidGlassSettings.ts`
- Create: `audio-share/src/AudioShare.Lyrics/Frontend/src/liquidGlassSettings.test.ts`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx`

**Interfaces:**
- Consumes: WPF message `{ type: "liquid-settings", settings: LiquidGlassSettings }`.
- Produces: `officialLiquidGlassSettings`, `readLiquidGlassSettingsMessage(message)`, and `LyricsOverlay` prop `liquidSettings`.

- [ ] **Step 1: Write failing parser and component tests**

Add parser tests:

```ts
it('uses the exact GitHub defaults', () => {
  expect(officialLiquidGlassSettings).toEqual({
    displacementScale: 70,
    blurAmount: 0.0625,
    saturation: 140,
    aberrationIntensity: 2,
    elasticity: 0.15,
    cornerRadius: 999,
  })
})

it('accepts one complete in-range settings message', () => {
  expect(readLiquidGlassSettingsMessage({
    type: 'liquid-settings',
    settings: {
      displacementScale: 80,
      blurAmount: 0.2,
      saturation: 150,
      aberrationIntensity: 3,
      elasticity: 0.3,
      cornerRadius: 80,
    },
  })).toEqual({
    displacementScale: 80,
    blurAmount: 0.2,
    saturation: 150,
    aberrationIntensity: 3,
    elasticity: 0.3,
    cornerRadius: 80,
  })
})

it('rejects partial, non-finite, and out-of-range settings', () => {
  expect(readLiquidGlassSettingsMessage({ type: 'liquid-settings', settings: { displacementScale: 70 } })).toBeNull()
  expect(readLiquidGlassSettingsMessage({
    type: 'liquid-settings',
    settings: { ...officialLiquidGlassSettings, blurAmount: Number.NaN },
  })).toBeNull()
  expect(readLiquidGlassSettingsMessage({
    type: 'liquid-settings',
    settings: { ...officialLiquidGlassSettings, saturation: 301 },
  })).toBeNull()
})
```

Extend the `liquid-glass-react` mock to expose all six props as data attributes, render with a non-default settings object, and assert each value reaches the mock.

- [ ] **Step 2: Run frontend tests to verify RED**

Run:

```powershell
Set-Location audio-share\src\AudioShare.Lyrics\Frontend
npm.cmd test -- --run src/liquidGlassSettings.test.ts src/LyricsOverlay.test.tsx
```

Expected: failure because the settings module and `liquidSettings` prop do not exist.

- [ ] **Step 3: Implement the typed settings parser**

Create the complete settings module below. It accepts only one complete `liquid-settings` message and returns `null` for partial, non-finite, or out-of-range values.

```ts
export interface LiquidGlassSettings {
  displacementScale: number
  blurAmount: number
  saturation: number
  aberrationIntensity: number
  elasticity: number
  cornerRadius: number
}

export const officialLiquidGlassSettings: LiquidGlassSettings = {
  displacementScale: 70,
  blurAmount: 0.0625,
  saturation: 140,
  aberrationIntensity: 2,
  elasticity: 0.15,
  cornerRadius: 999,
}

export function readLiquidGlassSettingsMessage(message: unknown): LiquidGlassSettings | null {
  if (!isRecord(message) || message.type !== 'liquid-settings' || !isRecord(message.settings)) {
    return null
  }

  const settings = message.settings
  if (
    !isFiniteRange(settings.displacementScale, 0, 200)
    || !isFiniteRange(settings.blurAmount, 0, 1)
    || !isFiniteRange(settings.saturation, 0, 300)
    || !isFiniteRange(settings.aberrationIntensity, 0, 20)
    || !isFiniteRange(settings.elasticity, 0, 1)
    || !isFiniteRange(settings.cornerRadius, 0, 999)
  ) {
    return null
  }

  return {
    displacementScale: settings.displacementScale,
    blurAmount: settings.blurAmount,
    saturation: settings.saturation,
    aberrationIntensity: settings.aberrationIntensity,
    elasticity: settings.elasticity,
    cornerRadius: settings.cornerRadius,
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isFiniteRange(value: unknown, minimum: number, maximum: number): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= minimum && value <= maximum
}
```

- [ ] **Step 4: Wire the settings into `LiquidGlass`**

Change `LyricsOverlayProps` to require `liquidSettings: LiquidGlassSettings`, then pass only these values:

```tsx
<LiquidGlass
  className="lyrics-glass"
  mode="standard"
  displacementScale={liquidSettings.displacementScale}
  blurAmount={liquidSettings.blurAmount}
  saturation={liquidSettings.saturation}
  aberrationIntensity={liquidSettings.aberrationIntensity}
  elasticity={liquidSettings.elasticity}
  cornerRadius={liquidSettings.cornerRadius}
  padding="0"
  style={glassPositionStyle}
>
```

In `main.tsx`, initialize settings and insert this branch before backdrop and lyric-state handling:

```tsx
const [liquidSettings, setLiquidSettings] = useState(officialLiquidGlassSettings)

const onMessage = (event: MessageEvent<unknown>) => {
  const nextLiquidSettings = readLiquidGlassSettingsMessage(event.data)
  if (nextLiquidSettings) {
    setLiquidSettings(nextLiquidSettings)
    return
  }

  // Existing backdrop and lyric-state branches remain after this block.
}

return (
  <LyricsOverlay
    state={state}
    backdropImageUrl={backdropImageUrl}
    liquidSettings={liquidSettings}
  />
)
```

- [ ] **Step 5: Run frontend tests and build**

Run:

```powershell
npm.cmd test
npm.cmd run build
```

Expected: all frontend tests pass and Vite produces `dist/index.html` with exit 0.

- [ ] **Step 6: Commit**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/Frontend/src/liquidGlassSettings.ts audio-share/src/AudioShare.Lyrics/Frontend/src/liquidGlassSettings.test.ts audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/LyricsOverlay.test.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx
git commit -m "feat: drive Lyrics glass from official props"
```

---

### Task 5: Build the React live tuner surface

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/Frontend/src/LiquidGlassTuner.tsx`
- Create: `audio-share/src/AudioShare.Lyrics/Frontend/src/LiquidGlassTuner.test.tsx`
- Create: `audio-share/src/AudioShare.Lyrics/Frontend/src/tunerBridge.ts`
- Create: `audio-share/src/AudioShare.Lyrics/Frontend/src/tunerBridge.test.ts`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx`
- Modify: `audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css`

**Interfaces:**
- Consumes: WPF `{ type: "liquid-settings", settings }` messages.
- Produces: `{ type: "tuner-ready" }`, `{ type: "liquid-preview", settings }`, `{ type: "liquid-reset" }`, `{ type: "liquid-cancel" }`, and `{ type: "liquid-save" }` WebView messages.

- [ ] **Step 1: Write failing tuner behavior tests**

Use Testing Library to prove:

```tsx
it('renders exactly six official prop controls', () => {
  render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)
  expect(screen.getAllByRole('slider')).toHaveLength(6)
  for (const name of Object.keys(officialLiquidGlassSettings)) {
    expect(screen.getByText(name)).toBeInTheDocument()
  }
})

it('previews a slider value without saving', () => {
  render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)
  fireEvent.change(screen.getByLabelText('displacementScale slider'), { target: { value: '82' } })
  expect(bridge.preview).toHaveBeenLastCalledWith({
    ...officialLiquidGlassSettings,
    displacementScale: 82,
  })
  expect(bridge.save).not.toHaveBeenCalled()
})

it('maps Reset Cancel and Save to separate bridge commands', () => {
  render(<LiquidGlassTuner settings={officialLiquidGlassSettings} bridge={bridge} />)
  fireEvent.click(screen.getByRole('button', { name: 'Reset' }))
  fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
  fireEvent.click(screen.getByRole('button', { name: 'Save' }))
  expect(bridge.reset).toHaveBeenCalledOnce()
  expect(bridge.cancel).toHaveBeenCalledOnce()
  expect(bridge.save).toHaveBeenCalledOnce()
})
```

Bridge tests assert each method posts the exact message shape to `window.chrome.webview`.

- [ ] **Step 2: Run tuner tests to verify RED**

Run:

```powershell
npm.cmd test -- --run src/LiquidGlassTuner.test.tsx src/tunerBridge.test.ts
```

Expected: failure because the tuner and bridge modules do not exist.

- [ ] **Step 3: Implement the bridge**

Export this interface and browser implementation:

```ts
export interface TunerBridge {
  preview(settings: LiquidGlassSettings): void
  reset(): void
  cancel(): void
  save(): void
}

export function createTunerBridge(webview: FlowCastWebView): TunerBridge {
  return {
    preview: (settings) => webview.postMessage({ type: 'liquid-preview', settings }),
    reset: () => webview.postMessage({ type: 'liquid-reset' }),
    cancel: () => webview.postMessage({ type: 'liquid-cancel' }),
    save: () => webview.postMessage({ type: 'liquid-save' }),
  }
}
```

Export `FlowCastWebView` from `webview.d.ts` or move the interface into `tunerBridge.ts` so it is importable without duplicating it.

- [ ] **Step 4: Implement the tuner component**

Implement the component with one shared update path for range and number inputs:

```tsx
import { useEffect, useState } from 'react'
import LiquidGlass from 'liquid-glass-react'
import type { LiquidGlassSettings } from './liquidGlassSettings'
import type { TunerBridge } from './tunerBridge'

const controls = [
  { key: 'displacementScale', min: 0, max: 200, step: 1 },
  { key: 'blurAmount', min: 0, max: 1, step: 0.001 },
  { key: 'saturation', min: 0, max: 300, step: 1 },
  { key: 'aberrationIntensity', min: 0, max: 20, step: 0.1 },
  { key: 'elasticity', min: 0, max: 1, step: 0.01 },
  { key: 'cornerRadius', min: 0, max: 999, step: 1 },
] as const

interface LiquidGlassTunerProps {
  settings: LiquidGlassSettings
  bridge: TunerBridge
}

export function LiquidGlassTuner({ settings, bridge }: LiquidGlassTunerProps) {
  const [draft, setDraft] = useState(settings)

  useEffect(() => setDraft(settings), [settings])

  const update = (control: (typeof controls)[number], rawValue: string) => {
    const parsed = Number(rawValue)
    if (!Number.isFinite(parsed)) {
      return
    }

    const value = Math.min(control.max, Math.max(control.min, parsed))
    const next = { ...draft, [control.key]: value }
    setDraft(next)
    bridge.preview(next)
  }

  return (
    <main className="tuner-shell">
      <header>
        <h1>Liquid Glass</h1>
        <p>rdev/liquid-glass-react 1.1.1</p>
      </header>

      <div className="tuner-preview-scene" aria-label="Liquid Glass preview">
        <LiquidGlass
          mode="standard"
          displacementScale={draft.displacementScale}
          blurAmount={draft.blurAmount}
          saturation={draft.saturation}
          aberrationIntensity={draft.aberrationIntensity}
          elasticity={draft.elasticity}
          cornerRadius={draft.cornerRadius}
          padding="18px 34px"
        >
          <span>Liquid Glass</span>
        </LiquidGlass>
      </div>

      <section className="tuner-controls">
        {controls.map((control) => (
          <label className="tuner-control" key={control.key}>
            <span>{control.key}</span>
            <input
              aria-label={`${control.key} slider`}
              type="range"
              min={control.min}
              max={control.max}
              step={control.step}
              value={draft[control.key]}
              onChange={(event) => update(control, event.currentTarget.value)}
            />
            <input
              aria-label={`${control.key} value`}
              type="number"
              min={control.min}
              max={control.max}
              step={control.step}
              value={draft[control.key]}
              onChange={(event) => update(control, event.currentTarget.value)}
            />
          </label>
        ))}
      </section>

      <footer>
        <button type="button" onClick={bridge.reset}>Reset</button>
        <button type="button" onClick={bridge.cancel}>Cancel</button>
        <button type="button" className="primary" onClick={bridge.save}>Save</button>
      </footer>
    </main>
  )
}
```

- [ ] **Step 5: Select the tuner page in `main.tsx`**

Use a dedicated root that posts ready, listens for validated settings, and leaves the default overlay branch unchanged:

```tsx
function TunerSurface() {
  const [settings, setSettings] = useState(officialLiquidGlassSettings)
  const webview = window.chrome?.webview

  useEffect(() => {
    if (!webview) {
      return
    }

    const onMessage = (event: MessageEvent<unknown>) => {
      const next = readLiquidGlassSettingsMessage(event.data)
      if (next) {
        setSettings(next)
      }
    }

    webview.addEventListener('message', onMessage)
    webview.postMessage({ type: 'tuner-ready' })
    return () => webview.removeEventListener('message', onMessage)
  }, [webview])

  if (!webview) {
    return null
  }

  return <LiquidGlassTuner settings={settings} bridge={createTunerBridge(webview)} />
}

const isTuner = new URLSearchParams(window.location.search).get('surface') === 'tuner'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {isTuner ? <TunerSurface /> : <App />}
  </StrictMode>,
)
```

- [ ] **Step 6: Style the independent panel without affecting the capsule**

Append only scoped tuner rules. Keep `.overlay`, `.scene-backdrop`, `.lyrics-glass`, and `.glass-content` unchanged:

```css
.tuner-shell {
  min-height: 100%;
  padding: 28px;
  color: #172033;
  background: #f5f7fb;
}

.tuner-shell header h1,
.tuner-shell header p {
  margin: 0;
}

.tuner-preview-scene {
  display: grid;
  min-height: 180px;
  margin: 22px 0;
  overflow: hidden;
  place-items: center;
  border-radius: 24px;
  background:
    radial-gradient(circle at 22% 28%, #ffb16a 0 12%, transparent 30%),
    radial-gradient(circle at 75% 68%, #6fd6ff 0 16%, transparent 34%),
    linear-gradient(135deg, #22304d, #7b64d9 52%, #e9eef8);
}

.tuner-preview-scene > div {
  color: white;
  font-size: 22px;
  font-weight: 700;
}

.tuner-controls {
  display: grid;
  gap: 14px;
}

.tuner-control {
  display: grid;
  grid-template-columns: 190px 1fr 96px;
  gap: 14px;
  align-items: center;
}

.tuner-control input[type="number"] {
  width: 96px;
}

.tuner-shell footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 24px;
}
```

- [ ] **Step 7: Run frontend tests and build**

Run:

```powershell
npm.cmd test
npm.cmd run build
```

Expected: all frontend tests pass and the production build exits 0.

- [ ] **Step 8: Commit**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/Frontend/src/LiquidGlassTuner.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/LiquidGlassTuner.test.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/tunerBridge.ts audio-share/src/AudioShare.Lyrics/Frontend/src/tunerBridge.test.ts audio-share/src/AudioShare.Lyrics/Frontend/src/main.tsx audio-share/src/AudioShare.Lyrics/Frontend/src/styles.css audio-share/src/AudioShare.Lyrics/Frontend/src/webview.d.ts
git commit -m "feat: add Lyrics liquid glass tuner UI"
```

---

### Task 6: Host one tuner window and connect right-click commands

**Files:**
- Create: `audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml`
- Create: `audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs`
- Modify: `audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs`
- Create: `audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs`

**Interfaces:**
- Consumes: `LiquidGlassSettingsController`, its `SettingsChanged` event, and `wwwroot/index.html?surface=tuner`.
- Produces: direct right-click opening, one tuner instance, live overlay settings messages, and Save/Cancel close semantics.

- [ ] **Step 1: Write failing WPF integration tests**

Add source-structure tests consistent with existing startup tests:

```csharp
[Fact]
public void MainWindow_RightClickOpensOneLiquidGlassTuner()
{
    var xaml = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml"));
    var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "MainWindow.xaml.cs"));
    Assert.Contains("PreviewMouseRightButtonUp=\"WindowSurface_OnPreviewMouseRightButtonUp\"", xaml);
    Assert.Contains("LiquidGlassTunerWindow? tunerWindow", source);
    Assert.Contains("tunerWindow.Activate()", source);
}

[Fact]
public void TunerWindow_LoadsTheReactTunerAndHandlesEveryCommand()
{
    var source = File.ReadAllText(FindRepositoryFile("src", "AudioShare.Lyrics", "LiquidGlassTunerWindow.xaml.cs"));
    Assert.Contains("index.html?surface=tuner", source);
    Assert.Contains("\"tuner-ready\"", source);
    Assert.Contains("\"liquid-preview\"", source);
    Assert.Contains("\"liquid-reset\"", source);
    Assert.Contains("\"liquid-cancel\"", source);
    Assert.Contains("\"liquid-save\"", source);
}
```

- [ ] **Step 2: Run WPF tests to verify RED**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests|FullyQualifiedName~FlowCastLyricsStartupTests.MainWindow_RightClickOpensOneLiquidGlassTuner"
```

Expected: tests fail because the tuner window and right-click handler do not exist.

- [ ] **Step 3: Create the WPF host window**

Create this normal 720x640 host window:

```xml
<Window x:Class="AudioShare.Lyrics.LiquidGlassTunerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
        Title="FlowCast Lyrics — Liquid Glass"
        Width="720"
        Height="640"
        MinWidth="620"
        MinHeight="560"
        WindowStartupLocation="CenterScreen"
        Background="#F5F7FB">
    <Grid>
        <TextBlock x:Name="TunerStatus"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"
                   Foreground="#4B5870"
                   Text="Loading Liquid Glass tuner…" />
        <wv2:WebView2 x:Name="TunerWebView"
                      Visibility="Hidden" />
    </Grid>
</Window>
```

Initialize the same virtual host mapping used by `MainWindow`, then navigate to `https://flowcast.local/index.html?surface=tuner`. Keep DevTools, context menus, accelerator keys, status bar, and zoom controls disabled.

The constructor accepts one `LiquidGlassSettingsController`. Use this complete lifecycle, including exact command dispatch and Cancel-on-close behavior:

```csharp
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AudioShare.Lyrics;

public partial class LiquidGlassTunerWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly LiquidGlassSettingsController controller;
    private bool closeCommitted;
    private bool webViewReady;

    internal LiquidGlassTunerWindow(LiquidGlassSettingsController controller)
    {
        this.controller = controller;
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        controller.SettingsChanged += Controller_OnSettingsChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await TunerWebView.EnsureCoreWebView2Async();
            var settings = TunerWebView.CoreWebView2.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            TunerWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(webRoot, "index.html")))
            {
                throw new FileNotFoundException("FlowCast Lyrics web assets were not found.", webRoot);
            }

            TunerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "flowcast.local",
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            TunerWebView.Source = new Uri("https://flowcast.local/index.html?surface=tuner");
        }
        catch (Exception)
        {
            TunerStatus.Text = "Unable to load the Liquid Glass tuner.";
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var message = JsonDocument.Parse(e.WebMessageAsJson);
            var root = message.RootElement;
            if (!root.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "tuner-ready":
                    webViewReady = true;
                    TunerWebView.Visibility = Visibility.Visible;
                    TunerStatus.Visibility = Visibility.Collapsed;
                    PostSettings();
                    break;
                case "liquid-preview":
                    if (root.TryGetProperty("settings", out var preview) &&
                        preview.Deserialize<LiquidGlassSettings>(JsonOptions) is { IsValid: true } settings)
                    {
                        controller.Preview(settings);
                    }
                    break;
                case "liquid-reset":
                    controller.Reset();
                    PostSettings();
                    break;
                case "liquid-cancel":
                    controller.Cancel();
                    closeCommitted = true;
                    Close();
                    break;
                case "liquid-save":
                    controller.Save();
                    closeCommitted = true;
                    Close();
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    private void Controller_OnSettingsChanged(object? sender, LiquidGlassSettings settings)
    {
        _ = Dispatcher.BeginInvoke(PostSettings);
    }

    private void PostSettings()
    {
        if (webViewReady && TunerWebView.CoreWebView2 is not null)
        {
            TunerWebView.CoreWebView2.PostWebMessageAsJson(controller.GetSettingsJson());
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!closeCommitted)
        {
            controller.Cancel();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        controller.SettingsChanged -= Controller_OnSettingsChanged;
        if (TunerWebView.CoreWebView2 is not null)
        {
            TunerWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        TunerWebView.Dispose();
    }
}
```

- [ ] **Step 4: Connect the controller to the normal overlay**

Construct one controller in `MainWindow`:

```csharp
private readonly LiquidGlassSettingsController liquidGlassSettings =
    new(LiquidGlassSettingsStore.CreateDefault());
private LiquidGlassTunerWindow? tunerWindow;
```

Subscribe `liquidGlassSettings.SettingsChanged` and use this forwarding method:

```csharp
private void LiquidGlassSettings_OnSettingsChanged(object? sender, LiquidGlassSettings settings)
{
    if (webViewReady)
    {
        OverlayWebView.CoreWebView2.PostWebMessageAsJson(liquidGlassSettings.GetSettingsJson());
    }
}
```

In the overlay WebView ready handler, post both `presenter.GetSnapshotJson()` and `liquidGlassSettings.GetSettingsJson()` before showing the web surface. Unsubscribe the settings event in `MainWindow_OnClosed`.

- [ ] **Step 5: Add direct right-click opening and single-instance focus**

Attach `PreviewMouseRightButtonUp="WindowSurface_OnPreviewMouseRightButtonUp"` to `WindowSurface` and add this code without changing the existing left-button handler:

```csharp
private void WindowSurface_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
{
    e.Handled = true;
    if (tunerWindow is { IsVisible: true })
    {
        tunerWindow.Activate();
        return;
    }

    tunerWindow = new LiquidGlassTunerWindow(liquidGlassSettings);
    tunerWindow.Closed += TunerWindow_OnClosed;
    tunerWindow.Show();
}

private void TunerWindow_OnClosed(object? sender, EventArgs e)
{
    if (tunerWindow is not null)
    {
        tunerWindow.Closed -= TunerWindow_OnClosed;
        tunerWindow = null;
    }
}
```

- [ ] **Step 6: Run focused and full tests**

Run:

```powershell
dotnet test audio-share\tests\AudioShare.Core.Tests\AudioShare.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiquidGlassTunerWindowTests|FullyQualifiedName~FlowCastLyricsStartupTests"
dotnet test audio-share\AudioShare.sln --no-restore
```

Expected: focused tests and all .NET tests pass with zero failures.

- [ ] **Step 7: Commit**

```powershell
git add -- audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml audio-share/src/AudioShare.Lyrics/LiquidGlassTunerWindow.xaml.cs audio-share/src/AudioShare.Lyrics/MainWindow.xaml audio-share/src/AudioShare.Lyrics/MainWindow.xaml.cs audio-share/tests/AudioShare.Core.Tests/FlowCastLyricsStartupTests.cs audio-share/tests/AudioShare.Core.Tests/LiquidGlassTunerWindowTests.cs
git commit -m "feat: open one Lyrics glass tuner on right click"
```

---

### Task 7: Verify the complete live-tuning story and hand control to the user

**Files:**
- Verify only: `audio-share/src/AudioShare.Lyrics/**`
- Verify only: `audio-share/tests/AudioShare.Core.Tests/**`
- Generated QA artifact: `work/lyrics-liquid-glass-tuner-qa.png`

**Interfaces:**
- Consumes: all tasks above.
- Produces: a tested debug build that the user can tune; no packaged release yet.

- [ ] **Step 1: Run fresh frontend verification**

```powershell
Set-Location audio-share\src\AudioShare.Lyrics\Frontend
npm.cmd ci
npm.cmd test
npm.cmd run build
```

Expected: dependency audit has zero vulnerabilities, every Vitest test passes, and Vite exits 0.

- [ ] **Step 2: Run fresh .NET verification**

```powershell
Set-Location audio-share
dotnet test AudioShare.sln --no-restore
dotnet publish src\AudioShare.Lyrics\AudioShare.Lyrics.csproj --configuration Debug --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output dist\FlowCast-Lyrics-tuner-debug
```

Expected: all .NET tests pass and `dist\FlowCast-Lyrics-tuner-debug\FlowCast Lyrics.exe` is produced.

- [ ] **Step 3: Perform the original visual regression check**

Launch the debug EXE. Confirm the normal surface is only the 180x44 capsule, contains no blue-white fake gradient, and uses the desktop backdrop under the real `LiquidGlass` component.

- [ ] **Step 4: Perform the tuner interaction check**

Verify this exact sequence:

1. Left-drag moves the capsule.
2. Right-click opens one tuner.
3. A second right-click focuses the same tuner and does not create another process or window.
4. Moving every slider changes the live capsule immediately.
5. Reset previews the six official defaults.
6. Cancel restores the saved values and closes the tuner.
7. Reopen, change values, Save, close and restart FlowCast Lyrics.
8. The saved values reappear after restart.
9. Close the tuner and confirm its WebView process is released while the overlay remains running.

- [ ] **Step 5: Capture visual proof**

Save one screenshot containing the tuner and live capsule to `work/lyrics-liquid-glass-tuner-qa.png`, then inspect it at original resolution. Reject the build if the capsule is a flat fixed gradient or the tuner introduces extra controls beyond the six agreed props.

- [ ] **Step 6: Check repository scope and cleanliness**

```powershell
git diff --check
git status --short
git log -6 --oneline
```

Expected: no source changes remain unstaged from this plan; the protected `AudioShare.App` modifications remain present but uncommitted and untouched; only generated `dist`, `release`, `work`, and pre-existing unrelated untracked files remain.

- [ ] **Step 7: Stop before release packaging**

Give the debug build to the user and ask them to tune and Save their desired values. Do not create a release ZIP, update manifest, GitHub release, or final EXE package until the user confirms the saved appearance.
