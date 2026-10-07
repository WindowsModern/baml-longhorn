#!/usr/bin/env python3
"""
Chain search for build-4074 BAML framing.

Model under test: the stream is a sequence of tagged records, each carrying its
own length, so that walking is:

    next = recordStart + headerSize + length

i.e. the length covers the record body (everything after the header).  This is
the shape most compiled-markup formats use.

Search space:
    tagWidth   in {1, 2}
    lenWidth   in {1, 2, 4}
    lenOffset  = position of the length field within the record header
    base       = whether length is measured from the record start or from the
                 end of the length field

A scheme is accepted only if it walks from the first record to exactly the end
of the file in one consistent sequence, with every length in a sane range.
"""
import os
import struct
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.join(HERE, "..", "samples", "xaml")
SAMPLE = os.path.join(SAMPLES, "shellview", "modulesizer.baml")


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def u(data, off, width):
    if off + width > len(data):
        return None
    if width == 1:
        return data[off]
    if width == 2:
        return struct.unpack_from("<H", data, off)[0]
    return struct.unpack_from("<I", data, off)[0]


def walk(data, start, tag_width, len_offset, len_width, from_len_end, header):
    """Walk records; return list of (offset, tag, length) or None."""
    pos = start
    out = []
    guard = 0
    while pos < len(data):
        guard += 1
        if guard > 4096:
            return None
        if pos + header > len(data):
            return None

        tag = u(data, pos, tag_width)
        if tag is None:
            return None

        value = u(data, pos + len_offset, len_width)
        if value is None or value <= 0:
            return None

        out.append((pos, tag, value))

        if from_len_end:
            nxt = pos + len_offset + len_width + value
        else:
            nxt = pos + value
        if nxt <= pos:
            return None
        pos = nxt

    return out if pos == len(data) and len(out) >= 3 else None


def main():
    data = read(SAMPLE)
    print("sample: %s (%d bytes)" % (os.path.basename(SAMPLE), len(data)))
    print()

    hits = []
    for start in range(0, 8):
        for tag_width in (1, 2):
            for len_offset in range(1, 13):
                for len_width in (1, 2, 4):
                    header = max(len_offset + len_width, tag_width)
                    for from_len_end in (True, False):
                        recs = walk(data, start, tag_width, len_offset,
                                    len_width, from_len_end, header)
                        if recs:
                            hits.append((start, tag_width, len_offset, len_width,
                                         from_len_end, header, recs))

    if not hits:
        print("no chaining scheme walks the file")
    for (start, tw, lo, lw, fle, hdr, recs) in hits[:12]:
        print("start=%d tagWidth=%d lenOffset=%d lenWidth=%d fromLenEnd=%s header=%d"
              % (start, tw, lo, lw, fle, hdr))
        for (o, tag, ln) in recs[:14]:
            print("    @%-4d tag=%02x len=%d  body=[%d..%d)"
                  % (o, tag, ln, o + hdr, o + hdr + ln))
        print()

    # ---- independent: where are the strings, and what distance brackets them
    print("=== per-string bracket analysis ===")
    pos = 0
    anchors = []
    while True:
        idx = data.find(b"\x00\x15", 40 + pos) if False else None
        break
    for text in (b"PresentationFramework",
                 b"System.Windows.Controls.Primitives.Thumb",
                 b"http:////"):
        off = data.find(text)
        if off < 0:
            continue
        end = off + len(text)
        anchors.append((off, end, text))
        print("  %-44s [%d..%d] len=%d" % (text.decode()[:44], off, end, len(text)))
    print()
    print("  distances between consecutive anchors:")
    for i in range(1, len(anchors)):
        prev_end = anchors[i - 1][1]
        cur_off = anchors[i][0]
        print("    gap %d -> %d = %d bytes"
              % (prev_end, cur_off, cur_off - prev_end))
    return 0


if __name__ == "__main__":
    sys.exit(main())
