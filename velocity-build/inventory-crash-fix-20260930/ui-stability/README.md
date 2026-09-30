# UI stability follow-up (2026-09-30)

Baseline: `f1ab0e76098287f8c3460ea673ec02c07a6d7008`, Actions Run 92.

This patch is applied after the existing inventory patch. It only changes UI storage, layout, drawing bounds and search navigation. It does not change feature algorithms or automatically execute configuration/script actions.

- Remaining Traditional Chinese key/Lua labels use Simplified Chinese; font fallback prefers YaHei/SimSun
- Stable XUI window storage prevents retained parent pointers from becoming dangling when inventory options or nested children open
- Checkbox layout reserves displayed labels and bind badges
- Inventory options grow to fit controls; cards start below the toolbar and agent-team cards wrap at one column
- Nested auto-height sections can grow inside a scrollable page
- LEGIT uses one column below the existing 760-pixel content breakpoint
- Miscellaneous sections can all remain expanded in a single outer scroll page
- Search includes non-bind controls and uses the correct navigation destinations
- Sidebar toggles preserve the user-resized width
- HUD draw coordinates stay within the viewport without overwriting saved offsets

The existing quick-configuration removal stays in place. Normal configuration files remain the filesystem source.

## Validation

- `native_regression.cpp` is inserted into the existing native D3D11 WARP test harness and runs against the actual XUI implementation
- `test_remaining_ui_model.cpp` compiles directly against the production UI model
- `python test_hud_viewport.py <native-source-root>` compiles the production viewport helper and extracted keybind layout prepass with UBSan where supported
- The former vector window-storage pattern reproduces a heap-use-after-free under AddressSanitizer; the deque pattern preserves parent addresses

Passing these checks does not prove CS2 gameplay execution or reproduction of a user's game crash. In-game testing remains explicitly unverified.
