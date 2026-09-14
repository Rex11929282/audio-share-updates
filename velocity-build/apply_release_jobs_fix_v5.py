from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
hpp = root / "cs2" / "MCB-CS2" / "project" / "core" / "features" / "changer" / "changer.hpp"
t = hpp.read_text(encoding="utf-8")

if "#include <utilities/threadpool/threadpool.hpp>" not in t:
    t = t.replace("#include <filesystem>\n", "#include <filesystem>\n#include <utilities/threadpool/threadpool.hpp>\n", 1)

if "\t\tvoid shutdown( );" not in t.split("\tclass agents", 1)[0]:
    marker = "\t\tvoid flush_skin_images( );\n"
    if marker not in t:
        raise SystemExit("[release-jobs-fix-v5] flush_skin_images marker missing")
    t = t.replace(marker, marker + "\t\tvoid shutdown( );\n", 1)

if "m_image_jobs" not in t:
    marker = "\t\tstd::mutex m_image_mutex{};\n"
    if marker not in t:
        raise SystemExit("[release-jobs-fix-v5] image mutex marker missing")
    t = t.replace(marker, marker +
        "\t\tstd::mutex m_job_mutex{};\n"
        "\t\tstd::vector<threadpool::job> m_image_jobs{};\n"
        "\t\tstd::atomic_bool m_shutting_down{};\n", 1)

hpp.write_text(t, encoding="utf-8")
print("[release-jobs-fix-v5] tracked skin job declarations applied")
