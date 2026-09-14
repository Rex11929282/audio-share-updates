# MCB v6 Exact Panel implementation checklist

1. Replace v5's 7-tab navigation with the source's 9 sidebar pages plus separate Settings view.
2. Reproduce source geometry/tokens without substituting legacy spacing or icon-only navigation.
3. Introduce source-matching local page model/state independent from gameplay settings.
4. Implement source control types: toggle, range, select, segmented selector, keybind capture, color, multi-select tags.
5. Implement source custom pages: Inventory, Scripts, Configs, Settings.
6. Implement command search/filter and empty state.
7. Implement local persistence and local config snapshots.
8. Implement source keyboard chrome: Ctrl+S save, Ctrl+B sidebar presentation, Esc close, search action.
9. Implement Settings DPI/menu opacity/theme/widget preview states.
10. Implement the 48 documented UI/showcase states that belong to the interactive menu; explicitly document showcase-only cards that are not part of the normal interactive window.
11. Preserve existing MCB Lua/config functionality only as an extension when it does not alter the source-defined exact UI. Do not reinterpret source UI-only controls as gameplay actions.
12. Add coverage validation that counts source views/states rather than only checking strings.
13. Native screenshot comparison against supplied reference before calling the panel pixel-exact.
