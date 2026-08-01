# FlowCast Feedback Motion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add low-cost, state-driven visual feedback to FlowCast without changing audio-routing behavior.

**Architecture:** `MainWindow` continues to own routing truth and invokes `MotionController` only after existing state transitions complete. `MotionController` owns all WPF Storyboards, enforces the 30 FPS and reduced-motion/minimized gates, and always restores final values.

**Tech Stack:** .NET 8, WPF, Storyboard, existing `AudioShare.Core` tests.

## Global Constraints

- Do not modify Windows routing, B1 configuration, or Discord configuration.
- Do not use looping decorative animations.
- Stop all Storyboards when minimized, closing, or reduced motion is enabled.

---

### Task 1: Expand event-driven motion primitives

**Files:**
- Modify: `audio-share/src/AudioShare.App/MotionController.cs`
- Test: `audio-share/src/AudioShare.App/AudioShare.App.csproj` Release build

- [ ] Add motion methods for confirmed sharing, confirmed local-only state, audio-level pulse, selection feedback, attention feedback, timer feedback, and window resume.
- [ ] Keep all calls gated by `CanPlay`, use a 30 FPS maximum, and remove all completed Storyboards.
- [ ] Ensure `Suspend` and reduced motion restore opacity, scale, position, and visibility-safe final values.
- [ ] Run `dotnet build audio-share/AudioShare.sln -c Release` and expect zero warnings and zero errors.

### Task 2: Attach visual elements to existing route state events

**Files:**
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`

- [ ] Add named visual targets for the route beacons, status confirmation, B1 waveform, timer progress, and program-card selection.
- [ ] Invoke sharing feedback only after the existing route success branch, local-only feedback only after the existing reset success branch, and audio-level pulse only from the existing B1 status refresh.
- [ ] Invoke selection feedback only after `RouteCoordinator` confirms selection state.
- [ ] Invoke timer feedback only when the schedule state changes.
- [ ] Run the existing app and verify that launch, minimize/restore, selection, share, stop, timer, and reduced-motion paths do not throw.

### Task 3: Regression verification

**Files:**
- Test: `audio-share/tests/AudioShare.Core.Tests/*.cs`

- [ ] Run `dotnet test audio-share/AudioShare.sln --no-restore` and expect all tests to pass.
- [ ] Run `dotnet build audio-share/AudioShare.sln -c Release --no-restore` and expect zero warnings and zero errors.
- [ ] Run `git diff --check` and manually review every changed file for accidental routing-policy changes.
