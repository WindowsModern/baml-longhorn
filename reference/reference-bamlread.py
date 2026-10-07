#!/usr/bin/env python3
"""
Longhorn BAML reader/writer -- reference implementation of the v0 spec.

Format source: decompiled MS.Internal\\Baml*Record.cs from System.Windows.dll 6.0.3708.0
(Longhorn build 4074 era). See LONGHORN-BAML-SPEC-v0.md.

Framing (per BamlRecord.Write / BamlRecordManager.GetNextRecord):
    int64  recordSize   # LE; = 2 (type field) + payload; EXCLUDES these 8 bytes
    int16  recordType   # LE
    ...    payload

Every pointer field is stored RELATIVE to the record's own position and is
recovered as `field + record_pos`, where record_pos is the offset of the
recordType field.  `<= 0` means "none" (see BamlNodeRecord.SeekNextChild).

The "roundtrip" mode is the only self-check possible without a genuine
Longhorn BAML sample: it encodes a document from this spec and decodes it
again.  It validates internal consistency, NOT byte-compatibility with the
real compiler (which could not be run -- see README).
"""
import struct
import sys

# ---------------------------------------------------------------- record types
REC = {
    0: "Unknown", 1: "StartDocument", 2: "EndDocument", 3: "Element",
    4: "EndElement", 5: "ParseLiteralContent", 6: "XmlnsProperty",
    7: "DynamicProperty", 8: "DynamicEvent", 9: "GenericAttribute", 10: "Text",
    11: "AssemblyInfo", 12: "TypeInfo", 13: "AttributeInfo",
    14: "ComplexDynamicProperty", 15: "EndComplexDynamicProperty",
    16: "ClrObject", 17: "EndClrObject", 18: "ClrProperty",
    19: "ClrArrayProperty", 20: "EndClrArrayProperty", 21: "ClrComplexProperty",
    22: "EndClrComplexProperty", 23: "IncludeTag", 24: "DynamicPropertyCustom",
}
REC_BY_NAME = {v: k for k, v in REC.items()}

# Records whose payload begins with the 12-byte node header.
# (BamlNodeRecord subclasses only.)
NODE_RECORDS = {"Element", "ParseLiteralContent", "Text", "ClrObject"}

# Records with an empty payload (Baml*End*Record with no fields).
EMPTY_RECORDS = {"EndDocument", "EndElement", "EndComplexDynamicProperty",
                 "EndClrObject", "EndClrArrayProperty", "EndClrComplexProperty",
                 "Unknown"}


# ------------------------------------------------------------------- primitives
def _read_str(buf, pos):
    """BinaryReader.ReadString: 7-bit-encoded byte length, then UTF-8."""
    length = 0
    shift = 0
    while True:
        b = buf[pos]
        pos += 1
        length |= (b & 0x7F) << shift
        if not (b & 0x80):
            break
        shift += 7
    raw = buf[pos:pos + length]
    return raw.decode("utf-8", "replace"), pos + length


def _write_str(value):
    raw = value.encode("utf-8")
    n = len(raw)
    out = bytearray()
    while n >= 0x80:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    out.append(n)
    out += raw
    return bytes(out)


class Record:
    def __init__(self, rtype, pos=0, size=0, fields=None, node=None):
        self.type = rtype
        self.pos = pos          # offset of the recordType field
        self.size = size
        self.fields = {} if fields is None else dict(fields)   # insertion ordered
        self.node = {} if node is None else dict(node)         # node-header fields

    def __repr__(self):
        return "Record(%s@%d size=%d %s%s)" % (
            self.type, self.pos, self.size, self.node, self.fields)


# ---------------------------------------------------------------------- reader
def parse(buf):
    """Yield Records. Raises ValueError with a byte offset on any inconsistency."""
    records = []
    pos = 0
    total = len(buf)
    while pos + 10 <= total:
        record_pos = pos + 8                    # offset of the type field
        size = struct.unpack_from("<q", buf, pos)[0]
        if size <= 0:
            break
        if size > total - pos:
            raise ValueError("record at %d claims size %d but only %d bytes remain"
                             % (pos, size, total - pos))
        rtype = struct.unpack_from("<h", buf, record_pos)[0]
        name = REC.get(rtype)
        if name is None:
            raise ValueError("unknown record type %d at offset %d" % (rtype, record_pos))
        rec = Record(name, record_pos, size)
        p = record_pos + 2
        # payload ends at the record's end (size spans the whole record)
        end = pos + size

        if name in NODE_RECORDS:
            depth, parent, right, leftcnt = struct.unpack_from("<hiiH", buf, p)
            rec.node = {
                "depth": depth,
                "parentOffset": parent + record_pos if parent > 0 else parent,
                "rightSiblingOffset": right + record_pos if right > 0 else right,
                "leftElementSiblingsCount": leftcnt,
            }
            p += 12

        f = rec.fields
        if name == "StartDocument":
            root, = struct.unpack_from("<i", buf, p); p += 4
            f["rootElementOffset"] = root + record_pos if root > 0 else root
            f["loadAsync"] = bool(buf[p]); p += 1
            f["maxAsyncRecords"], = struct.unpack_from("<i", buf, p); p += 4
        elif name == "Element":
            f["id"], f["childNodes"], f["elementNodes"] = struct.unpack_from("<hhh", buf, p); p += 6
            first, = struct.unpack_from("<i", buf, p); p += 4
            f["firstChildOffset"] = first + record_pos if first > 0 else first
        elif name == "ParseLiteralContent":
            f["value"], p = _read_str(buf, p)
            f["lineNumber"], f["linePosition"] = struct.unpack_from("<ii", buf, p); p += 8
        elif name == "XmlnsProperty":
            f["prefix"], p = _read_str(buf, p)
            f["value"], p = _read_str(buf, p)
        elif name == "DynamicProperty":
            f["attributeId"], = struct.unpack_from("<h", buf, p); p += 2
            f["value"], p = _read_str(buf, p)
            f["complex"] = bool(buf[p]); p += 1
        elif name == "DynamicEvent":
            f["attributeId"], = struct.unpack_from("<h", buf, p); p += 2
            f["value"], p = _read_str(buf, p)
        elif name == "GenericAttribute":
            f["namespaceUri"], p = _read_str(buf, p)
            f["localName"], p = _read_str(buf, p)
            f["value"], p = _read_str(buf, p)
        elif name == "Text":
            f["value"], p = _read_str(buf, p)
        elif name == "AssemblyInfo":
            f["assemblyId"], = struct.unpack_from("<h", buf, p); p += 2
            f["assemblyFullName"], p = _read_str(buf, p)
        elif name == "TypeInfo":
            f["typeId"], f["assemblyId"] = struct.unpack_from("<hh", buf, p); p += 4
            f["typeFullName"], p = _read_str(buf, p)
        elif name == "AttributeInfo":
            f["attributeId"], f["ownerTypeId"] = struct.unpack_from("<hh", buf, p); p += 4
            f["name"], p = _read_str(buf, p)
        elif name == "ComplexDynamicProperty":
            f["attributeId"], = struct.unpack_from("<h", buf, p); p += 2
        elif name == "ClrObject":
            f["id"], = struct.unpack_from("<h", buf, p); p += 2
        elif name == "ClrProperty":
            f["name"], p = _read_str(buf, p)
            f["value"], p = _read_str(buf, p)
            f["fieldTypeId"], = struct.unpack_from("<h", buf, p); p += 2
        elif name in ("ClrArrayProperty", "ClrComplexProperty"):
            f["name"], p = _read_str(buf, p)
        elif name == "IncludeTag":
            f["value"], p = _read_str(buf, p)

        rec.fields = f
        records.append(rec)
        pos += size
    return records


def _rel(value, rec):
    """Encode a pointer field: absolute offset -> position-relative.
    A non-positive value is a sentinel ('none') and is written verbatim,
    mirroring the reader, which only rebases positive values."""
    if not isinstance(value, int) or value <= 0:
        return value
    return value - rec.pos


# ---------------------------------------------------------------------- writer
def _node_header(rec):
    n = rec.node
    return struct.pack("<hiiH", n.get("depth", -1),
                       _rel(n.get("parentOffset", -1), rec),
                       _rel(n.get("rightSiblingOffset", -1), rec),
                       n.get("leftElementSiblingsCount", 0) & 0xFFFF)


def serialize(records):
    """Encode Records.

    Pointer fields in rec.node / rec.fields are supplied as ABSOLUTE offsets
    and are rebased against each record's own position (pos = offset of the
    recordType field, i.e. record_start + 8).

    Positions are assigned in a first pass that advances by each record's
    *computed* payload length; the payload length depends only on field values,
    not on positions, so the pass is exact.
    """
    out = bytearray()
    for rec in records:
        rec.pos = len(out) + 8
        payload = _build_payload(rec)
        # recordSize is the TOTAL record length, including this 8-byte size
        # field.  Both the real reader (BamlReader.GetNextRecord, which does
        # `pos += recordSize`) and this parser advance by it, so it must span
        # the whole record.  It is kept even by a pad byte so the next record
        # starts on an even offset.
        size = 8 + 2 + len(payload)
        if size % 2:
            size += 1
        rec.size = size
        out += struct.pack("<q", size)
        out += struct.pack("<h", REC_BY_NAME[rec.type])
        out += payload
        if size > 8 + 2 + len(payload):
            out += b"\x00"
    return bytes(out)


def _build_payload(rec):
    payload = bytearray()
    if rec.type in NODE_RECORDS:
        payload += _node_header(rec)
    f = rec.fields
    if rec.type == "StartDocument":
        payload += struct.pack("<i", _rel(f.get("rootElementOffset", -1), rec))
        payload += struct.pack("<?", f.get("loadAsync", False))
        payload += struct.pack("<i", f.get("maxAsyncRecords", 0))
    elif rec.type == "Element":
        payload += struct.pack("<hhh", f["id"], f["childNodes"], f["elementNodes"])
        payload += struct.pack("<i", _rel(f.get("firstChildOffset", -1), rec))
    elif rec.type == "ParseLiteralContent":
        payload += _write_str(f["value"])
        payload += struct.pack("<ii", f.get("lineNumber", 0), f.get("linePosition", 0))
    elif rec.type == "XmlnsProperty":
        payload += _write_str(f["prefix"]) + _write_str(f["value"])
    elif rec.type == "DynamicProperty":
        payload += struct.pack("<h", f["attributeId"])
        payload += _write_str(f["value"]) + struct.pack("<?", f.get("complex", False))
    elif rec.type == "DynamicEvent":
        payload += struct.pack("<h", f["attributeId"]) + _write_str(f["value"])
    elif rec.type == "GenericAttribute":
        payload += _write_str(f["namespaceUri"]) + _write_str(f["localName"]) + _write_str(f["value"])
    elif rec.type == "Text":
        payload += _write_str(f["value"])
    elif rec.type == "AssemblyInfo":
        payload += struct.pack("<h", f["assemblyId"]) + _write_str(f["assemblyFullName"])
    elif rec.type == "TypeInfo":
        payload += struct.pack("<hh", f["typeId"], f["assemblyId"]) + _write_str(f["typeFullName"])
    elif rec.type == "AttributeInfo":
        payload += struct.pack("<hh", f["attributeId"], f["ownerTypeId"]) + _write_str(f["name"])
    elif rec.type == "ComplexDynamicProperty":
        payload += struct.pack("<h", f["attributeId"])
    elif rec.type == "ClrObject":
        payload += struct.pack("<h", f["id"])
    elif rec.type == "ClrProperty":
        payload += _write_str(f["name"]) + _write_str(f["value"]) + struct.pack("<h", f.get("fieldTypeId", 0))
    elif rec.type in ("ClrArrayProperty", "ClrComplexProperty"):
        payload += _write_str(f["name"])
    elif rec.type == "IncludeTag":
        payload += _write_str(f["value"])
    return payload


# --------------------------------------------------------------------- printer
def dump(records):
    lines = []
    for rec in records:
        lines.append("[%06d] %-26s size=%-5d" % (rec.pos, rec.type, rec.size))
        if rec.node:
            lines.append("         node: depth=%s parent=%s right=%s leftCount=%s" % (
                rec.node["depth"], rec.node["parentOffset"],
                rec.node["rightSiblingOffset"], rec.node["leftElementSiblingsCount"]))
        for k, v in rec.fields.items():
            lines.append("         %s = %r" % (k, v))
    return "\n".join(lines)


def to_pseudo_xaml(records):
    """Best-effort tree reconstruction using depth/parent/sibling links."""
    out = []
    depth_stack = []
    for rec in records:
        if rec.type == "Element":
            d = rec.node.get("depth", 0)
            while depth_stack and depth_stack[-1] >= d:
                depth_stack.pop()
            out.append("%s<Element id=%s depth=%s children=%s/%s>" % (
                "  " * d, rec.fields["id"], d,
                rec.fields["childNodes"], rec.fields["elementNodes"]))
            depth_stack.append(d)
        elif rec.type == "Text":
            out.append("%s  <Text %r/>" % ("  " * max(len(depth_stack) - 1, 0), rec.fields["value"]))
        elif rec.type == "DynamicProperty":
            out.append("%s  @attr[%s] = %r" % (
                "  " * max(len(depth_stack) - 1, 0), rec.fields["attributeId"], rec.fields["value"]))
        elif rec.type == "EndElement":
            if depth_stack:
                depth_stack.pop()
            out.append("%s</Element>" % ("  " * max(len(depth_stack), 0)))
        elif rec.type in ("AssemblyInfo", "TypeInfo", "AttributeInfo"):
            out.append("; %s %s" % (rec.type, rec.fields))
        elif rec.type == "StartDocument":
            out.append("; StartDocument %s" % rec.fields)
        elif rec.type == "EndDocument":
            out.append("; EndDocument")
    return "\n".join(out)


# ------------------------------------------------------------------------ main
def selftest():
    """Encode a document from the spec, decode it, compare. Also verify the
    framing invariant that decoding positions line up with encoding."""
    doc = [
        Record("StartDocument", fields={"rootElementOffset": 0, "loadAsync": False, "maxAsyncRecords": 0}),
        Record("AssemblyInfo", fields={"assemblyId": 0, "assemblyFullName": "MSDotnetAvalon.Windows, Version=6.0.3708.0"}),
        Record("TypeInfo", fields={"typeId": 0, "assemblyId": 0, "typeFullName": "MSDotnetAvalon.Windows.Controls.DockPanel"}),
        Record("TypeInfo", fields={"typeId": 1, "assemblyId": 0, "typeFullName": "MSDotnetAvalon.Windows.Documents.Text"}),
        Record("AttributeInfo", fields={"attributeId": 0, "ownerTypeId": 0, "name": "ID"}),
        Record("AttributeInfo", fields={"attributeId": 1, "ownerTypeId": 1, "name": "Role"}),
        Record("XmlnsProperty", fields={"prefix": "", "value": "using:MSDotnetAvalon.Windows.Controls"}),
        Record("Element", node={"depth": 0, "parentOffset": -1, "rightSiblingOffset": -1,
                                "leftElementSiblingsCount": 0},
               fields={"id": 0, "childNodes": 2, "elementNodes": 2, "firstChildOffset": -1}),
        Record("DynamicProperty", fields={"attributeId": 0, "value": "root", "complex": False}),
        Record("Element", node={"depth": 1, "parentOffset": -1, "rightSiblingOffset": -1,
                                "leftElementSiblingsCount": 0},
               fields={"id": 1, "childNodes": 1, "elementNodes": 0, "firstChildOffset": -1}),
        Record("DynamicProperty", fields={"attributeId": 1, "value": "TITLE", "complex": False}),
        Record("Text", node={"depth": 2, "parentOffset": -1, "rightSiblingOffset": -1,
                             "leftElementSiblingsCount": 0},
               fields={"value": "Hello Longhorn"}),
        Record("EndElement", fields={}),
        Record("EndElement", fields={}),
        Record("EndDocument", fields={}),
    ]
    # patch the two offset fields now that positions are known by a dry run
    blob = serialize(doc)
    before = len(blob)
    # second pass with real offsets: rootElement -> first Element; firstChild -> its child
    elem0 = next(r for r in doc if r.type == "Element")
    doc[0].fields["rootElementOffset"] = elem0.pos
    child = doc[doc.index(elem0) + 2]
    elem0.fields["firstChildOffset"] = child.pos
    blob = serialize(doc)

    back = parse(blob)
    assert len(back) == len(doc), "record count %d != %d" % (len(back), len(doc))
    for a, b in zip(doc, back):
        assert a.type == b.type, "type %s != %s" % (a.type, b.type)
        assert a.fields == b.fields, "fields differ for %s:\n  %s\n  %s" % (a.type, a.fields, b.fields)
        if a.type in NODE_RECORDS:
            for k in ("depth", "leftElementSiblingsCount"):
                assert a.node[k] == b.node[k], (a.type, k)
            for k in ("parentOffset", "rightSiblingOffset"):
                assert a.node[k] == b.node[k], (a.type, k, a.node[k], b.node[k])
    print("SELFTEST OK  (%d records, %d bytes)" % (len(back), len(blob)))
    print()
    print(dump(back))
    print()
    print("--- pseudo-XAML reconstruction ---")
    print(to_pseudo_xaml(back))
    return 0


def main(argv):
    if len(argv) < 2 or argv[1] == "selftest":
        return selftest()
    with open(argv[1], "rb") as fh:
        buf = fh.read()
    print("input: %s (%d bytes)" % (argv[1], len(buf)))
    try:
        records = parse(buf)
    except ValueError as exc:
        print("PARSE ERROR: %s" % exc)
        return 2
    print("parsed %d records" % len(records))
    print()
    print(dump(records))
    print()
    print("--- pseudo-XAML reconstruction ---")
    print(to_pseudo_xaml(records))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
