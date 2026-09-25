"""Verify and repackage an immutable CI artifact; never load the game DLL."""
from __future__ import annotations
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import struct
import urllib.parse
import urllib.request
import uuid
import zipfile

REPO = 'Rex11929282/audio-share-updates'
RUN = 36153692686
COMMIT = '2528f1c830c02e0bc5851382d7c0ef525bb66d1c'
ARTIFACT = 10872144445
ARCHIVE_SHA = '81bc8021c94d85518de5ee5d81895ed6b6e462fffe032e914b93cddaad16fdb3'
MAIN = 'MCB-CS2-update-25515854-dev.dll'
MAX_BYTES = 256 * 1024 * 1024


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        require(urllib.parse.urlparse(newurl).scheme == 'https', 'non-HTTPS redirect')
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if redirected and urllib.parse.urlparse(newurl).hostname != 'api.github.com':
            redirected.remove_header('Authorization')
        return redirected


def get_api(path: str) -> bytes:
    require(path.startswith(f'/repos/{REPO}/'), 'unexpected API route')
    request = urllib.request.Request('https://api.github.com' + path, headers={
        'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
        'Accept': 'application/vnd.github+json', 'User-Agent': 'MCB-artifact-verifier',
    })
    with urllib.request.build_opener(SafeRedirect()).open(request, timeout=90) as response:
        data = response.read(MAX_BYTES + 1)
    require(len(data) <= MAX_BYTES, 'download exceeds limit')
    return data


def clean_name(name: str) -> str:
    require('\\' not in name and ':' not in name and '\x00' not in name, 'unsafe ZIP path')
    path = PurePosixPath(name)
    require(not path.is_absolute() and '..' not in path.parts, 'unsafe ZIP traversal')
    return path.as_posix()


def archive_files(data: bytes) -> dict[str, bytes]:
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        infos = z.infolist()
        require(len(infos) < 10000 and sum(i.file_size for i in infos) < MAX_BYTES, 'ZIP limits')
        names = [clean_name(i.filename) for i in infos if not i.is_dir()]
        require(len(names) == len(set(n.lower() for n in names)), 'duplicate ZIP member')
        require(z.testzip() is None, 'ZIP CRC mismatch')
        return {clean_name(i.filename): z.read(i) for i in infos if not i.is_dir()}


def pe_info(data: bytes) -> dict:
    require(len(data) >= 256 and data[:2] == b'MZ', 'not MZ')
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    require(pe + 24 <= len(data) and data[pe:pe+4] == b'PE\0\0', 'not PE')
    machine, sections, timestamp, _, _, optional_size, flags = struct.unpack_from('<HHIIIHH', data, pe+4)
    optional = pe + 24
    require(machine == 0x8664 and flags & 0x2000, 'not AMD64 DLL')
    require(optional_size >= 112 and optional + optional_size + sections*40 <= len(data), 'PE bounds')
    require(struct.unpack_from('<H', data, optional)[0] == 0x20b, 'not PE32+')
    section_table = optional + optional_size
    def offset(rva: int, length: int) -> int:
        for i in range(sections):
            address = section_table + i*40
            virtual_size, virtual_address, raw_size, raw = struct.unpack_from('<IIII', data, address+8)
            if virtual_address <= rva and rva-virtual_address+length <= raw_size:
                result = raw + rva-virtual_address
                require(result+length <= len(data), 'RVA file bounds')
                return result
        raise ValueError('RVA is not backed by file data')
    directory_count = min(struct.unpack_from('<I', data, optional+108)[0], (optional_size-112)//8)
    result = {'machine': 'AMD64', 'format': 'PE32+', 'dll': True, 'bytes': len(data),
              'sha256': sha(data), 'timestamp': timestamp, 'executed': False, 'imports': []}
    if directory_count > 1:
        imports_rva, imports_size = struct.unpack_from('<II', data, optional+120)
        if imports_rva and imports_size:
            for i in range(min(imports_size//20, 2048)):
                descriptor = offset(imports_rva+i*20, 20)
                fields = struct.unpack_from('<IIIII', data, descriptor)
                if fields == (0,0,0,0,0): break
                name_offset = offset(fields[3], 1)
                end = data.find(b'\0', name_offset, min(len(data), name_offset+512))
                require(end >= 0, 'unterminated DLL import name')
                result['imports'].append(data[name_offset:end].decode('ascii'))
    if directory_count > 6:
        debug_rva, debug_size = struct.unpack_from('<II', data, optional+112+6*8)
        if debug_rva and debug_size:
            require(debug_size < 1024*1024, 'debug size limit')
            for i in range(debug_size//28):
                fields = struct.unpack_from('<IIHHIIII', data, offset(debug_rva+i*28, 28))
                kind, size, raw = fields[4], fields[5], fields[7]
                if kind == 2 and size >= 24 and raw+size <= len(data) and data[raw:raw+4] == b'RSDS':
                    result['codeview'] = {'guid': str(uuid.UUID(bytes_le=data[raw+4:raw+20])),
                        'age': struct.unpack_from('<I', data, raw+20)[0]}
                    break
    return result


def pdb_identity(data: bytes) -> dict:
    require(data.startswith(b'Microsoft C/C++ MSF 7.00\r\n\x1aDS\0\0\0') and len(data) >= 56, 'not MSF7 PDB')
    block_size, _, blocks, directory_size, _, map_block = struct.unpack_from('<IIIIII', data, 32)
    require(block_size in (512,1024,2048,4096,8192,16384,32768,65536), 'PDB block size')
    require(blocks*block_size <= len(data) and directory_size < 16*1024*1024, 'PDB size')
    def block(index: int) -> bytes:
        require(index < blocks, 'PDB block index')
        return data[index*block_size:(index+1)*block_size]
    directory_blocks = (directory_size+block_size-1)//block_size
    require(directory_blocks*4 <= block_size, 'multi-level PDB directory unsupported')
    ids = struct.unpack_from('<'+'I'*directory_blocks, block(map_block))
    directory = b''.join(block(i) for i in ids)[:directory_size]
    require(len(directory) >= 4, 'PDB directory missing')
    streams = struct.unpack_from('<I', directory)[0]
    require(2 <= streams <= 100000 and 4+streams*4 <= len(directory), 'PDB streams bounds')
    sizes = struct.unpack_from('<'+'I'*streams, directory, 4)
    cursor = 4+streams*4
    for index, size in enumerate(sizes):
        count = 0 if size == 0xffffffff else (size+block_size-1)//block_size
        require(cursor+count*4 <= len(directory), 'PDB stream blocks bounds')
        ids = struct.unpack_from('<'+'I'*count, directory, cursor)
        cursor += count*4
        if index == 1:
            info = b''.join(block(i) for i in ids)[:size]
            require(len(info) >= 28, 'PDB info stream short')
            return {'guid': str(uuid.UUID(bytes_le=info[12:28])), 'age': struct.unpack_from('<I', info, 8)[0]}
    raise ValueError('PDB info stream absent')


def main() -> None:
    run = json.loads(get_api(f'/repos/{REPO}/actions/runs/{RUN}'))
    require(run['head_sha'] == COMMIT and run['conclusion'] == 'success', 'source run identity/status')
    metadata = json.loads(get_api(f'/repos/{REPO}/actions/artifacts/{ARTIFACT}'))
    require(not metadata['expired'] and metadata['workflow_run']['id'] == RUN, 'artifact run/expiry')
    archive = get_api(f'/repos/{REPO}/actions/artifacts/{ARTIFACT}/zip')
    require(sha(archive) == ARCHIVE_SHA, 'downloaded artifact SHA mismatch')
    files = archive_files(archive)
    old_manifest = json.loads(files['SHA256_MANIFEST.json'])
    for path, record in old_manifest.items():
        name = path.replace('\\', '/')
        require(name in files and len(files[name]) == record['bytes'] and sha(files[name]) == record['sha256'], 'source manifest mismatch: ' + name)
    require('generated_ui_helper_checks=51' in files['UI_MAP_NATIVE_REGRESSION.log'].decode('utf-8-sig'), 'native UI test evidence missing')
    msbuild = files['MSBUILD.log'].decode('utf-8-sig', errors='replace')
    require('Build succeeded.' in msbuild and re.search(r'\b0 Error\(s\)', msbuild), 'successful full build evidence absent')
    main_name = 'candidate-source-rebuild/' + MAIN
    runtime_name = 'candidate-source-rebuild/runtime/lua51.dll'
    pdb_name = main_name[:-4] + '.pdb'
    native = pe_info(files[main_name])
    runtime = pe_info(files[runtime_name])
    identity = pdb_identity(files[pdb_name])
    require(native.get('codeview') == identity, 'DLL/PDB identity mismatch')
    snapshot = archive_files(files['SOURCE_SNAPSHOT.zip'])
    manifests = [value for name, value in snapshot.items() if name.endswith('nix_luajit_manifest.hpp') and name.startswith('native-source/')]
    require(len(manifests) == 1 and runtime['sha256'] in manifests[0].decode('utf-8').lower(), 'runtime hash not pinned into compiled native source')
    out = Path('package')
    out.mkdir()
    def write(name: str, data: bytes | str) -> None:
        dest = out / clean_name(name)
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(data.encode('utf-8') if isinstance(data, str) else data)
    write(MAIN, files[main_name])
    write('runtime/lua51.dll', files[runtime_name])
    write('runtime/LuaJIT-COPYRIGHT.txt', files['candidate-source-rebuild/runtime/LuaJIT-COPYRIGHT.txt'])
    write('symbols/' + MAIN[:-4] + '.pdb', files[pdb_name])
    omitted = []
    font_ext = {'.ttf','.otf','.ttc','.woff','.woff2','.eot','.fon','.fnt'}
    with zipfile.ZipFile(out/'SOURCE_SNAPSHOT.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        for name, data in sorted(snapshot.items()):
            parts = PurePosixPath(name).parts
            # Exclude standalone font files and bundled resource/asset byte arrays.
            if PurePosixPath(name).suffix.lower() in font_ext or any(p.lower() in ('resources','assets','fonts') for p in parts):
                omitted.append(name)
                continue
            if data[:4] in (b'OTTO', b'wOFF', b'wOF2', b'ttcf'):
                omitted.append(name)
                continue
            z.writestr(name, data)
    for name, data in files.items():
        if name.startswith('candidate-source-rebuild/') or name == 'SOURCE_SNAPSHOT.zip':
            continue
        if PurePosixPath(name).suffix.lower() in ('.log','.txt','.json','.jsonl','.ps1','.py','.yml','.cpp','.hpp','.cmd'):
            write('validation/build/' + name, data)
    write('source-assets-omitted.json', json.dumps(omitted, ensure_ascii=False, indent=2))
    receipt = {'build_run': RUN, 'build_commit': COMMIT, 'source_artifact': ARTIFACT,
        'source_artifact_sha256': ARCHIVE_SHA, 'source_zip_crc': 'PASS',
        'source_manifest_entries_verified': len(old_manifest), 'main_dll': native,
        'runtime_dll': runtime, 'pdb_identity': identity, 'dll_pdb_match': True,
        'pdb_sha256': sha(files[pdb_name]), 'binary_bytes_changed_by_packaging': False,
        'in_game': 'NOT_TESTED', 'full_nixware_compatibility': 'NOT_COMPLETED',
        'original_lua_executed_or_modified': False, 'historical_b15_integrated': False,
        'main_build_warnings': 83, 'main_build_errors': 0,
        'ui_win32_helper_assertions': 51, 'python_patch_test_cases': 11}
    write('validation/BINARY_VERIFICATION.json', json.dumps(receipt, ensure_ascii=False, indent=2))
    write('00_先讀我.txt', '''MCB CS2 更新適配候選版｜2026-09-25

這包包含真正重建的 Windows x64 主 DLL、精確版 LuaJIT 與測試紀錄。
它是既有公開來源建置支線的獨立候選，不是 b15 的 MCB_storage.dll 補丁。
遊戲內尚未驗收；上游宣告目標 Build 25515854，但仍稱其更新尚未完整。
你的電腦實際 Build ID 尚未取得。不能把編譯成功當成更新後遊戲已修好。

最少操作
1. 完全結束舊遊戲程序。整包解壓到新的資料夾，保留原包與個人 CFG。
2. 沿用既有載入方式，選這包根目錄的 MCB-CS2-update-25515854-dev.dll。
   不要選 runtime/lua51.dll，不要混入旧 MCB_storage.dll，不要覆蓋舊版本。
   runtime 必須保留在主 DLL 旁邊的子資料夾內。
3. 先只測主選單能否出現、1440×1080 點擊與拖曳。不要立即載入 Lua 或預設。
4. 確認啟動後才到 Nixware 腳本管理頁手動選擇你已審查的原稿。
   需要批准時依頁面確認該檔案內容，勿批准不明脚本。
   本次没有執行、修改或重新創作你的六份功能 Lua。

重要限制
- 沒有附上舊 b15 全包或六份私人原稿：它們不在這個公開雲端建置工作區。
  這不是要求你混搭 DLL；本候選运行不依賴舊 b15 DLL/helper。
- 本版 CFG 儲存支線不同；不要假設舊 b15 二進位 CFG 可直接匯入，保留原檔。
- Nixware API 仍非全面相容；特殊事件、任意腳本和遊戲原生行為未驗收。
- LuaJIT 真實 FFI／cdata／本次自產位元碼已在 Windows 獨立環境測試。
  沒有驗證你那份組名位元碼或第三方 C 模組，未隨包加入 LuaJIT 編譯器模組。
- 原生 Legit/Rage/HVH 預設代碼已編入；不是已驗收的三份通用完整 CFG。
- 射擊記錄代碼已編入；空槍根因未修復、未驗收。
- 保留原稿與既有回復資料。本輪沒有加入防護繞過或自動注入器。

若無法啟動，停止反覆載入，保留新版的 velocity_init.log、崩潰 dmp、
Nixware 頁面提示與你實際選取的 DLL。不要把舊包日誌當成本輪證據。

SOURCE_SNAPSHOT.zip 是本輪來源快照，資源與字型資料不隨附。
建置工具可取得固定上游來源及依賴；來源快照不是離線自帶所有依賴的工程。
主 DLL 的內部版本資源仍沿用該支線的 1.6.2 標記，成品以 SHA-256 識別。
''')
    write('01_實際測試與未完成.md', '''# 本輪結果

成功建置：36153692686，提交 2528f1c830c02e0bc5851382d7c0ef525bb66d1c。
實際使用 MSBuild Ship|x64 /t:Rebuild、clang-cl/lld；83 warnings、0 errors。
主 DLL 與 PDB 並非只改檔名，已從更新後來源全量編譯；交付重排不改它們的位元組。

實際修正：
- 取得固定上游更新 a6f200d09e17b144584cdd80c5429d7189bf2259。
- 舊射擊補丁不再尋找不存在的 m_events map，改用 checked register_listener。
- 修正靜態 events::initialize 內錯用 this 的本輪錯誤。
- 合併中文化補丁與 UI 補丁重複定義的滑鼠更新函式。
- 焦點／擷取取消事件只保留一組，不把取消偽造成成功放開。
- 移除上游新增但未要求的 Discord webhook 測試呼叫與相關編译來源。

真正執行的測試：
- 11 個 Python 補丁解析／冪等／錯誤拒絕測試。不是遊戲功能測試。
- Windows 生成 C++ 事件註冊測試：靜態類別、失敗返回、只分發一次；遊戲服務為替身。
- 51 項生成 C++ 滑鼠／焦點測試：真实 HWND、ClientToScreen、ScreenToClient；
  viewport／輸入容器為替身。覆盖 1440×1080、1920×1080、1280×720。
- 真正 LuaJIT 2.1.1736781742：FFI、cdata、pointer、callback、本輪產生的位元碼。
- 獨立 Windows EXE 載入真正 lua51.dll。不是在 CS2 裡載入主 DLL。
- Render bootstrap 原生函式為替身；CFG／shot 的整合檢查是源码標記，不是功能驗收。
- 主 DLL 全量編譯與連結、成品 SHA、PE 架構、DLL/PDB GUID及Age比對、ZIP CRC。

失敗保留：
36151463502：本輪新增碼在靜態函式使用 this，主編譯失敗。
36152470472：舊補丁重複宣告 mouse helper 與 WM_KILLFOCUS，主編譯失敗。
36153692686：上述問題修正後重新全量建置成功。沒有將失敗改寫為通過。

未完成：新版遊戲啟動／遊戲 ABI、GPU 全 UI、完整 Nixware API、六原稿實機、
CFG逐欄位和回滾的本次端到端測試、空槍根因、一鍵注入、b15 二進位回移整合。
測試資料與二進位核驗在 validation；不以綠色 CI 推定遊戲可用。
''')
    write('Verify-Package.ps1', r'''$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $root 'SHA256_MANIFEST.json') -Raw | ConvertFrom-Json
$count = 0
foreach ($entry in $manifest.PSObject.Properties) {
    $path = Join-Path $root $entry.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing: $($entry.Name)" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.Value.sha256 -or (Get-Item -LiteralPath $path).Length -ne $entry.Value.bytes) {
        throw "Mismatch: $($entry.Name)"
    }
    $count++
}
Write-Host "Verified $count files. No DLL or Lua was executed. This is not an in-game test."
''')
    manifest = {p.relative_to(out).as_posix(): {'bytes':p.stat().st_size,'sha256':sha(p.read_bytes())}
                for p in sorted(out.rglob('*')) if p.is_file()}
    write('SHA256_MANIFEST.json', json.dumps(manifest, ensure_ascii=False, indent=2))
    with zipfile.ZipFile('verified-package-check.zip','w',zipfile.ZIP_DEFLATED) as z:
        for p in sorted(out.rglob('*')):
            if p.is_file(): z.write(p,p.relative_to(out).as_posix())
    checked = archive_files(Path('verified-package-check.zip').read_bytes())
    for name, item in manifest.items():
        require(sha(checked[name]) == item['sha256'], 'repacked file mismatch')
    require(checked[MAIN] == files[main_name] and checked['runtime/lua51.dll'] == files[runtime_name], 'binary changed')
    print(json.dumps({'main':native,'runtime':runtime,'pdb_match':True,
        'package_manifest_entries':len(manifest),'package_zip_crc':'PASS',
        'original_artifact_hash_checked':True,'binary_bytes_unchanged':True,
        'source_resource_files_omitted':len(omitted)}, indent=2))


if __name__ == '__main__':
    main()
