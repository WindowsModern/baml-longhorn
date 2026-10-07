#!/usr/bin/env python3
"""
Extract embedded resources from a .g.resx (generated resource) file.

A `.g.resx` is the manifest that the compiler emits for an assembly's embedded
resources. Each `<data>` entry carries the resource either as a base64
`<value>` or as a `<file>` reference, and the resource NAME encodes its kind:

    <name>.baml        a compiled XAML stream
    <name>.xaml        a raw XAML stream
    <name>.gif/.png/...  an image

The 103-file primary corpus was extracted from
`Microsoft.Windows.WCPClient.g.resx` this way, so the same route opens up any
other assembly's compiled XAML as additional test material.

Usage:
    extract_gresx.py <file.g.resx> <outdir> [--list]
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


def entries(path):
    """Yields (name, mime, payload) for every data entry in the resx."""
    tree = ET.parse(path)
    root = tree.getroot()
    for data in root.findall("data"):
        name = data.get("name")
        mime = data.get("type") or data.get("mimetype") or ""
        value_el = data.find("value")
        if name is None:
            continue
        if value_el is not None and value_el.text:
            try:
                payload = base64.b64decode(value_el.text.strip())
            except Exception as exc:
                print("  ! %s: base64 decode failed: %s" % (name, exc))
                continue
            yield name, mime, payload
        else:
            file_el = data.find("file")
            if file_el is not None:
                yield name, mime, None      # external file reference


def kind_of(name):
    lower = name.lower()
    if lower.endswith(".baml"):
        return "baml"
    if lower.endswith(".xaml"):
        return "xaml"
    _, ext = os.path.splitext(lower)
    return ext.lstrip(".") or "other"


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 1

    path = argv[1]
    outdir = argv[2]
    list_only = "--list" in argv

    if not os.path.exists(path):
        print("missing: %s" % path)
        return 1

    counts = {}
    total = 0
    for name, mime, payload in entries(path):
        total += 1
        k = kind_of(name)
        counts[k] = counts.get(k, 0) + 1
        size = "external" if payload is None else "%d B" % len(payload)
        if list_only:
            print("  %-10s %-9s %s" % (k, size, name))
            continue
        if payload is None:
            continue
        # keep the resource name as the path, which mirrors the original tree
        target = os.path.join(outdir, name.replace("/", os.sep).replace("\\", os.sep))
        parent = os.path.dirname(target)
        if parent and not os.path.isdir(parent):
            os.makedirs(parent)
        with open(target, "wb") as fh:
            fh.write(payload)

    print("%s" % path)
    print("  entries: %d" % total)
    for k in sorted(counts):
        print("    %-10s %d" % (k, counts[k]))
    if not list_only:
        print("  written to: %s" % outdir)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
