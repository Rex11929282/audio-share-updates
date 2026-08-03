# FlowCast Lyrics Liquid Glass Tuner Design

**Date:** 2026-08-03
**Status:** Approved on 2026-08-03

## Goal

Let the user tune the real `rdev/liquid-glass-react` effect live without adding controls to the normal FlowCast Lyrics capsule. The normal friend experience remains a small, borderless, topmost Dynamic Island. No custom gradient or imitation glass controls are permitted.

## Confirmed interaction

- Left-drag continues to move the capsule.
- Right-clicking the capsule opens a single independent React tuning window.
- Opening the command again focuses the existing tuning window instead of creating another one.
- Closing the tuning window releases its WebView. Friends who do not open it only see the capsule.

## Official component contract

FlowCast Lyrics continues to use `liquid-glass-react` 1.1.1. The tuner edits these six component props directly:

| Prop | Tuner range | Official reset value |
| --- | ---: | ---: |
| `displacementScale` | 0–200 | 70 |
| `blurAmount` | 0–1 | 0.0625 |
| `saturation` | 0–300 | 140 |
| `aberrationIntensity` | 0–20 | 2 |
| `elasticity` | 0–1 | 0.15 |
| `cornerRadius` | 0–999 | 999 |

Each control has both a slider and numeric input. The component remains in the existing `standard` mode; mode selection and other package props are outside this tuner scope.

## Architecture and data flow

1. The Lyrics WPF process owns the saved and draft `LiquidGlassSettings` values.
2. The normal overlay WebView receives the current settings and passes them unchanged to `LiquidGlass`.
3. A second WPF window hosts the React tuner only while it is open.
4. Slider or numeric-input changes send draft settings through WebView2 to WPF.
5. WPF validates the six finite, in-range numbers and forwards valid drafts to the normal overlay immediately.
6. No slider event writes to disk.

The existing desktop backdrop feed remains responsible only for supplying pixels that the GitHub component can refract. It does not add a visual style or replace any package prop.

## Commands

- **Reset:** replaces the draft with the official values in the table and previews them immediately. It does not save.
- **Cancel:** restores the last saved settings in the overlay and closes the tuner.
- **Save:** validates and persists the draft, keeps it applied to the overlay, and closes the tuner.
- Closing the tuner with its window close button behaves like Cancel.

## Persistence and recovery

Settings are stored at `%LocalAppData%\FlowCast Lyrics\liquid-glass-settings.json`.

- A missing file loads the official reset values.
- Invalid JSON, missing fields, non-finite values, or out-of-range values load the official reset values without blocking application startup.
- Saving writes one complete settings document; partial settings are not persisted.

## React tuner UI

The panel contains a preview surface, the six labeled controls, their current numeric values, and Reset, Cancel, and Save actions. Labels use the package prop names so the user can compare them directly with the GitHub README. The panel does not add color, gradient, shadow, or other fake-glass controls.

## Scope boundaries

- Do not change `AudioShare.App/App.xaml.cs` or `AudioShare.App/MainWindow.xaml.cs`.
- Do not change FlowCast audio routing, Radmin discovery, NetEase lyric retrieval, or lyric transport.
- Do not publish a GitHub release as part of this work.
- Do not package a new EXE until the user has tuned and saved the desired values.

## Verification

- Frontend tests prove all six values reach the real `LiquidGlass` props and that Reset uses the official defaults.
- Frontend tests prove draft messages update the preview and invalid messages are rejected.
- .NET tests cover settings validation, load/save recovery, single-instance tuner behavior, and Save/Cancel semantics.
- Integration verification confirms right-click opens one tuner, sliders update the live capsule, Cancel restores saved values, and Save survives restart.
- Visual verification compares the capsule against the GitHub component behavior over an actual background; a fixed blue-white gradient is a regression.

## Success criteria

The user can right-click the capsule, tune all six agreed GitHub props with immediate visual feedback, cancel safely, or save values that survive restart. With the tuner closed, FlowCast Lyrics remains only the movable Dynamic Island and incurs no second-WebView cost.
