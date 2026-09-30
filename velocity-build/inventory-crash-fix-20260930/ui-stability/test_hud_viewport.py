"""Portable HUD-only regression: compiles helper + actual keybind layout prepass.

Run: python source92/build-tools/test_hud_viewport.py
Requires g++/clang++ (or set CXX); never builds or loads the game DLL.
"""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import sys

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[1] / 'native-source'
widgets = (root / 'project/core/rendering/impl/widgets.cpp').read_text(encoding='utf-8')
start = widgets.index('// Clamp the actual spring/fade bounds, including rows leaving the list.')
end = widgets.index('static auto icon_w_px', start)
layout = widgets[start:end]
assert 'smoothed_base_y.value( ) + animated_min_y' in layout
assert 'animated_max_y - animated_min_y' in layout
assert widgets.count('row_offsets[ i ]') == 4
assert 'std::max( w, target_w )' in widgets
assert widgets.count('rendering::viewport::clamp_origin(') == 9
assert (root / 'project/core/features/esp/other/other.overlay.cpp').read_text(encoding='utf-8').count('rendering::viewport::clamp_origin(') == 5
assert (root / 'project/core/features/misc/impl/impacts.cpp').read_text(encoding='utf-8').count('rendering::viewport::clamp_origin(') == 2

preamble = r'''
#include <core/rendering/viewport_bounds.hpp>
#include <array>
#include <iostream>
#include <limits>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>
static int checks = 0;
#define CHECK(x) do { ++checks; if (!(x)) throw std::runtime_error(#x); } while (false)
struct Fade {
    float v = 0; int updates = 0, in_calls = 0, out_calls = 0;
    float alpha() const { return v; }
    void fade_in(float) { ++in_calls; }
    void fade_out(float) { ++out_calls; }
    void update() { ++updates; }
};
struct Spring {
    float v = 0, step = 0, target = 0; int updates = 0, snaps = 0;
    float value() const { return v; }
    void snap(float x) { v = x; ++snaps; }
    void set_target(float x) { target = x; }
    void update() { v += step; ++updates; }
};
struct State { Fade alpha; Spring offset_y; bool active_this_frame = false; };
struct Entry { std::string name; };
struct Config { struct { bool value = true; } show_header; };
struct Frame { float base, lower, upper; std::array<float,32> offsets{}, alphas{}; };
Frame position(std::map<std::string,State>& row_states, const std::vector<Entry>& entries,
               float wanted, int screen_h, bool show_header = true, float scale = 1) {
    Config kb_cfg; kb_cfg.show_header.value = show_header;
    const float row_h = 21 * scale, header_h = 24 * scale, row_spacing = 3 * scale;
    const float header_block_h = show_header ? header_h + row_spacing : 0;
    const int count = static_cast<int>(entries.size());
    Spring smoothed_base_y; smoothed_base_y.v = wanted;
'''
postamble = r'''
    Frame result{base_ry, animated_min_y, animated_max_y};
    std::copy(std::begin(row_offsets), std::end(row_offsets), result.offsets.begin());
    std::copy(std::begin(row_alphas), std::end(row_alphas), result.alphas.begin());
    return result;
}
int main() { try {
    using rendering::viewport::clamp_origin;
    CHECK(clamp_origin(10,100,1920) == 10);
    CHECK(clamp_origin(-9999,100,1920) == 0);
    CHECK(clamp_origin(9999,100,1920) == 1820);
    CHECK(clamp_origin(10,200,100) == 0);
    CHECK(clamp_origin(10,100,100) == 0);
    CHECK(clamp_origin(10,100,0) == 0);
    CHECK(clamp_origin(10,100,-1) == 0);
    CHECK(clamp_origin(10,-1,100) == 0);
    for (float bad : {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity(), -std::numeric_limits<float>::infinity()}) {
        CHECK(clamp_origin(bad,100,1920) == 0);
        CHECK(clamp_origin(10,bad,1920) == 0);
        CHECK(clamp_origin(10,100,bad) == 0);
    }
    for (float viewport : {0.f, 1.f, 20.f, 70.f, 480.f, 1080.f, 1920.f, 3840.f})
        for (float scale : {.85f,1.f,1.35f})
            for (float extent : {0.f,21.f,24.f,78.f,174.f,800.f,4096.f})
                for (float offset : {-10000.f,-1.f,0.f,1.f,10000.f}) {
                    const float size = extent * scale;
                    const float saved_offset = offset;
                    const float x = clamp_origin(10 + offset, size, viewport);
                    CHECK(std::isfinite(x) && x >= 0);
                    CHECK(offset == saved_offset);
                    if (size <= viewport) CHECK(x + size <= viewport + .001f);
                    else CHECK(x == 0);
                    CHECK(clamp_origin(x,size,viewport) == x);
                }
    // A growing watermark must include pills wider than its animated background.
    CHECK(clamp_origin(1920-300-10, std::max(300.f,500.f),1920) == 1420);
    std::map<std::string,State> rows;
    rows["new"] = {};
    rows["moving"].alpha.v = .6f; rows["moving"].offset_y.v = -8; rows["moving"].offset_y.step = -2;
    rows["fading"].alpha.v = .2f; rows["fading"].offset_y.v = 180; rows["fading"].offset_y.step = 5;
    rows["expired"].offset_y.v = 2500;
    auto f = position(rows, {{"new"},{"moving"}},1000,300);
    CHECK(f.lower == -10 && f.upper == 206);
    CHECK(f.base == 94 && f.base + f.upper == 300);
    CHECK(f.offsets[0] == 27 && f.offsets[1] == -10);
    CHECK(f.alphas[0] == 0 && f.alphas[1] == .6f);
    CHECK(rows.count("expired") == 0 && rows.count("fading") == 1);
    CHECK(rows["new"].offset_y.snaps == 1);
    for (auto const& [name,state] : rows) {
        CHECK(state.alpha.updates == 1 && state.offset_y.updates == 1);
        CHECK((name == "fading") == (state.alpha.out_calls == 1));
    }
    const auto before = rows;
    f = position(rows, {{"new"},{"moving"}},-10000,70);
    CHECK(f.base + f.lower == 0); // Oversized stack is top-aligned, not collapsed.
    CHECK(f.upper - f.lower > 70);
    for (auto const& [name,state] : rows) CHECK(state.offset_y.updates == before.at(name).offset_y.updates + 1);
    std::map<std::string,State> duplicate;
    duplicate["same"].offset_y.step = 1;
    f = position(duplicate, {{"same"},{"same"}},10,300);
    CHECK(f.offsets[0] == 28 && f.offsets[1] == 29); // Preserve per-entry sampling.
    std::map<std::string,State> empty;
    f = position(empty, {},9999,5,false);
    CHECK(f.base == 5 && f.lower == 0 && f.upper == 0);
    std::map<std::string,State> full;
    std::vector<Entry> entries;
    for (int i=0;i<32;++i) entries.push_back({std::to_string(i)});
    f = position(full,entries,9999,1080,false,1.35f);
    CHECK(f.base >= 0 && f.base + f.upper <= 1080.001f);
    CHECK(f.offsets[31] > f.offsets[0]);
    std::cout << "HUD viewport checks=" << checks << "\nproduction_helper=PASS\nproduction_keybind_layout_prepass=PASS\nengine_backend=NONE\n";
    return 0;
} catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; } }
'''
compiler = os.environ.get('CXX') or shutil.which('g++') or shutil.which('clang++')
if not compiler:
    raise RuntimeError('A portable C++ compiler is required; set CXX')
with tempfile.TemporaryDirectory(prefix='hud-viewport-test-') as temp:
    path = Path(temp)
    source = path / 'hud_viewport_test.cpp'
    source.write_text(preamble + layout + postamble,encoding='utf-8')
    binary = path / ('hud_viewport_test.exe' if os.name == 'nt' else 'hud_viewport_test')
    subprocess.run([compiler,'-std=c++17','-Wall','-Wextra','-Werror','-pedantic','-fsanitize=undefined','-I'+str(root/'project'),str(source),'-o',str(binary)],check=True)
    subprocess.run([str(binary)],check=True)
print('HUD production call-site checks=PASS')
