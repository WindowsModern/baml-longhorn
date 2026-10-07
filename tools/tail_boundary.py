#!/usr/bin/env python3
"""
Settle the record boundary rule in the region that carries PropertyCustom
records, using enhancements\\simplelist.baml's tail as the test case.

Known-good reading so far (from the decoder, which walks 103/103 to EOF once
PropertyCustom stops reading a string):

    @198  AttributeInfo   size=14  attributeId=1 ownerTypeId=0 name="Width"
    @214  PropertyCustom  size=8   attributeId=1
    @226  LiteralContent  size=15
    @249  ...

This tool prints the tail with candidates for where each record can end, so the
exact relationship between `size`, the payload, and any inter-record padding is
visible rather than assumed.
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
PATH = os.path.join(SAMPLES, "enhancements", "simplelist.baml")

VARIABLE = {1, 5, 6, 15, 16, 17, 19, 23, 25, 28, 29, 30, 31, 32}
NAMES = {
    1: "DocumentStart", 2: "DocumentEnd", 3: "ElementStart", 4: "ElementEnd",
    5: "Property", 6: "PropertyCustom", 7: "PropertyComplexStart",
    8: "PropertyComplexEnd", 9: "PropertyArrayStart", 10: "PropertyArrayEnd",
    11: "PropertyIListStart", 12: "PropertyIListEnd",
    13: "PropertyIDictionaryStart", 14: "PropertyIDictionaryEnd",
    15: "LiteralContent", 16: "Text", 17: "RoutedEvent", 18: "ClrEvent",
    19: "XmlnsProperty", 23: "IncludeTag", 25: "DefAttribute",
    28: "PIMapping", 29: "AssemblyInfo", 30: "TypeInfo",
    31: "TypeSerializerInfo", 32: "AttributeInfo",
}


def main():
    d = open(PATH, "rb").read()
    print("%s  (%d bytes)" % (os.path.basename(PATH), len(d)))
    print()

    # authoritative walk: size rule = sizeFieldStart + size
    pos = 0
    print("=== walk with next = (start+2) + size ===")
    while pos + 2 <= len(d):
        t = struct.unpack_from("<h", d, pos)[0]
        if t not in NAMES:
            print("  @%-5d type=%d  <-- not a record" % (pos, t))
            break
        if t in VARIABLE:
            size = struct.unpack_from("<i", d, pos + 2)[0]
            payload = size - 4
            print("  @%-5d %-20s size=%-4d payloadBytes=%-3d ends@%d"
                  % (pos, NAMES[t], size, payload, pos + 2 + size))
            pos = pos + 2 + size
        else:
            extra = 2 if t in (3, 7, 9, 11, 13) else 0
            print("  @%-5d %-20s (fixed, %d bytes)" % (pos, NAMES[t], 2 + extra))
            pos += 2 + extra
    print()

    print("=== raw bytes from 190 to end ===")
    for i in range(190, len(d)):
        c = d[i]
        ch = chr(c) if 32 <= c < 127 else "."
        print("  %4d: %02x %3d  %s" % (i, c, c, ch))
    return 0


if __name__ == "__main__":
    sys.exit(main())
