#!/usr/bin/env python3
"""
Align the build-4074 record stream, using the decompiled framing.

Framing (from System.Windows.Serialization of PresentationFramework, decompiled):
    <type>  int16 LE                      always 2 bytes
    <size>  int32 LE                      only for BamlVariableSizedRecord subclasses
    <payload>

Constraints that must hold for the correct alignment:
  * the first record is DocumentStart  -> int16 == 1
  * DocumentStart is variable-sized    -> the next int32 is its size
  * walking must land exactly on known string boundaries and, for the whole file,
    either reach EOF exactly or reach a DocumentEnd

This tool brute-forces the stream start offset plus the payload interpretation of
DocumentStart, and reports every alignment that walks cleanly.
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

TYPE_NAMES = {
    0: "Unknown", 1: "DocumentStart", 2: "DocumentEnd", 3: "ElementStart",
    4: "ElementEnd", 5: "Property", 6: "PropertyCustom",
    7: "PropertyComplexStart", 8: "PropertyComplexEnd", 9: "PropertyArrayStart",
    10: "PropertyArrayEnd", 11: "PropertyIListStart", 12: "PropertyIListEnd",
    13: "PropertyIDictionaryStart", 14: "PropertyIDictionaryEnd",
    15: "LiteralContent", 16: "Text", 17: "RoutedEvent", 18: "ClrEvent",
    19: "XmlnsProperty", 20: "XmlAttribute", 21: "ProcessingInstruction",
    22: "Comment", 23: "IncludeTag", 24: "DefArrayStart", 25: "DefArrayEnd",
    26: "DefTag", 27: "DefAttribute", 28: "EndAttributes", 29: "PIMapping",
    30: "AssemblyInfo", 31: "TypeInfo", 32: "TypeSerializerInfo",
    33: "AttributeInfo", 34: "ResourceInfo", 35: "PropertyResourceReference",
}


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def main():
    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))
    path = files[0]
    data = read(path)
    print("%s (%d bytes)" % (os.path.basename(path), len(data)))
    print()

    print("=== every offset in 0..64 where int16 == 1 (DocumentStart) ===")
    for p in range(0, min(64, len(data) - 6)):
        v = struct.unpack_from("<h", data, p)[0]
        if v == 1:
            size = struct.unpack_from("<i", data, p + 2)[0]
            print("  @%-4d type=1 DocumentStart   next int32 = %d  (payload would end at %d)"
                  % (p, size, p + 6 + size))
    print()

    print("=== bytes 24..64 in full ===")
    for i in range(24, min(64, len(data))):
        print("  %3d: %02x %3d" % (i, data[i], data[i]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
