#!/usr/bin/env python3
"""
Census of brush-valued attributes in the 4074 corpus.

The point is to establish, before writing any code, how many brush values are
already structured and how many still arrive as an opaque string. A brush can reach
the decoder by two different routes:

  * a PropertyCustom record whose value is a tagged string, e.g.
    `[00][len]"#RRGGBB"` -- Brush.SerializeOn's Other branch
  * an ElementStart record naming the brush type, with the brush's own properties as
    child records -- the ordinary element route

Only the first can be "expanded", and only when the string is a compound form that
Parsers.ParseBrush would have accepted.
"""
import os
import re
import subprocess
import sys
import collections

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SAMPLES = os.path.join(ROOT, "samples", "xaml")
EXE = os.path.join(ROOT, "src", "BamlLonghorn.Cli", "bin", "Debug", "baml.exe")

BRUSH_ATTRS = ("Background", "Foreground", "Fill", "Stroke", "BorderBrush",
               "Color", "OpacityMask")
BRUSH_TYPES = ("Brush", "SolidColorBrush", "LinearGradientBrush",
               "RadialGradientBrush", "ImageBrush", "VisualBrush")

ATTR = re.compile(r'([\w:]+)="([^"]*)"')
ELEMENT = re.compile(r'<([\w.:]+)')

# the grammar Parsers.ParseBrush accepts, per the 4074 PresentationCore decompile
COMPOUND = re.compile(
    r'^\s*(HorizontalGradient|VerticalGradient|RadialGradient|LinearGradient)\b',
    re.IGNORECASE)


def main():
    if not os.path.exists(EXE):
        print("missing CLI: %s" % EXE)
        return 1

    files = []
    for dp, _dn, fns in os.walk(SAMPLES):
        for n in sorted(fns):
            if n.lower().endswith(".baml"):
                files.append(os.path.join(dp, n))
    files.sort(key=os.path.getsize)

    brush_values = collections.Counter()
    compound = collections.Counter()
    brush_elements = collections.Counter()
    total_brush = 0

    for path in files:
        out = subprocess.run([EXE, "-q", "xaml", path], capture_output=True, timeout=60)
        text = out.stdout.decode("utf-8", "replace")

        for m in ATTR.finditer(text):
            if m.group(1) in BRUSH_ATTRS:
                total_brush += 1
                value = m.group(2)
                if COMPOUND.match(value):
                    compound[value.split()[0]] += 1
                else:
                    brush_values[value] += 1

        for m in ELEMENT.finditer(text):
            name = m.group(1)
            short = name.rsplit(".", 1)[-1]
            if short in BRUSH_TYPES:
                brush_elements[short] += 1

    print("brush-valued attributes total : %d" % total_brush)
    print()
    print("brushes arriving as ELEMENTS (already structured):")
    for k, c in brush_elements.most_common(10):
        print("   %-24s %d" % (k, c))
    print()
    print("values matching Parsers.ParseBrush's compound grammar:")
    if compound:
        for k, c in compound.most_common(10):
            print("   %-24s %d" % (k, c))
    else:
        print("   (none)")
    print()
    print("brush values that are plain colours or names (nothing to expand):")
    for k, c in brush_values.most_common(12):
        print("   %-42s %d" % (repr(k), c))
    return 0


if __name__ == "__main__":
    sys.exit(main())
