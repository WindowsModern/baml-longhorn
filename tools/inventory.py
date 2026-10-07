#!/usr/bin/env python3
"""
Inventories every element type and attribute name that appears in the sample corpora.

The point is to decide the modern-XAML mapping from evidence rather than from memory. A
conversion table invented from recollection would be wrong about which Longhorn components
actually occur, and about how often; this produces the counts to prioritise by.

Usage:
    python tools/inventory.py [corpus-dir ...]
"""
import os
import re
import sys
from collections import Counter

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TAG = re.compile(r"<\s*([A-Za-z_][\w.:\-]*)")
ATTR = re.compile(r"([A-Za-z_][\w.:\-]*)\s*=\s*\"")
CLOSE = re.compile(r"</\s*([A-Za-z_][\w.:\-]*)")


def local(tag):
    """Strips a namespace prefix and, for CLR-style names, the namespace path."""
    if tag.startswith("{"):
        tag = tag.split("}", 1)[1]
    if ":" in tag:
        tag = tag.split(":", 1)[1]
    return tag


def main(argv):
    corpora = argv or ["samples/xaml", "samples/wcp4093"]
    elems = Counter()
    attrs = Counter()
    prefixes = Counter()
    files = 0

    for rel in corpora:
        base = rel if os.path.isabs(rel) else os.path.join(ROOT, rel)
        if not os.path.isdir(base):
            print("  (missing) " + rel)
            continue
        for dirpath, _dirs, names in os.walk(base):
            for name in names:
                if not name.lower().endswith(".baml"):
                    continue
                files += 1
                baml = os.path.join(dirpath, name)
                xaml = baml[:-5] + ".xaml"
                # decode with the CLI when there is no source beside it
                if os.path.exists(xaml):
                    text = open(xaml, encoding="utf-8", errors="replace").read()
                else:
                    text = decode(baml)
                if not text:
                    continue
                for m in TAG.finditer(text):
                    tag = m.group(1)
                    if tag.startswith("!"):
                        continue
                    if ":" in tag:
                        prefixes[tag.split(":", 1)[0]] += 1
                    elems[local(tag)] += 1
                for m in CLOSE.finditer(text):
                    tag = m.group(1)
                    if ":" in tag:
                        prefixes[tag.split(":", 1)[0]] += 1
                    elems[local(tag)] += 1
                for m in ATTR.finditer(text):
                    attrs[local(m.group(1))] += 1

    print("  files scanned: %d" % files)
    print()
    print("  element types: %d distinct" % len(elems))
    for name, n in elems.most_common(60):
        print("    %6d  %s" % (n, name))
    if len(elems) > 60:
        print("    ... (+%d more)" % (len(elems) - 60))
    print()
    print("  attribute names: %d distinct" % len(attrs))
    for name, n in attrs.most_common(40):
        print("    %6d  %s" % (n, name))
    print()
    print("  xmlns prefixes seen: %s" % (", ".join(sorted(prefixes)) or "(none)"))
    return 0


def decode(baml):
    """Falls back to the CLI when no source markup sits beside a fixture."""
    import subprocess
    for config in ("Release", "Debug"):
        for exe in ("baml.exe", "baml"):
            cli = os.path.join(ROOT, "src", "BamlLonghorn.Cli", "bin", config, exe)
            if os.path.exists(cli):
                r = subprocess.run([cli, "--no-header", "-q", "xaml", baml],
                                   capture_output=True, timeout=120)
                return r.stdout.decode("utf-8", "replace") if r.returncode == 0 else None
    return None


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
