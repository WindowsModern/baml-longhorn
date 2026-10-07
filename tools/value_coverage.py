#!/usr/bin/env python3
"""
Measure how many attribute values the XAML decompiler actually decoded.

This supersedes two earlier ad-hoc counters that were both wrong:

  * match counter 1 treated any space-containing value as a hex fallback, so it
    counted correctly-decoded strings ("Segoe UI", "M 0 0 L 802 0 ...") as
    failures and reported 409
  * match counter 2 additionally treated any all-hex-digit DECIMAL ("17", "16",
    "12") as a hex fallback, inflating the number again

A genuine fallback is a space-separated run of two or more tokens where EVERY
token is exactly two hex digits -- i.e. the raw-bytes rendering.
"""
import os
import re
import subprocess
import sys
import collections

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SAMPLES = os.path.join(ROOT, "samples", "xaml")
EXE = os.path.join(ROOT, "src", "BamlLonghorn.Cli", "bin", "Debug", "baml.exe")

ATTR = re.compile(r'([\w:]+)="([^"]*)"')
RAWTWO = re.compile(r'[0-9a-f]{2}')


def is_raw_bytes(value):
    """
    True only for the 'xx xx xx' raw-bytes rendering.

    This test is necessarily heuristic, and it has one known false positive: a
    value that the stream genuinely stores as a TEXT string consisting of hex-digit
    pairs. `Center` on a RotateTransform is exactly that -- the BAML really does
    contain the characters "23 17", which is a Point written by the XAML author and
    parsed at load time by PointConverter. No byte-level test can separate that from
    a genuine fallback, so a small non-zero count here is expected and should be
    inspected by hand rather than assumed to be a decoding gap.
    """
    tokens = value.split()
    if len(tokens) < 2:
        return False
    return all(RAWTWO.fullmatch(t) for t in tokens)


def main():
    if not os.path.exists(EXE):
        print("missing CLI: %s" % EXE)
        return 1

    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=os.path.getsize)

    total = 0
    raw = 0
    by_attr = collections.Counter()
    patterns = collections.Counter()
    incomplete = 0

    for path in files:
        out = subprocess.run([EXE, "-q", "xaml", path], capture_output=True, timeout=60)
        text = out.stdout.decode("utf-8", "replace")
        if "<!-- INCOMPLETE:" in text:
            incomplete += 1
        for m in ATTR.finditer(text):
            total += 1
            if is_raw_bytes(m.group(2)):
                raw += 1
                by_attr[m.group(1)] += 1
                patterns[m.group(2)] += 1

    decoded = total - raw
    print("corpus value decoding")
    print("  files                : %d  (incomplete: %d)" % (len(files), incomplete))
    print("  attributes total     : %d" % total)
    print("  VALUE DECODED        : %d  (%.2f%%)"
          % (decoded, 100.0 * decoded / max(total, 1)))
    print("  look like raw bytes  : %d  (%.2f%%)"
          % (raw, 100.0 * raw / max(total, 1)))
    if raw:
        print()
        print("  NOTE: a small count here is expected. The BAML for RotateTransform's")
        print("  Center is literally the TEXT \"23 17\", so it is indistinguishable from")
        print("  a raw-byte fallback by inspection. Verify by opening the Records tab:")
        print("  a genuine gap shows as PropertyCustom/rawValue, a text value as")
        print("  Property/value.")
        print()
        print("  candidates by attribute:")
        for a, c in by_attr.most_common(10):
            print("     %-24s %d" % (a, c))
        print("  candidates by pattern:")
        for v, c in patterns.most_common(10):
            print("     %-40s x%d" % (v, c))
    return 0


if __name__ == "__main__":
    sys.exit(main())
