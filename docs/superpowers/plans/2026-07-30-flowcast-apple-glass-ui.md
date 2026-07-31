# FlowCast Apple Glass UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace FlowCast's light utility-style main window with the approved Apple-inspired glass interface while retaining all audio-routing behavior.

**Architecture:** Keep `MainWindow.xaml.cs` as the behavior owner and rebuild only `MainWindow.xaml` presentation resources and layout. Existing named controls and event handlers remain unchanged so routing, stop safety, timer, diagnostics, tutorial, and update behavior keep their current interfaces.

**Tech Stack:** WPF XAML, .NET 8, existing FlowCast code-behind and tests.

## Global Constraints

- Keep all FlowCast audio routing and safety behavior unchanged.
- Keep the Update button hidden unless the existing update check exposes it.
- Keep timer settings hidden until the user selects the timer control.
- Use Apple-inspired visual principles without Apple trademarks, copied assets, or system materials.
- Do not add external UI dependencies.

---

### Task 1: Rebuild the Main Window Visual Hierarchy

**Files:**
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`

**Interfaces:**
- Consumes: Existing named controls `FlowCastStatusText`, `Applications`, `UpdateButton`, `TimerPanel`, and all existing click handlers.
- Produces: A responsive frosted-glass main window that preserves every existing named control and handler.

- [ ] **Step 1: Replace global window resources and background**

Add layered gradient background shapes and reusable glass, icon, primary-action, and program-row styles. Use no external assets beyond the existing FlowCast logo.

- [ ] **Step 2: Replace header and sharing-status presentation**

Use a translucent title bar with the FlowCast mark, compact utility controls, and a large status card. Preserve `FlowCastStatusText`, `FlowCastStatusHintText`, `RecentActivityText`, `UpdateButton`, and their handlers.

- [ ] **Step 3: Replace program list and controls presentation**

Render each active program as a floating glass row while preserving the selection bindings and `ApplicationSelectionChanged` handler. Render Banana requirement, routing controls, timer panel, and error panel as distinct glass/safety surfaces.

- [ ] **Step 4: Build the application**

Run: `dotnet build audio-share/AudioShare.sln --no-restore --nologo -v:minimal`

Expected: Build succeeds without warnings or errors.

### Task 2: Verify Interactive States and Accessibility

**Files:**
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml` only if visual state changes require small XAML corrections.
- Test: `audio-share/tests/AudioShare.Core.Tests`

**Interfaces:**
- Consumes: Existing `MainWindow.xaml.cs` state setters and controls.
- Produces: Correct visual enablement for no-update, sharing, local-only, timer, Banana-missing, and error states.

- [ ] **Step 1: Verify named control preservation**

Run: `rg -n "x:Name=\"(FlowCastStatusText|UpdateButton|TimerPanel|StartStopTimerButton|ErrorPanel)\"" audio-share/src/AudioShare.App/MainWindow.xaml`

Expected: Every required control remains defined exactly once.

- [ ] **Step 2: Run the full automated suite**

Run: `dotnet test audio-share/AudioShare.sln --no-restore --nologo -v:minimal`

Expected: All existing tests pass.

- [ ] **Step 3: Inspect the final XAML diff**

Run: `git diff --check -- audio-share/src/AudioShare.App/MainWindow.xaml`

Expected: No whitespace errors.

### Task 3: Package and Install the Updated App

**Files:**
- Generated: `D:/codexhome/scratch/flowcast-release-1.0.3/FlowCast Setup.exe`

**Interfaces:**
- Consumes: Existing NSIS installer script and verified router-helper payload.
- Produces: A self-contained user-scope FlowCast Setup with the visual update.

- [ ] **Step 1: Increase the application version to `1.0.3`**

Update `Version`, `AssemblyVersion`, and `FileVersion` in `audio-share/src/AudioShare.App/AudioShare.App.csproj` from `1.0.2` to `1.0.3`.

- [ ] **Step 2: Produce the self-contained publish output**

Publish for `win-x64`, copy the verified router-helper payload, regenerate its manifest hash, and compile `installer/FlowCast.nsi` using the existing NSIS scratch tool.

- [ ] **Step 3: Install and verify**

Run the generated Setup silently for the current user, then confirm the installed executable reports `1.0.3.0` and the router-helper manifest hash matches the copied helper file.
