#!/usr/bin/env python3
"""
Solve the variable/fixed sizing classification for build-4074 BAML record codes.

The framing rule is proven: next = sizeFieldStart + size = recordStart + 2 + size.
23 of 103 samples walk cleanly with the transcribed sizing table; the rest
diverge. This tool:

  1. walks every sample, recording WHERE and at WHICH record code each walk fails
  2. reports the histogram of failing codes
  3. brute-forces the unknown codes' classification (variable vs fixed) and keeps
     the assignment that maximises the number of samples walking cleanly to EOF

Only a handful of codes are in doubt, so the search is small: it flips one code at
a time, then pairs.
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

TYPE_NAMES = [
    "Unknown", "DocumentStart", "DocumentEnd", "ElementStart", "ElementEnd",
    "Property", "PropertyCustom", "PropertyComplexStart", "PropertyComplexEnd",
    "PropertyArrayStart", "PropertyArrayEnd", "PropertyIListStart",
    "PropertyIListEnd", "PropertyIDictionaryStart", "PropertyIDictionaryEnd",
    "LiteralContent", "Text", "RoutedEvent", "ClrEvent", "XmlnsProperty",
    "XmlAttribute", "ProcessingInstruction", "Comment", "IncludeTag",
    "DefArrayStart", "DefArrayEnd", "DefTag", "DefAttribute", "EndAttributes",
    "PIMapping", "AssemblyInfo", "TypeInfo", "TypeSerializerInfo",
    "AttributeInfo", "ResourceInfo", "PropertyResourceReference",
]
MAXTYPE = 36

BASE_VARIABLE = {1, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 23, 27,
                 29, 30, 31, 32, 33, 34, 35}


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def walk(data, variable):
    pos = 0
    n = 0
    while pos + 2 <= len(data):
        if n > 20000:
            return False, pos, -1
        t = struct.unpack_from("<h", data, pos)[0]
        if t < 0 or t > MAXTYPE:
            return False, pos, t
        if t in variable:
            if pos + 6 > len(data):
                return False, pos, t
            size = struct.unpack_from("<i", data, pos + 2)[0]
            nxt = pos + 2 + size
            if size < 0 or nxt <= pos + 2 or nxt > len(data):
                return False, pos, t
            pos = nxt
        else:
            pos += 2 + (2 if t in (3, 24) else 0)
        n += 1
    return (pos == len(data)), pos, None


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))
    datas = [read(p) for p in files]

    ok = sum(1 for d in datas if walk(d, BASE_VARIABLE)[0])
    print("baseline (transcribed table): %d / %d clean" % (ok, len(files)))
    print()

    # histogram of the record code at which each failing walk stops
    fails = Counter()
    for d in datas:
        good, pos, t = walk(d, BASE_VARIABLE)
        if not good:
            fails[t] += 1
    print("failing record codes (code at the stop point -> occurrences):")
    for code, cnt in fails.most_common():
        nm = TYPE_NAMES[code] if code is not None and 0 <= code < len(TYPE_NAMES) else "?"
        print("   code=%-6s %-26s %d" % (code, nm, cnt))
    print()

    # try flipping one code at a time
    print("single-code flips that improve the clean count:")
    base = ok
    improvements = []
    for code in range(0, MAXTYPE + 1):
        for add in (True, False):
            v = set(BASE_VARIABLE)
            if add:
                v.add(code)
            else:
                v.discard(code)
            if v == BASE_VARIABLE:
                continue
            n_ok = sum(1 for d in datas if walk(d, v)[0])
            if n_ok > base:
                improvements.append((n_ok, code, add))
    improvements.sort(reverse=True)
    for (n_ok, code, add) in improvements[:15]:
        print("   %-26s -> %s  gives %d/%d"
              % ("code %d %s" % (code, TYPE_NAMES[code] if code < len(TYPE_NAMES) else "?"),
                 "VARIABLE" if add else "fixed", n_ok, len(files)))
    if not improvements:
        print("   none — the divergence is not a single-code sizing flip")

    # greedy: repeatedly take the best single flip
    print()
    print("greedy multi-flip search:")
    cur = set(BASE_VARIABLE)
    cur_ok = base
    for step in range(8):
        best = None
        for code in range(0, MAXTYPE + 1):
            for add in (True, False):
                v = set(cur)
                if add:
                    v.add(code)
                else:
                    v.discard(code)
                if v == cur:
                    continue
                n_ok = sum(1 for d in datas if walk(d, v)[0])
                if best is None or n_ok > best[0]:
                    best = (n_ok, code, add, v)
        if best is None or best[0] <= cur_ok:
            break
        cur_ok, code, add, cur = best[0], best[1], best[2], best[3]
        print("   step %d: code %d -> %s   now %d/%d"
              % (step + 1, code, "VARIABLE" if add else "fixed", cur_ok, len(files)))
    print()
    print("final clean count: %d / %d" % (cur_ok, len(files)))
    print("final VARIABLE set: %s" % sorted(cur))
    return 0


if __name__ == "__main__":
    sys.exit(main())
