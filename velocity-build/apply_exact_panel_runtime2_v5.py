from pathlib import Path
import ast
import re
import sys

root = Path(sys.argv[1]).resolve()
patch_dir = Path(__file__).resolve().parent
v = root / "cs2" / "MCB-CS2"
menu = v / "project/core/rendering/impl/menu/menu.core.cpp"
hpp = v / "project/core/rendering/rendering.hpp"


def function_end(text: str, start: int) -> int:
    brace = text.find('{', start)
    if brace < 0:
        raise RuntimeError('function opening brace missing')
    depth = 0
    i = brace
    in_string = False
    in_char = False
    line_comment = False
    block_comment = False
    escape = False
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if line_comment:
            if c == '\n': line_comment = False
            i += 1; continue
        if block_comment:
            if c == '*' and n == '/': block_comment = False; i += 2; continue
            i += 1; continue
        if in_string:
            if escape: escape = False
            elif c == '\\': escape = True
            elif c == '"': in_string = False
            i += 1; continue
        if in_char:
            if escape: escape = False
            elif c == '\\': escape = True
            elif c == "'": in_char = False
            i += 1; continue
        if c == '/' and n == '/': line_comment = True; i += 2; continue
        if c == '/' and n == '*': block_comment = True; i += 2; continue
        if c == '"': in_string = True; i += 1; continue
        if c == "'": in_char = True; i += 1; continue
        if c == '{': depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0: return i + 1
        i += 1
    raise RuntimeError('function closing brace missing')

# Header state.
t = hpp.read_text(encoding='utf-8')
if 'm_exact_last_compact' not in t:
    marker = '        bool m_sidebar_compact{};\n'
    if marker in t:
        t = t.replace(marker, marker + '        bool m_exact_last_compact{};\n', 1)
hpp.write_text(t, encoding='utf-8')

t = menu.read_text(encoding='utf-8')

# Keep only the newly generated draw_top_bar function. The original body may
# follow it because the first generator replaces the signature rather than the
# whole function; remove that tail with brace-aware boundaries.
top_marker = '// MCB exact uploaded topbar.'
mp = t.find(top_marker)
if mp < 0:
    raise SystemExit('[exact-runtime2-v5] exact topbar marker missing')
top_start = t.rfind('\tvoid menu::draw_top_bar', 0, mp)
if top_start < 0:
    raise SystemExit('[exact-runtime2-v5] exact topbar function start missing')
top_end = function_end(t, top_start)
ns_end = t.rfind('\n} // namespace rendering')
if ns_end > top_end:
    t = t[:top_end] + t[ns_end:]

# Reuse the already reviewed exact runtime C++ body from runtime-v1, but place
# it using brace-aware function boundaries rather than a fragile source regex.
old_patch = (patch_dir / 'apply_exact_panel_runtime_v5.py').read_text(encoding='utf-8')
tree = ast.parse(old_patch)
template = None
for node in tree.body:
    if isinstance(node, ast.Assign):
        for target in node.targets:
            if isinstance(target, ast.Name) and target.id == 'replacement':
                template = ast.literal_eval(node.value)
                break
    if template is not None: break
if template is None:
    raise SystemExit('[exact-runtime2-v5] runtime template missing')
expanded = re.sub('X', template, 'X', count=1)
needle = '\n\n\tvoid menu::shutdown'
if needle in expanded:
    expanded = expanded.split(needle, 1)[0]

draw_start = t.find('\tvoid menu::draw( )')
if draw_start < 0:
    raise SystemExit('[exact-runtime2-v5] menu::draw start missing')
draw_end = function_end(t, draw_start)
t = t[:draw_start] + expanded + t[draw_end:]

menu.write_text(t, encoding='utf-8')
print('[exact-runtime2-v5] brace-aware exact shell runtime applied')
