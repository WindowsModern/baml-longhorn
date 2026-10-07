#!/usr/bin/env python3
"""
Resolve the exact record-size base for build-4074 BAML.

DocumentStart already decodes correctly from offset 0:
    type=1, sizeField=41, featureId="PreAlpha", loadAsync=0, maxAsyncRecords=-1

What is unresolved is where the NEXT record begins, i.e. exactly which bytes the
4-byte size field counts. The candidates:

    (a) size counts payload only, next = sizeFieldEnd + size
    (b) size counts sizeField + payload, next = sizeFieldStart + size
    (c) size counts type + sizeField + payload, next = recordStart + size
    (d) as (a) but with 2-byte or 8-byte size field
    ... plus a constant delta / alignment term.

Instead of reasoning about it, this tool brute-forces (fieldWidth, sizeOffset
within header, sizeIncludesOwnField, sizeIncludesType, padTo) and keeps any
combination that walks ALL samples from offset 0 to exactly EOF.

The known record type codes are 0..36, so a mis-stepped walk shows up instantly as
an out-of-range type.
"""
import os
import struct
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLES = os.path.abspath(os.path.join(HERE, "..", "samples", "xaml"))

VARIABLE = {1, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
            15, 16, 17, 18, 19, 23, 27,
            29, 30, 31, 32, 33, 34, 35}
MAXTYPE = 36


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def dword_pad(n):
    return (4 - (n % 4)) % 4


def read7bit(b, pos):
    v = 0
    s = 0
    while True:
        c = b[pos]
        pos += 1
        v |= (c & 0x7F) << s
        if not (c & 0x80):
            return v, pos
        s += 7


def read_string(b, pos):
    n, pos = read7bit(b, pos)
    return b[pos:pos + n], pos + n


def fmt_version_len(b, pos):
    """Bytes consumed by FormatVersion at pos, or None."""
    if pos + 4 > len(b):
        return None
    n = struct.unpack_from("<i", b, pos)[0]
    if n < 0 or n % 2 or pos + 4 + n > len(b):
        return None
    return 4 + n + dword_pad(n) + 12


def payload_len(b, t, body):
    """Bytes consumed by LoadRecordData for the given type at `body`."""
    try:
        if t == 1:                                   # DocumentStart
            k = fmt_version_len(b, body)
            if k is None:
                return None
            q = body + k
            if q + 5 > len(b):
                return None
            return k + 1 + 4                          # +LoadAsync +MaxAsyncRecords
        if t in (5, 6, 15, 16, 17, 18, 19, 23, 27, 35):   # string value
            _, q = read_string(b, body)
            return q - body
        if t == 29:                                  # PIMapping
            _, q = read_string(b, body)
            _, q = read_string(b, q)
            return q - body + 2
        if t == 30:                                  # AssemblyInfo
            _, q = read_string(b, body + 2)
            return q - body
        if t in (31, 32):                            # TypeInfo
            _, q = read_string(b, body + 4)
            return q - body
        if t == 33:                                  # AttributeInfo
            _, q = read_string(b, body + 4)
            return q - body
        if t == 34:                                  # ResourceInfo
            _, q = read_string(b, body)
            return q - body
        if t in (7, 8, 9, 10, 11, 12, 13, 14):
            return 2                                 # attributeId
        return 0
    except (ValueError, IndexError, struct.error):
        return None


def walk(data, kind, field_width, size_off, count_own, count_type, pad_to):
    """Walk records; return list of (off, type, size) or None."""
    pos = 0
    out = []
    while pos < len(data):
        if len(out) > 4096:
            return None
        if pos + 2 + field_width > len(data):
            return None
        t = struct.unpack_from("<h", data, pos)[0]
        if t < 0 or t > MAXTYPE:
            return None

        if t in VARIABLE:
            if field_width == 4:
                size = struct.unpack_from("<i", data, pos + size_off)[0]
            elif field_width == 2:
                size = struct.unpack_from("<H", data, pos + size_off)[0]
            else:
                size = data[pos + size_off]
            if size < 0:
                return None
            body = pos + size_off + field_width
            pl = payload_len(data, t, body)
            if pl is None:
                return None
            out.append((pos, t, size, pl))
            # candidate next-position rules
            nxt = body + size
            if count_own:
                nxt += field_width
            if count_type:
                nxt += 2
            if pad_to and nxt % pad_to:
                nxt += pad_to - (nxt % pad_to)
            if nxt <= pos:
                return None
            pos = nxt
        else:
            body = pos + 2
            pl = 2 if t in (3, 24) else 0
            out.append((pos, t, 2 + pl, pl))
            pos = body + pl
    return out if pos == len(data) and len(out) >= 3 else None


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))
    print("samples: %d" % len(files))

    tries = 0
    hits = []
    for kind in ("size",):
        for field_width in (1, 2, 4):
            for size_off in range(2, 14):
                for count_own in (False, True):
                    for count_type in (False, True):
                        for pad_to in (0, 2, 4):
                            tries += 1
                            ok = 0
                            for p in files[:12]:
                                data = read(p)
                                if walk(data, kind, field_width, size_off,
                                        count_own, count_type, pad_to):
                                    ok += 1
                            if ok >= 6:
                                hits.append((ok, field_width, size_off,
                                             count_own, count_type, pad_to))
    print("combinations tried: %d" % tries)
    print()
    print("combinations that walked >=6 of the first 12 samples cleanly:")
    if not hits:
        print("   none")
    for (ok, fw, so, co, ct, pd) in sorted(hits, reverse=True)[:15]:
        print("   ok=%2d/12  sizeWidth=%d sizeOff=%d countOwnField=%s countType=%s pad=%s"
              % (ok, fw, so, co, ct, pd))

    # Also report, for the smallest sample, where the 2nd record plausibly starts
    data = read(files[0])
    print()
    print("smallest sample: plausible next-record starts after DocumentStart")
    for p in range(40, 56):
        t = struct.unpack_from("<h", data, p)[0]
        if 0 <= t <= MAXTYPE:
            print("   @%d type=%d ok" % (p, t))
    return 0


if __name__ == "__main__":
    sys.exit(main())
