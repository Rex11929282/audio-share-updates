# FlowCast Apple-Inspired Glass UI Design

## Goal

Replace the current utility-style interface with a focused Apple-inspired glass interface. Remove the Discord-inspired layout entirely. The application remains FlowCast and does not use Apple trademarks, logos, or copied assets.

## Layout

- A translucent macOS-style title bar contains the FlowCast mark, title, and compact circular utility controls.
- The main panel contains one prominent sharing-status card, the detected program list, and a compact floating bottom control bar.
- The sharing-status card is the visual priority. It displays the active program, B1 destination, animated-style waveform, and the single primary action: Stop Sharing.
- Each program row shows an application icon, name, playback detail, selection control, and one of two state labels: Sharing or Only Me.

## Interaction

- Selecting a program and pressing Start Sharing keeps the existing routing confirmation and verification behavior.
- Stop Sharing remains the single primary safety action and clears selections after verified local-only routing.
- The timer remains opt-in. Its compact button is always visible; minute controls appear only after it is selected, and Start Timer is enabled only while sharing.
- Tutorial, diagnostics, refresh, and an update button (only when an update exists) remain available as secondary actions.

## Visual Language

- Background: a soft pearl-gray gradient with restrained blue, mint, and warm coral light blooms.
- Surfaces: translucent white frosted-glass panels with background blur, soft shadows, hairline borders, and generous whitespace.
- Accent colors: blue for sharing controls, mint for healthy Banana/device state, red only for Stop Sharing or unsafe state.
- Use 18px rounded glass cards, SF-like system typography fallback, spacious rows, and one clear primary action per state.
- Use the bundled FlowCast mark. Do not use Discord branding, mascot, logo, or copied icon assets.

## Verification

- Verify the main interface at normal desktop size and minimum window size.
- Verify light-on-dark text contrast and disabled controls.
- Verify no-update state hides the Update action.
- Verify timer configuration remains hidden until selected.
- Run the existing full test suite and package a new Setup before installation.
