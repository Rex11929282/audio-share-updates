# Audio Share Visual Tutorial Design

## Goal

Replace the current text-only tutorial with a clear Simplified Chinese, in-app visual guide. It teaches a user how to share only selected application audio through Voicemeeter Banana and Discord, without covering Voicemod.

## Scope

- Keep the tutorial as an in-app window opened from `使用教程`.
- Add real configuration screenshots for Voicemeeter Banana and Discord voice settings.
- Draw numbered arrows and labels over screenshots at runtime so the annotations remain crisp at different window sizes.
- Explain each step with four short sections: what to do, why it matters, success result, and a recovery action.
- Add a dedicated rules section and common troubleshooting section.
- Do not change audio-routing behavior, device selection, Discord settings, or Voicemod behavior.

## Tutorial Structure

1. Before starting: Voicemeeter Banana must be installed and restarted.
2. Banana A1: choose the real headphone or speaker for local playback.
3. Banana buses: `Voicemeeter Input` uses A1 + B1; `Voicemeeter AUX Input` uses A1 only.
4. Discord: microphone is `Voicemeeter Out B1`; speaker is `Voicemeeter AUX Input`.
5. Audio Share: select only the applications a friend should hear, then apply the route.
6. Stop sharing: `停止分享，只自己听` and normal window close both put active applications on AUX.

## Rules

- Selected application: sent to B1, therefore a Discord friend can hear it.
- Unselected application: sent to AUX, therefore only the local listener hears it.
- Discord playback stays on AUX so a friend's voice is not returned to the friend.
- Stopping or closing Audio Share sends currently detected applications to AUX, not B1.

## Visuals

- Use the captured Voicemeeter Banana window as the real interface reference.
- Capture a Discord `语音和视频` settings image with Pi only when the page contains no private messages; otherwise use the existing user-provided settings screenshot.
- Use red arrows, numbered circles, and small opaque labels. Never cover the target control itself.

## Troubleshooting

- I cannot hear computer audio: check Banana A1.
- Friend cannot hear selected music: check the selected application and B1.
- Friend hears an echo: check Discord speaker is AUX and Discord is never selected for sharing.
- Application is missing: start playback, then click `重新检测`.

## Verification

- Unit tests cover tutorial content, rules, and no-Voicemod wording.
- Build succeeds with no warnings or errors.
- A Pi inspection verifies the tutorial opens, images render, arrows align to their targets, and the existing audio settings remain unchanged.
