#!/usr/bin/env python3
"""
Field census around known strings in build-4074 BAML.

For every ASCII string of interest, print every 1-, 2-, 4- and 8-byte integer
that can be read from the 20 bytes preceding it, with its exact offset.  The one
whose value equals string length (+1 for a NUL) is the length field; its offset
and width then define the record header.

This removes all manual hex counting, which is where the earlier attempts kept
going wrong.
"""
import os
import struct
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLE = os.path.join(HERE, "..", "samples", "xaml", "shellview", "modulesizer.baml")

INTERESTING = [
    b"PreAlpha",
    b"PresentationFramework",
    b"System.Windows.Controls.Primitives.Thumb",
    b"http:////schemas.microsoft.com//2005//xaml//",
]


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def main():
    data = read(SAMPLE)
    print("sample %s (%d bytes)" % (os.path.basename(SAMPLE), len(data)))
    print()

    for text in INTERESTING:
        off = data.find(text)
        if off < 0:
            continue
        n = len(text)
        print("=" * 76)
        print("string %r" % text.decode()[:60])
        print("  at offset %d, byte length %d" % (off, n))
        print("  exact preceding byte values:")
        lo = max(0, off - 20)
        for i in range(lo, off):
            print("      %3d: %02x %3d" % (i, data[i], data[i]))
        print("  candidate integer fields in the 20 preceding bytes:")
        for i in range(lo, off):
            for width in (1, 2, 4, 8):
                if i + width > off:
                    continue
                v = int.from_bytes(data[i:i + width], "little")
                mark = ""
                if v == n:
                    mark = "   <== EQUALS string length"
                elif v == n + 1:
                    mark = "   <== EQUALS length + 1 (NUL)"
                if mark:
                    print("      @%d width=%d LE=%d%s" % (i, width, v, mark))
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
