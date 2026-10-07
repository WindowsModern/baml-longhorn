#!/usr/bin/env python3
"""
Find the indexed-id field in build-4074 BAML interstitials.

Entry text positions are known exactly.  Between the end of one entry and the
length byte of the next sits a small variable-size interstitial.  If the format
keeps an indexed table there, some integer field in that interstitial should
count 0,1,2,3,... across the whole file, or at least be constant/structured.

Rather than assume a fixed header size, this tool scans EVERY 1/2/4-byte field
inside each interstitial and reports which (relative position, width)
combination produces the most consistent sequence across all samples.
"""
import os
import sys
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.abspath(os.path.join(HERE, "..", "samples", "xaml"))


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def entries_with_gaps(data):
    """Returns (header_end, [(gap_start, len_off, text_start, text_bytes, entry_end)])."""
    # header
    i = 10
    while i + 1 < len(data) and (data[i] | (data[i + 1] << 8)) != 0:
        i += 2
    hdr_end = i + 2

    out = []
    pos = hdr_end
    while pos < len(data):
        found = None
        for scan in range(pos, min(len(data), pos + 80)):
            L = data[scan]
            tstart = scan + 1
            tend = tstart + L
            if L < 3 or tend > len(data):
                continue
            if all(0x20 <= b < 0x7F for b in data[tstart:tend]):
                found = (scan, tstart, tend)
                break
        if found is None:
            break
        scan, tstart, tend = found
        end = tend + (1 if tend < len(data) and data[tend] == 0 else 0)
        out.append((pos, scan, tstart, data[tstart:tend], end))
        pos = end
    return hdr_end, out


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))

    # gap length distribution
    gapdist = defaultdict(int)
    for p in files:
        data = read(p)
        hdr_end, ents = entries_with_gaps(data)
        for (gap_start, len_off, tstart, text, end) in ents:
            gapdist[len_off - gap_start] += 1
    print("gap length (from previous entry end to this entry's length byte) distribution:")
    for k in sorted(gapdist):
        print("   %2d bytes : %d occurrences" % (k, gapdist[k]))
    print()

    # For each (relative offset, width) read a value from each gap and see
    # whether the sequence across entries equals 0,1,2,...
    results = []
    for width in (4, 2, 1):
        max_off = 20
        for rel in range(-max_off, 0):     # rel measured from the length byte
            exact_files = 0
            usable_files = 0
            first_vals = None
            for p in files:
                data = read(p)
                hdr_end, ents = entries_with_gaps(data)
                if len(ents) < 4:
                    continue
                vals = []
                ok = True
                for (gap_start, len_off, tstart, text, end) in ents:
                    at = len_off + rel
                    if at < 0 or at + width > len(data):
                        ok = False
                        break
                    vals.append(int.from_bytes(data[at:at + width], "little"))
                if not ok:
                    continue
                usable_files += 1
                if all(vals[i] == i for i in range(len(vals))):
                    exact_files += 1
                if first_vals is None:
                    first_vals = vals[:6]
            if exact_files:
                results.append((exact_files, usable_files, rel, width, first_vals))

    print("fields that read as the sequence 0,1,2,... :")
    if not results:
        print("   none")
    for (ex, us, rel, width, fv) in sorted(results, reverse=True)[:20]:
        print("   rel=%-4d width=%d  exact=%d/%d  first=%s"
              % (rel, width, ex, us, fv))
    return 0


if __name__ == "__main__":
    sys.exit(main())
