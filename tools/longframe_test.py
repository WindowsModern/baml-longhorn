#!/usr/bin/env python3
"""
Decisive test: do the real 4074 samples use the CoreAvalon long-record framing?

If they do, then somewhere near the start of the file there is an offset p where

    int64 at p   = a plausible record size (positive, <= file length, even)
    int16 at p+8 = a valid BamlRecordType (0..24), and StartDocument (=1) for the
                   first record

The known record type codes (identical in both assemblies -- verified via
metadata: BamlRecordType in the 4093 assembly matches the core assembly item for
item) are:

    0 Unknown   1 StartDocument  2 EndDocument   3 Element     4 EndElement
    5 ParseLiteralContent        6 XmlnsProperty 7 DynamicProperty
    8 DynamicEvent               9 GenericAttribute 10 Text     11 AssemblyInfo
   12 TypeInfo  13 AttributeInfo 14 ComplexDynamicProperty
   15 EndComplexDynamicProperty  16 ClrObject    17 EndClrObject
   18 ClrProperty 19 ClrArrayProperty 20 EndClrArrayProperty
   21 ClrComplexProperty 22 EndClrComplexProperty 23 IncludeTag
   24 DynamicPropertyCustom
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

VALID = set(range(0, 25))


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def scan_document_headers(data, limit=96):
    """Find offsets where a long record header would start."""
    hits = []
    for p in range(0, min(limit, len(data) - 10)):
        size = struct.unpack_from("<q", data, p)[0]
        if size <= 0 or size > len(data) or size % 2 != 0:
            continue
        rtype = struct.unpack_from("<h", data, p + 8)[0]
        if rtype in VALID:
            hits.append((p, size, rtype))
    return hits


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))

    print("scanning the first 96 bytes of every sample for a long-record header")
    print()
    total_hits = 0
    first_hits = 0
    nonzero_sizes = 0
    for p in files:
        data = read(p)
        hits = scan_document_headers(data)
        if hits:
            total_hits += 1
            p0, size, rtype = hits[0]
            if rtype == 1:
                first_hits += 1
            if size > 20:
                nonzero_sizes += 1
    print("files with any candidate header : %d / %d" % (total_hits, len(files)))
    print("  ... whose first candidate is StartDocument(1): %d" % first_hits)
    print("  ... whose first candidate size  > 20        : %d" % nonzero_sizes)
    print()

    # show the smallest sample in detail
    data = read(files[0])
    print("detail for %s (%d bytes):" % (os.path.basename(files[0]), len(data)))
    hits = scan_document_headers(data)
    if not hits:
        print("  no candidate long-record header in the first 96 bytes")
    for (p, size, rtype) in hits[:10]:
        print("  @%-4d size=%-8d type=%d" % (p, size, rtype))
    print()

    # what does the stream look like if we assume the region before 28 is the
    # only header, i.e. is there ANY long-record structure after 28?
    print("candidate headers anywhere in the file (first 12):")
    allhits = []
    for p in range(0, len(data) - 10):
        size = struct.unpack_from("<q", data, p)[0]
        if size <= 0 or size > len(data) or size % 2 != 0:
            continue
        rtype = struct.unpack_from("<h", data, p + 8)[0]
        if rtype in VALID:
            allhits.append((p, size, rtype))
    for (p, size, rtype) in allhits[:12]:
        print("  @%-4d size=%-8d type=%d" % (p, size, rtype))
    print("  total candidates in file: %d" % len(allhits))
    return 0


if __name__ == "__main__":
    sys.exit(main())
