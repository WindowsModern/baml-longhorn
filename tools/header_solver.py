#!/usr/bin/env python3
"""
Record-header solver for build-4074 BAML.

Anchor facts (independent of any header guess):
  * the concatenation of every entry's text and its length byte tiles the
    region between the document header and the element stream, in all 103
    samples: each entry contributes
        <len byte == len(text)> <text bytes> <NUL?>
  * the document header's text always ends at offset 28

What is unknown is the record header: how many bytes precede each length byte,
and which of them is a tag vs a 32-bit id.

Constraint used to solve it: table entries are indexed, so the 32-bit id of
consecutive entries should form a clean sequence (0,1,2,... or at least a
monotone step).  That is far more discriminating than "looks like a tag".

This tool enumerates (header_size, id_offset, id_width) and scores the resulting
id sequence across many samples.
"""
import os
import struct
import sys
from collections import Counter

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.abspath(os.path.join(HERE, "..", "samples", "xaml"))


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def header_text_end(data):
    i = 10
    while i + 1 < len(data):
        if (data[i] | (data[i + 1] << 8)) == 0:
            return i + 2
        i += 2
    return -1


def entry_positions(data):
    """
    Locate each entry: a 1-byte length L immediately followed by L printable
    ASCII bytes.  Returns list of (len_off, text_off, text_end_incl_nul).
    """
    out = []
    pos = header_text_end(data)
    if pos < 0:
        return out
    while pos < len(data):
        found = None
        for scan in range(pos, min(len(data), pos + 64)):
            L = data[scan]
            tstart = scan + 1
            tend = tstart + L
            if L < 3 or tend > len(data):
                continue
            chunk = data[tstart:tend]
            if all(0x20 <= b < 0x7F for b in chunk):
                found = (scan, tstart, tend)
                break
        if found is None:
            break
        scan, tstart, tend = found
        end = tend + (1 if tend < len(data) and data[tend] == 0 else 0)
        out.append((scan, tstart, tend, end))
        pos = end
    return out


def score_scheme(files, header_size, id_off, id_width):
    """
    header_size = bytes between the previous entry's end and the length byte.
    id sits at length_off - header_size + id_off.
    Returns (exact_sequence_files, total_files, sample_ids)
    """
    exact = 0
    total = 0
    sample = None
    for path in files:
        data = read(path)
        pos = entry_positions(data)
        if len(pos) < 3:
            continue
        total += 1
        ids = []
        ok = True
        for (len_off, tstart, tend, end) in pos:
            base = len_off - header_size
            if base < 0 or base + id_off + id_width > len(data):
                ok = False
                break
            ids.append(int.from_bytes(data[base + id_off:base + id_off + id_width],
                                      "little"))
        if not ok:
            continue
        # the id of entry n should be n
        if all(ids[i] == i for i in range(len(ids))):
            exact += 1
        if sample is None:
            sample = ids[:8]
    return exact, total, sample


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    print("samples: %d" % len(files))

    # sanity: how many entries do we locate per file?
    per = [len(entry_positions(read(p))) for p in files]
    print("entries located per file: min=%d max=%d total=%d"
          % (min(per), max(per), sum(per)))
    print()

    print("solving a header where ids form the sequence 0,1,2,...")
    print("%8s %6s %8s  %-14s %s"
          % ("hdrSize", "idOff", "idWidth", "exactFiles", "first ids"))
    best = []
    for header_size in range(2, 25):
        for id_off in range(0, header_size):
            for id_width in (4, 2):
                if id_off + id_width > header_size:
                    continue
                exact, total, sample = score_scheme(files, header_size,
                                                    id_off, id_width)
                if exact > 0:
                    best.append((exact, header_size, id_off, id_width, sample))
                    print("%8d %6d %8d  %-14s %s"
                          % (header_size, id_off, id_width,
                             "%d/%d" % (exact, total), sample))
    if not best:
        print("  (no header size yields a 0,1,2,... id sequence)")
    else:
        best.sort(reverse=True)
        print()
        print("best: hdrSize=%d idOff=%d idWidth=%d exact=%d/%d"
              % (best[0][1], best[0][2], best[0][3], best[0][0], len(files)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
