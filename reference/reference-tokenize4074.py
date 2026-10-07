#!/usr/bin/env python3
"""
Empirical framing decoder for build-4074 Longhorn BAML.

Findings so far (all 74 samples share the same shape):
  * byte 0x00 = version byte, value 1
  * then an AssemblyInfo-ish record: 7-bit length prefix + UTF-16LE string
    -> "PreAlpha"  (length 0x29 = 41 = 41 bytes => 20 chars + wchar terminator)
  * then a STRING TABLE of UTF-16LE strings, each preceded by 4 bytes
  * interned references use delimiters: '!' (type), '"' (style), ',' (xmlns)

This tool does not assert a complete parse.  Its job is to emit an ordered,
offset-labelled token stream so the record structure can be read off directly.
"""
import os
import struct
import sys

SAMPLES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "samples", "xaml")


def read(path):
    with open(path, "rb") as fh:
        return fh.read()


def read7bit(buf, pos):
    """BinaryReader-style 7-bit-encoded integer (used for some lengths)."""
    value = 0
    shift = 0
    start = pos
    while pos < len(buf):
        b = buf[pos]
        pos += 1
        value |= (b & 0x7F) << shift
        if not (b & 0x80):
            return value, pos
        shift += 7
    return None, start


def try_utf16(buf, pos, nbytes):
    """Decode nbytes of UTF-16LE, trimming a trailing NUL. Returns text/None."""
    if nbytes < 0 or pos + nbytes > len(buf) or nbytes % 2:
        return None
    raw = buf[pos:pos + nbytes]
    try:
        text = raw.decode("utf-16-le")
    except UnicodeDecodeError:
        return None
    return text.rstrip("\x00")


def try_ascii(buf, pos, nbytes):
    if nbytes < 0 or pos + nbytes > len(buf):
        return None
    raw = buf[pos:pos + nbytes]
    if any(b < 0x20 or b >= 0x7F for b in raw):
        return None
    return raw.decode("ascii")


def classify_utf16(buf, pos):
    """Detect a 4-byte-length-prefixed UTF-16LE string at pos."""
    if pos + 4 > len(buf):
        return None
    n = struct.unpack_from("<I", buf, pos)[0]
    if n == 0 or n > 4096 or n % 2:
        return None
    text = try_utf16(buf, pos + 4, n)
    if text is None or len(text) < 2:
        return None
    # require it to look like a string (mostly printable)
    if sum(1 for c in text if c.isprintable()) < len(text) - 1:
        return None
    return (pos + 4 + n, text)


def classify_ascii7(buf, pos):
    """Detect a 7-bit-length-prefixed ASCII string at pos."""
    n, after = read7bit(buf, pos)
    if n is None or n == 0 or n > 4096:
        return None
    text = try_ascii(buf, after, n)
    if text is None or len(text) < 2:
        return None
    return (after + n, text)


def walk(buf, verbose=True, limit=None):
    """Tokenize greedily; report every token with its offset and the raw bytes
    between tokens (which is where the record structure lives)."""
    pos = 0
    tokens = []
    gaps = []
    while pos < len(buf):
        t16 = classify_utf16(buf, pos)
        ta = classify_ascii7(buf, pos)
        best = None
        if t16 and ta:
            # prefer the longer match; strings dominate this format
            best = t16 if (t16[0] >= ta[0]) else ta
            kind = "utf16" if t16[0] >= ta[0] else "ascii7"
        elif t16:
            best, kind = t16, "utf16"
        elif ta:
            best, kind = ta, "ascii7"
        if best is None:
            pos += 1
            continue
        end, text = best
        tokens.append((pos, kind, text, end - pos))
        pos = end
    return tokens


def dump(path, limit=64):
    buf = read(path)
    print("%s (%d bytes)" % (os.path.basename(path), len(buf)))
    toks = walk(buf)
    print("tokens: %d" % len(toks))
    prev_end = 0
    for i, (off, kind, text, tlen) in enumerate(toks):
        if i >= limit:
            print("  ... (%d more)" % (len(toks) - i))
            break
        gap = buf[prev_end:off]
        gaphex = gap.hex(" ") if gap else "-"
        print("  [%5d] %-6s len=%-4d %r" % (off, kind, tlen, text[:70]))
        print("           gap(%d) before: %s" % (len(gap), gaphex[:90]))
        prev_end = off + tlen
    tail = buf[prev_end:]
    print("  tail (%d): %s" % (len(tail), tail.hex(" ")[:120]))


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    if argv[1] == "dump":
        dump(argv[2], limit=int(argv[3]) if len(argv) > 3 else 40)
        return 0
    if argv[1] == "all":
        files = []
        for dp, _dn, fns in os.walk(SAMPLES):
            for n in sorted(fns):
                if n.lower().endswith(".baml"):
                    files.append(os.path.join(dp, n))
        for p in files:
            buf = read(p)
            toks = walk(buf)
            kinds = {}
            for _o, k, _t, _l in toks:
                kinds[k] = kinds.get(k, 0) + 1
            print("%-46s %6d bytes  %3d tokens  %s" % (
                os.path.basename(p), len(buf), len(toks), kinds))
        return 0
    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
