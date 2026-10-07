#!/usr/bin/env python3
"""
Survey .resx files for entries that might be compiled XAML.

`<data name="x.baml" type="...">` entries carry a resource whose *name* says what it is.
Scanning every manifest in a build tree answers, before any extraction work, whether a
build ships embedded BAML at all -- which is how the 4074 and 4093 WCPClient corpora were
found.

Usage:
    survey_resx.py <root> [<root> ...]
"""
import os
import sys
import xml.etree.ElementTree as ET

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

INTERESTING = (".baml", ".xaml", ".g.resx")


def survey(path):
    """Returns a list of (name, type, payload_bytes_or_None, is_external)."""
    found = []
    try:
        tree = ET.parse(path)
    except Exception as exc:
        return None, str(exc)

    root = tree.getroot()
    for data in root.findall("data"):
        name = data.get("name") or ""
        mime = data.get("type") or data.get("mimetype") or ""
        lower = name.lower()
        if "(none)" in lower:
            # the resx placeholder entry the designer writes; never a real resource
            continue

        value_el = data.find("value")
        file_el = data.find("file")
        payload = None
        external = file_el is not None and value_el is None
        if value_el is not None and value_el.text:
            import base64
            try:
                payload = base64.b64decode(value_el.text.strip())
            except Exception:
                payload = None

        if lower.endswith(".baml") or lower.endswith(".xaml") or ".baml" in lower:
            found.append((name, mime, payload, external))
    return found, None


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1

    total = 0
    for root_dir in argv[1:]:
        for dirpath, _dirnames, filenames in os.walk(root_dir):
            for fn in sorted(filenames):
                if not fn.lower().endswith(".resx"):
                    continue
                path = os.path.join(dirpath, fn)
                found, err = survey(path)
                rel = os.path.relpath(path, root_dir)
                if err:
                    print("  ! %-70s %s" % (rel, err))
                    continue
                if not found:
                    continue
                print("%s" % rel)
                for name, mime, payload, external in found:
                    size = "external" if external else (
                        "%d B" % len(payload) if payload else "unreadable")
                    print("    %-56s %-10s %s" % (name, size, mime[:40]))
                    total += 1
    print()
    print("total embedded-entry candidates: %d" % total)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
