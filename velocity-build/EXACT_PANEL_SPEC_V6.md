# MCB v6 Exact Panel Specification

Source of truth: user-provided `neverlose_gui_final_complete_mcb_glass.zip`.

## Fidelity rule

The uploaded HTML/CSS/JS and preview images are the only visual/interaction reference for the v6 panel. v5's 7-tab wide-sidebar reskin is not considered complete.

## Reference geometry / tokens

- Base viewport: 846 x 682
- Sidebar: 203 px
- Workspace: 643 px
- Topbar: 66 px
- Background: #05070A
- Sidebar: #07111A
- Sidebar deep: #050B11
- Surface: #080D13
- Surface 2: #09121B
- Surface 3: #0B1722
- Stroke: #142536
- Soft stroke: #0F1C29
- Text: #EEF3F7
- Muted: #8A96A3
- Dim: #4D5B69
- Accent: #13BFF5
- Danger: #F06F7F
- Radius: 6 px

## Interactive views (10)

Sidebar navigation (9):
1. Ragebot
2. Anti Aim
3. Legitbot
4. Players
5. World
6. Inventory
7. Main
8. Scripts
9. Configs

Separate Settings view (topbar/settings action):
10. Settings

## Source-defined behavior

The uploaded source explicitly describes the restoration as UI-only/local presentation. Preserve that behavior exactly where the source labels controls as `UI state only`, `Preview state only`, `Browser local only`, or `execution disabled`.

Required local UI functions:
- local state persistence
- keyboard navigation
- DPI scaling (75–150%)
- menu opacity
- expanded/compact/auto sidebar presentation
- menu animations
- language selector presentation
- notification position
- menu key presentation
- theme presets
- screen-widget visibility and movable preview elements
- search/filter behavior and empty state
- dropdown expanded state
- color picker expanded state
- keybind capture state
- multi-select/tag selector
- advanced/context menu state
- confirmation modal
- notifications/toasts
- unsaved-config visual state
- config local snapshots
- config create/load/save/delete
- local JSON import/export presentation
- inventory filter/search/rarity/sort/grid/list selection
- inventory inspect drawer
- scripts search/list/status/details
- read-only script editor presentation
- script lifecycle visual states

## Showcase/UI states

The source reports 48 UI states. v6 must not be labeled exact until all source states are represented or intentionally documented as source-only showcase states.

## Safety / scope

This specification is for GUI/local interaction fidelity only. Do not add game-process communication, anti-cheat evasion, injection changes, aim/ESP behavior, memory/process access, network manipulation, or gameplay automation as part of the exact-panel work.

## Validation labels

Do not call static source markers `performance PASS`.
Use separate labels:
- BUILD_PASS
- STATIC_SOURCE_CHECK_PASS
- UI_MODEL_COVERAGE_PASS
- LOCAL_INTERACTION_SMOKE_PASS
- IN_GAME_NOT_TESTED
- PIXEL_FIDELITY_NOT_VERIFIED (until a native panel screenshot has been compared against the supplied preview)
