#!/usr/bin/env python3
"""
build-4074 BAML record decoder — authoritative tables.

Source of truth: the decompiled PresentationFramework from **build 4074**
(`...\\4074 - PresentationFramework True\\System.Windows.Serialization\\`).
An earlier attempt used a *different* build (6.0.4051.31026 /
`4093 - PresentationFramework`), whose `BamlRecordType` has 36 members in a
DIFFERENT order. That mismatch is why the walk covered only 57 of 103 samples.

FRAMING (BamlRecord.cs / BamlVariableSizedRecord.cs)

    <type>  int16 LE   always 2 bytes   (BamlRecord.RecordTypeFieldLength = 2)
    <size>  int32 LE   only for BamlVariableSizedRecord subclasses
    <payload>

    next record = (offset of the size field) + size = recordStart + 2 + size

    BamlVariableSizedRecord.Write:
        num  = recordStart; Write((short)RecordType); num += 2;
        WriteRecordSize(); WriteRecordData();
        num2 = current;  RecordSize = num2 - num;     // size field + payload

ENUM (BamlRecordType : short, 34 members, order == code)

     0 Unknown                 17 RoutedEvent
     1 DocumentStart           18 ClrEvent
     2 DocumentEnd             19 XmlnsProperty
     3 ElementStart            20 XmlAttribute
     4 ElementEnd              21 ProcessingInstruction
     5 Property                22 Comment
     6 PropertyCustom          23 IncludeTag
     7 PropertyComplexStart    24 DefTag
     8 PropertyComplexEnd      25 DefAttribute
     9 PropertyArrayStart      26 EndAttributes
    10 PropertyArrayEnd        27 EndStartElement
    11 PropertyIListStart      28 PIMapping
    12 PropertyIListEnd        29 AssemblyInfo
    13 PropertyIDictionaryStart 30 TypeInfo
    14 PropertyIDictionaryEnd  31 TypeSerializerInfo
    15 LiteralContent          32 AttributeInfo
    16 Text                    33 LastRecordType

Codes 20, 21, 22, 24, 26, 27 have NO record class (BamlRecordManager.AllocateRecord
returns null), so they must never be treated as live records. There is no
DefArrayStart/DefArrayEnd in this build.

PAYLOAD READERS (transcribed from each LoadRecordData)

    DocumentStart      FormatVersion + Boolean LoadAsync + Int32 MaxAsyncRecords
    ElementStart       Int16 TypeId                                  (fixed)
    Property           Int16 AttributeId + string
    PropertyCustom     Int16 AttributeId + string
    RoutedEvent        Int16 AttributeId + string
    PropertyComplex/Array/IList/IDictionary Start   Int16 AttributeId (fixed)
    ...End records     -                                             (fixed)
    Text / IncludeTag  string
    LiteralContent     string + Int32 + Int32
    DefAttribute       string Value + string Name
    XmlnsProperty      string Prefix + string Value
    PIMapping          string Xmlns + string Clrns + Int16 AssemblyId
    AssemblyInfo       Int16 AssemblyId + string FullName
    TypeInfo           Int16 TypeId + Int16 AssemblyId + string TypeFullName
    TypeSerializerInfo TypeInfo + Int16 SerializerTypeId
    AttributeInfo      Int16 AttributeId + Int16 OwnerTypeId + string Name
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
    22: "Comment", 23: "IncludeTag", 24: "DefTag", 25: "DefAttribute",
    26: "EndAttributes", 27: "EndStartElement", 28: "PIMapping",
    29: "AssemblyInfo", 30: "TypeInfo", 31: "TypeSerializerInfo",
    32: "AttributeInfo", 33: "LastRecordType",
}

IMPLEMENTED = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
               18, 19, 23, 25, 28, 29, 30, 31, 32}

VARIABLE = {1, 5, 6, 15, 16, 17, 19, 23, 25, 28, 29, 30, 31, 32}

FIXED_WITH_INT16 = {3, 7, 9, 11, 13}


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def read7bit(b, pos):
    v = 0
    s = 0
    while True:
        if pos >= len(b):
            raise ValueError("truncated 7-bit int")
        c = b[pos]
        pos += 1
        v |= (c & 0x7F) << s
        if not (c & 0x80):
            return v, pos
        s += 7


def read_string(b, pos):
    n, pos = read7bit(b, pos)
    if n < 0 or pos + n > len(b):
        raise ValueError("bad string length %d" % n)
    return b[pos:pos + n].decode("utf-8", "replace"), pos + n


def dword_pad(n):
    return (4 - (n % 4)) % 4


def read_format_version(b, pos):
    if pos + 4 > len(b):
        raise ValueError("no room for FormatVersion")
    n = struct.unpack_from("<i", b, pos)[0]
    pos += 4
    if n < 0 or n % 2 or pos + n > len(b):
        raise ValueError("bad FormatVersion string length %d" % n)
    text = b[pos:pos + n].decode("utf-16-le", "replace")
    pos += n + dword_pad(n)
    vers = []
    for _ in range(3):
        vers.append(struct.unpack_from("<hh", b, pos))
        pos += 4
    return text, vers, pos


def parse(data, start=0):
    """Walk records. Returns (records, end, error)."""
    pos = start
    records = []
    while pos + 2 <= len(data):
        t = struct.unpack_from("<h", data, pos)[0]
        if t not in TYPE_NAMES:
            return records, pos, "type %d not in enum @%d" % (t, pos)
        if t not in IMPLEMENTED:
            return records, pos, "type %d (%s) has no record class @%d" \
                % (t, TYPE_NAMES[t], pos)
        rec = {"off": pos, "type": t, "name": TYPE_NAMES[t], "fields": {}}
        p = pos + 2

        if t in VARIABLE:
            if p + 4 > len(data):
                return records, pos, "no room for size @%d" % pos
            size = struct.unpack_from("<i", data, p)[0]
            rec["size"] = size
            body = p + 4
            end = p + size
            if size < 0 or end > len(data) or end < body:
                return records, pos, "size %d -> end %d @%d" % (size, end, pos)
            try:
                if t == 1:
                    feat, vers, q = read_format_version(data, body)
                    rec["fields"]["featureId"] = feat
                    rec["fields"]["reader"] = vers[0]
                    rec["fields"]["updater"] = vers[1]
                    rec["fields"]["writer"] = vers[2]
                    rec["fields"]["loadAsync"] = data[q]
                    rec["fields"]["maxAsyncRecords"] = struct.unpack_from("<i", data, q + 1)[0]
                elif t in (5, 6, 17):
                    rec["fields"]["attributeId"] = struct.unpack_from("<h", data, body)[0]
                    rec["fields"]["value"], _ = read_string(data, body + 2)
                elif t in (16, 23):
                    rec["fields"]["value"], _ = read_string(data, body)
                elif t == 15:
                    rec["fields"]["value"], q = read_string(data, body)
                    rec["fields"]["a"] = struct.unpack_from("<i", data, q)[0]
                    rec["fields"]["b"] = struct.unpack_from("<i", data, q + 4)[0]
                elif t == 25:
                    rec["fields"]["value"], q = read_string(data, body)
                    rec["fields"]["name"], _ = read_string(data, q)
                elif t == 19:
                    rec["fields"]["prefix"], q = read_string(data, body)
                    rec["fields"]["value"], _ = read_string(data, q)
                elif t == 28:
                    rec["fields"]["xmlns"], q = read_string(data, body)
                    rec["fields"]["clrns"], q = read_string(data, q)
                    rec["fields"]["assemblyId"] = struct.unpack_from("<h", data, q)[0]
                elif t == 29:
                    rec["fields"]["assemblyId"] = struct.unpack_from("<h", data, body)[0]
                    rec["fields"]["fullName"], _ = read_string(data, body + 2)
                elif t == 30:
                    rec["fields"]["typeId"] = struct.unpack_from("<h", data, body)[0]
                    rec["fields"]["assemblyId"] = struct.unpack_from("<h", data, body + 2)[0]
                    rec["fields"]["typeFullName"], _ = read_string(data, body + 4)
                elif t == 31:
                    rec["fields"]["typeId"] = struct.unpack_from("<h", data, body)[0]
                    rec["fields"]["assemblyId"] = struct.unpack_from("<h", data, body + 2)[0]
                    rec["fields"]["typeFullName"], q = read_string(data, body + 4)
                    rec["fields"]["serializerTypeId"] = struct.unpack_from("<h", data, q)[0]
                elif t == 32:
                    rec["fields"]["attributeId"] = struct.unpack_from("<h", data, body)[0]
                    rec["fields"]["ownerTypeId"] = struct.unpack_from("<h", data, body + 2)[0]
                    rec["fields"]["name"], _ = read_string(data, body + 4)
            except (ValueError, struct.error) as exc:
                return records, pos, "payload @%d: %s" % (pos, exc)
            records.append(rec)
            pos = end
            continue

        if t in FIXED_WITH_INT16:
            if p + 2 > len(data):
                return records, pos, "no room for Int16 @%d" % pos
            rec["fields"]["int16"] = struct.unpack_from("<h", data, p)[0]
            p += 2
        rec["size"] = p - pos
        records.append(rec)
        pos = p
    return records, pos, None


def main(argv):
    if len(argv) > 1 and argv[1] == "all":
        files = []
        for dp, _dn, fns in os.walk(SAMPLES):
            for n in sorted(fns):
                if n.lower().endswith(".baml"):
                    files.append(os.path.join(dp, n))
        files.sort(key=lambda p: os.path.getsize(p))
        print("%-46s %6s %6s %6s  %s" % ("file", "bytes", "recs", "end", "verdict"))
        clean = 0
        for p in files:
            data = read(p)
            try:
                recs, end, err = parse(data)
            except Exception as exc:
                print("%-46s  EXC %s" % (os.path.basename(p), exc))
                continue
            ok = err is None and end == len(data)
            if ok:
                clean += 1
            print("%-46s %6d %6d %6d  %s"
                  % (os.path.relpath(p, SAMPLES), len(data), len(recs), end,
                     "CLEAN" if ok else "no (%s)" % err))
        print()
        print("walked cleanly to EOF: %d / %d" % (clean, len(files)))
        return 0

    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in fns:
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=lambda p: os.path.getsize(p))
    path = argv[1] if len(argv) > 1 else files[0]
    data = read(path)
    recs, end, err = parse(data)
    print("%s (%d bytes) -> %d records, end=%d, err=%s"
          % (os.path.basename(path), len(data), len(recs), end, err))
    for r in recs[:40]:
        extra = " ".join("%s=%r" % (k, v) for k, v in r["fields"].items())
        print("  @%-4d %-22s size=%-5s %s" % (r["off"], r["name"], r.get("size"), extra))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
