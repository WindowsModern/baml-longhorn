#!/usr/bin/env python3
"""
Decompile the whole primary corpus to XAML and report how many succeed.

Drives the C# CLI (`baml xaml`) over every sample so the result reflects the
shipped implementation rather than a parallel Python one.
"""
import os
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SAMPLES = os.path.join(ROOT, "samples", "xaml")
EXE = os.path.join(ROOT, "src", "BamlLonghorn.Cli", "bin", "Debug", "baml.exe")


def main():
    if not os.path.exists(EXE):
        print("missing CLI: %s" % EXE)
        return 1

    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))

    ok = 0
    incomplete = 0
    failed = 0
    total_bytes = 0
    decompiled_bytes = 0
    failures = []

    for path in files:
        rel = os.path.relpath(path, SAMPLES)
        try:
            out = subprocess.run(
                [EXE, "-q", "xaml", path],
                capture_output=True, timeout=60)
        except Exception as exc:
            failures.append((rel, "exception: %s" % exc))
            failed += 1
            continue

        text = out.stdout.decode("utf-8", "replace")
        total_bytes += 1
        if out.returncode != 0 or "<!-- no element tree" in text:
            failures.append((rel, "no tree (rc=%d)" % out.returncode))
            failed += 1
            continue
        if "<!-- INCOMPLETE:" in text:
            incomplete += 1
            failures.append((rel, "partial"))
        else:
            ok += 1

    print("corpus XAML decompile")
    print("  files            : %d" % len(files))
    print("  complete         : %d" % ok)
    print("  partial          : %d" % incomplete)
    print("  no tree / error  : %d" % failed)
    print()
    if failures:
        print("first 20 non-complete:")
        for rel, why in failures[:20]:
            print("   %-46s %s" % (rel, why))
    return 0


if __name__ == "__main__":
    sys.exit(main())
