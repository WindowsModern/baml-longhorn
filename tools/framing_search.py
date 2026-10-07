#!/usr/bin/env python3
"""
Framing hypothesis search for build-4074 BAML.

Ground truth for the search: every ASCII string in the file is preceded, at some
small distance, by its own byte length.  Verified anchors in
samples\\xaml\\shellview\\modulesizer.baml (184 bytes):

    string                                   offset  len
    "PresentationFramework"                      52   21
    "System.Windows.Controls.Primitives.Thumb"   84   39
    "http:////schemas.microsoft.com//2005//xaml//" 136 44

Candidate length encodings found near those anchors:

    at 51: 0x15        = 21 = len            (1-byte len)
    at 50: 0x00 0x15   ...
    at 79: 28 00 00 00 = 40 = len + 1        (4-byte len)
    at 135: 2c         = 44 = len            (1-byte len)

So both widths occur.  The decisive test is a CHAIN: if records are
length-prefixed, then for some consistent header size h and length field width w,
the record boundaries must exactly tile the file with no gap and no overlap.

This tool brute-forces that: for each candidate (record-header interpretation),
it tries to tile the whole file and reports any that succeed.  A hypothesis that
tiles a 184-byte file exactly is almost certainly the real framing.
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


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def is_ascii(b):
    return b >= 0x20 and b < 0x7F


def try_tile(data, start, len_at, len_width, header):
    """
    Attempt to tile data[start:] with records shaped as
        [header bytes][length field (len_width bytes) at offset len_at]
    where the length counts the record's own body in one of several ways.

    Returns a list of (record_start, length_value) or None.
    """
    pos = start
    records = []
    while pos < len(data):
        if pos + header + len_width > len(data):
            return None
        field_off = pos + len_at
        if field_off + len_width > len(data):
            return None
        if len_width == 1:
            value = data[field_off]
        elif len_width == 2:
            value = struct.unpack_from("<H", data, field_off)[0]
        else:
            value = struct.unpack_from("<I", data, field_off)[0]
        if value <= 0:
            return None
        records.append((pos, value))
        pos = field_off + value          # length measured from the length field
        if pos > len(data):
            return None
    return records if pos == len(data) else None


def try_tile_from_start(data, len_at, len_width, header, base):
    """Same, but the length is measured from the record start + base."""
    pos = 0
    records = []
    while pos < len(data):
        if pos + header + len_width > len(data):
            return None
        field_off = pos + len_at
        if len_width == 1:
            value = data[field_off]
        elif len_width == 2:
            value = struct.unpack_from("<H", data, field_off)[0]
        else:
            value = struct.unpack_from("<I", data, field_off)[0]
        if value <= 0:
            return None
        records.append((pos, value))
        pos = pos + base + value
        if pos > len(data):
            return None
    return records if pos == len(data) else None


def main():
    data = read(SAMPLE)
    print("sample: %s (%d bytes)" % (os.path.basename(SAMPLE), len(data)))
    print()

    # ---- Part 1: tiling from various start offsets -------------------------
    print("=== tiling search (length measured from the length field) ===")
    found = 0
    for start in range(0, 60):
        for len_at in (0, 1, 2, 3, 4, 5, 6, 7, 8):
            for len_width in (1, 2, 4):
                for header in (2, 3, 4, 6, 8, 10):
                    if len_at + len_width > header + 8:
                        continue
                    recs = try_tile(data, start, len_at, len_width, header)
                    if recs and len(recs) >= 3:
                        print("  start=%d len_at=%d width=%d header=%d -> %d records"
                              % (start, len_at, len_width, header, len(recs)))
                        for (o, v) in recs[:12]:
                            print("       @%-4d len=%d" % (o, v))
                        found += 1
                        if found > 8:
                            return 0
    if not found:
        print("  (no exact tiling found)")

    # ---- Part 2: just report the local neighbourhood of each anchor --------
    print()
    print("=== bytes preceding each string anchor ===")
    for text in (b"PresentationFramework",
                 b"System.Windows.Controls.Primitives.Thumb",
                 b"http:////"):
        off = data.find(text)
        if off < 0:
            continue
        lo = max(0, off - 14)
        ctx = " ".join("%02x" % b for b in data[lo:off])
        print("  %-44s text@%-4d precedes: %s" % (text.decode(), off, ctx))
    return 0


if __name__ == "__main__":
    sys.exit(main())
