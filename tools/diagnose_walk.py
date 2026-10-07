#!/usr/bin/env python3
"""
Diagnose where the build-4074 record walk diverges on the failing samples.

The size rule `next = sizeFieldStart + size` walks the smallest sample perfectly
(7 records, exactly to EOF). 23 of 103 samples walk cleanly; the rest fail at a
specific offset, usually with a type code slightly above the enum's last value
(36) -- e.g. "type 38 out of range", which smells like an off-by-a-few alignment
rather than a wrong rule.

This tool, for the first failing sample, prints the walk up to the failure and the
bytes around the divergence, and reports which small deltas would land on a valid
record type.
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

TYPE_NAMES = [
    "Unknown", "DocumentStart", "DocumentEnd", "ElementStart", "ElementEnd",
    "Property", "PropertyCustom", "PropertyComplexStart", "PropertyComplexEnd",
    "PropertyArrayStart", "PropertyArrayEnd", "PropertyIListStart",
    "PropertyIListEnd", "PropertyIDictionaryStart", "PropertyIDictionaryEnd",
    "LiteralContent", "Text", "RoutedEvent", "ClrEvent", "XmlnsProperty",
    "XmlAttribute", "ProcessingInstruction", "Comment", "IncludeTag",
    "DefArrayStart", "DefArrayEnd", "DefTag", "DefAttribute", "EndAttributes",
    "PIMapping", "AssemblyInfo", "TypeInfo", "TypeSerializerInfo",
    "AttributeInfo", "ResourceInfo", "PropertyResourceReference", "LastRecordType",
]

VARIABLE = {1, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 23, 27,
            29, 30, 31, 32, 33, 34, 35}


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def walk(data, limit=None):
    pos = 0
    out = []
    while pos + 2 <= len(data):
        t = struct.unpack_from("<h", data, pos)[0]
        out.append((pos, t))
        if t < 0 or t > 36:
            return out, pos, "type %d out of range" % t
        if t in VARIABLE:
            if pos + 6 > len(data):
                return out, pos, "truncated"
            size = struct.unpack_from("<i", data, pos + 2)[0]
            nxt = pos + 2 + size
            if size < 0 or nxt <= pos + 6 or nxt > len(data):
                return out, pos, "size %d -> %d" % (size, nxt)
            pos = nxt
        else:
            pos += 2 + (2 if t in (3, 24) else 0)
        if limit and len(out) > limit:
            return out, pos, "limit"
    return out, pos, None


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))

    # first failing sample
    for p in files:
        data = read(p)
        recs, end, err = walk(data)
        if err is not None or end != len(data):
            print("first failing sample: %s (%d bytes)"
                  % (os.path.relpath(p, SAMPLES), len(data)))
            print()
            print("walk:")
            for (o, t) in recs[-8:]:
                nm = TYPE_NAMES[t] if 0 <= t <= 36 else "??"
                print("   @%-6d type=%-4d %s" % (o, t, nm))
            print("   STOP @%d  (%s)" % (end, err))
            print()
            print("bytes around the stop point:")
            lo = max(0, end - 24)
            for i in range(lo, min(len(data), end + 24)):
                mark = "  <== stop" if i == end else ""
                print("   %5d: %02x %3d%s" % (i, data[i], data[i], mark))
            print()
            print("which small offsets from the stop point yield a valid type?")
            for delta in range(-8, 9):
                q = end + delta
                if 0 <= q <= len(data) - 2:
                    tt = struct.unpack_from("<h", data, q)[0]
                    if 0 <= tt <= 36:
                        print("   delta %+3d -> @%d type=%d %s"
                              % (delta, q, tt, TYPE_NAMES[tt]))
            break
    return 0


if __name__ == "__main__":
    sys.exit(main())
