# FlowCast Presence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a perceptible launch sequence, B1 signal self-test, and safe system-tray controls.

**Architecture:** `MotionController` owns visual-only launch timing. `MainWindow` derives the self-test from the existing B1 status and owns the notification icon lifecycle. No route executor or Voicemeeter command changes are permitted.

**Tech Stack:** WPF, DispatcherTimer, Windows Forms NotifyIcon, xUnit.

## Global Constraints

- B1 self-test must only reflect the existing B1 meter reading.
- Minimize suspends all launch and B1 visual motion.
- Tray stopping must use the existing confirmation action.
- Audio routing remains untouched.

---

### Task 1: Add visual-only status feedback

**Files:**
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml`
- Modify: `audio-share/src/AudioShare.App/MotionController.cs`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`

- [ ] Add a launch sheen and self-test label to the status card.
- [ ] Drive a three-second, non-blocking sheen and ready-state pulse from `MotionController.PlayLaunch()`, then reset it during suspend/reduce motion.
- [ ] Map sharing plus B1 level to clear, non-claiming self-test copy.
- [ ] Build Release with zero warnings.

### Task 2: Add system-tray controls

**Files:**
- Modify: `audio-share/src/AudioShare.App/AudioShare.App.csproj`
- Modify: `audio-share/src/AudioShare.App/MainWindow.xaml.cs`

- [ ] Enable Windows Forms only for `NotifyIcon`.
- [ ] Initialize and dispose the tray icon with Show FlowCast and Stop Sharing, Only Me actions.
- [ ] Hide only on minimize; retain the existing close and reset behavior.
- [ ] Build and run the full test suite.
