#!/usr/bin/env python3
"""
Extract the XAML text from a BinaryFormatter-serialized System.IO.MemoryStream.

Some Longhorn resource manifests store XAML not as BAML but as a serialized
`MemoryStream` whose `_buffer` holds the text. The payload looks like:

    00 01 00 00 00 ff ff ff ff 01 00 00 00 00 00 00 00   stream header
    04 01 00 00 00  "System.IO.MemoryStream"             class name (length-prefixed)
    0a 00 00 00                                          member count
    07 "_buffer" 07 "_origin" 09 "_position" ...         member-name table
    <values, one per member, in that same order>

The members come out in alphabetical order, and every one of them is a fixed-size
primitive except `_buffer`, so the buffer does not start at a predictable offset.

Rather than implement the whole BinaryFormatter grammar, the buffer is located from the
END of the blob: the trailing members are all small integers, so the large block of bytes
immediately before them is the buffer. That is robust to a different member order.

Usage:
    extract_bf_xaml.py <file.resx> <outdir> [--list]
"""
import base64
import os
import re
import sys
import xml.etree.ElementTree as ET

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# The member-name table: length-prefixed names, ending with the base-class identity.
NAME_TABLE_END = re.compile(
    rb'(?:\x1d|\x1e|\x1f|[\x00-\x1f])'
    rb'(?:MarshalByRefObject\+__identity|System\.Object\+__identity)')

MIN_MARKUP = 16          # a real document is far longer than this


def find_markup(blob):
    """
    Returns the XAML text found anywhere in the blob, or None.

    Scans for a '<' byte and tries to decode a run from there, which avoids having to
    model the surrounding values at all. The first candidate that decodes cleanly and
    looks like markup wins.
    """
    for start in range(len(blob)):
        if blob[start] != 0x3C:          # '<'
            continue
        for encoding, stride in (("utf-16-le", 2), ("utf-8", 1)):
            chunk = blob[start:start + 200000]
            # trim to an even length for utf-16
            if stride == 2:
                chunk = chunk[:len(chunk) - (len(chunk) % 2)]
            try:
                text = chunk.decode(encoding)
            except UnicodeDecodeError as exc:
                text = chunk[:exc.start].decode(encoding, "ignore")
            text = text.rstrip("\x00")
            if len(text) < MIN_MARKUP:
                continue
            # a markup document must contain at least one tag with a name
            if not re.search(r'<[A-Za-z_][\w.:\-]*', text):
                continue
            # and must not contain raw NULs, which mean we picked the wrong stride
            if "\x00" in text:
                continue
            return text
    return None


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 1
    path, outdir = argv[1], argv[2]
    list_only = "--list" in argv

    if not list_only and not os.path.exists(outdir):
        os.makedirs(outdir)

    tree = ET.parse(path)
    got = 0
    miss = 0
    for data_el in tree.getroot().findall("data"):
        name = data_el.get("name") or ""
        if not name.lower().endswith((".xaml", ".baml")):
            continue
        value_el = data_el.find("value")
        if value_el is None or not value_el.text:
            continue
        try:
            blob = base64.b64decode(value_el.text.strip())
        except Exception as exc:
            print("  ! %s: %s" % (name, exc))
            continue

        text = find_markup(blob)
        if text is None:
            miss += 1
            print("  ? %-52s no markup (%d B)" % (name, len(blob)))
            continue
        got += 1
        if list_only:
            print("  %-52s %6d chars" % (name, len(text)))
        else:
            with open(os.path.join(outdir, name), "w",
                      encoding="utf-8", newline="\n") as fh:
                fh.write(text)
                if not text.endswith("\n"):
                    fh.write("\n")

    print()
    print("%s" % path)
    print("  extracted: %d   not found: %d" % (got, miss))
    if not list_only:
        print("  written to: %s" % outdir)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
