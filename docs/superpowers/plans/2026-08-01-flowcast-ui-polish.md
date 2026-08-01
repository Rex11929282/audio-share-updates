# FlowCast UI Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add atmosphere, interaction feedback, and progressive disclosure to FlowCast's existing WPF UI.

**Architecture:** `MainWindow` remains the source of truth for sharing and B1 measurements. `MotionController` renders the new visual-only atmosphere state and resets it when motion is suspended. XAML handles button press feedback and health-detail layout.

**Tech Stack:** .NET 8, WPF, existing FlowCast motion controller.

## Tasks

### Task 1: Atmosphere and launch motion

- [ ] Add named atmosphere segments to `MainWindow.xaml`.
- [ ] Extend `MotionController` with launch and B1-level rendering that resets when suspended.
- [ ] Call it only from the existing confirmed-sharing B1 signal path.

### Task 2: Interaction hierarchy

- [ ] Add pressed/released button Storyboards to the shared button template.
- [ ] Add a compact health summary and an on-demand details panel without changing health probe data.

### Task 3: Verification

- [ ] Build Release with zero warnings/errors.
- [ ] Run all tests and `git diff --check`.
- [ ] Review that no routing code changed.
