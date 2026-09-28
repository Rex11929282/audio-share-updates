from pathlib import Path
import base64, hashlib, subprocess, sys, zlib

HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
REPO = ROOT.parents[1]
PROJECT_PREFIX = Path("cs2/MCB-CS2")

parts = [(HERE / f"part{i}.txt").read_text(encoding="ascii").strip() for i in (1,2,3,4)]
payload = "".join(parts)
if len(payload) != 30938:
    raise RuntimeError(f"unexpected payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest() != "1598ac4eb344a3a852e13813673d0fe6bc794ff485478d988ee814d71adb34d3":
    raise RuntimeError("compressed payload hash mismatch")

patch = zlib.decompress(base64.b85decode(payload))
if len(patch) != 105196:
    raise RuntimeError(f"unexpected patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest() != "a9e773a35919bfdd8ec42c1df850be37163d9095ceca5bfec624f9859a7fb13d":
    raise RuntimeError("patch hash mismatch")

patch_file = ROOT / "MCB_UI_ONLY_DELIVERY.patch"
patch_file.write_bytes(patch)

# IMPORTANT: mcb-src is the actual Git repository root. Running git apply from
# cs2/MCB-CS2 silently ignores these project/* paths, while still returning 0.
# Apply from the repository root and explicitly prepend the project directory.
apply_args = [
    "git", "-C", str(REPO), "apply",
    "--directory=" + PROJECT_PREFIX.as_posix(),
    str(patch_file),
]
check_args = apply_args[:4] + ["--check"] + apply_args[4:]
subprocess.run(check_args, check=True)
subprocess.run(apply_args, check=True)

# Fail closed if the patch ever becomes a successful no-op again.
core = (ROOT / "project/core/rendering/impl/menu/menu.core.cpp").read_text(encoding="utf-8-sig")
exact = (ROOT / "project/core/rendering/impl/menu/menu.exact.cpp").read_text(encoding="utf-8-sig")
config = (ROOT / "project/core/rendering/impl/menu/menu.config.cpp").read_text(encoding="utf-8-sig")
xui = (ROOT / "project/external/xdraw/xui/xui.cpp").read_text(encoding="utf-8-sig")

required = {
    "six_main_pages": 'static constexpr const char* labels[]{ "戰鬥", "玩家", "庫存", "其他", "腳本", "設定檔" }' in core,
    "profile_center": "個人中心" in core,
    "unnamed_user": "未命名" in core,
    "scripts_folder_action": "開啟腳本資料夾" in exact,
    "config_folder_action": "開啟設定資料夾" in config,
    "ui_contract_present": (ROOT / "project/external/xdraw/xui/ui_contract.hpp").is_file(),
}
bad = [k for k,v in required.items() if not v]
if bad:
    raise RuntimeError("UI-only patch postcondition failed: " + ", ".join(bad))

for forbidden in (
    'static constexpr const char* labels[]{ "戰鬥", "玩家", "世界", "庫存", "其他", "腳本", "設定檔", "外觀" }',
    'static const char* environments[]{"原生腳本","相容層"}',
    'static constexpr const char* names[]{"低調","進攻","私人對抗"}',
):
    if forbidden in core or forbidden in exact or forbidden in config:
        raise RuntimeError("old UI marker remains after patch: " + forbidden)

status = subprocess.check_output(
    ["git","-C",str(REPO),"status","--short","--",PROJECT_PREFIX.as_posix()],
    text=True, encoding="utf-8", errors="replace"
)
if not status.strip():
    raise RuntimeError("UI-only patch produced no worktree changes")

print("MCB_UI_ONLY_DELIVERY=APPLIED_FOR_REAL")
print("repo_root=" + str(REPO))
print("project_prefix=" + PROJECT_PREFIX.as_posix())
print("patch_sha256=a9e773a35919bfdd8ec42c1df850be37163d9095ceca5bfec624f9859a7fb13d")
print("changed_worktree:")
print(status)
