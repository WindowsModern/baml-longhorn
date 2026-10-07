import os
import re

# Enumerate the decompiled WPF 4.0 sources and emit a fallback list of type names for
# WpfTypeIndex. The list exists so the converter still works where reflection over the
# presentation assemblies is unavailable; it is generated from real source rather than typed by
# hand, which is why this script exists at all.
#
# The Decompiled directory name contains a zero-width non-joiner, so it is located by pattern
# rather than by a literal path.

ROOT = r"F:\AI Agent WorkSpace\DeepSeek Harness\BamlLonghorn"
decompiled = None
for name in os.listdir(ROOT):
    if name.startswith("Decompile"):
        decompiled = os.path.join(ROOT, name)
        break
if decompiled is None:
    raise SystemExit("no Decompiled directory under " + ROOT)

fw = os.path.join(decompiled, "Microsoft.NET", "Framework", "v4.0.30319")
roots = [os.path.join(fw, a) for a in ("PresentationFramework", "PresentationCore", "WindowsBase")]
for r in roots:
    print("  %-24s exists=%s" % (r.split(os.sep)[-1], os.path.isdir(r)))

DECL = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:public|internal)\s+'
    r'(?:sealed\s+|abstract\s+|static\s+|partial\s+|unsafe\s+)*'
    r'(class|struct|enum|interface)\s+(\w+)',
    re.M)
NS = re.compile(r'^\s*namespace\s+([\w\.]+)', re.M)

shorts = set()
fulls = set()
files = 0
for root in roots:
    if not os.path.isdir(root):
        continue
    for dirpath, _dirs, names in os.walk(root):
        for n in names:
            if not n.endswith(".cs"):
                continue
            files += 1
            try:
                text = open(os.path.join(dirpath, n), encoding="utf-8", errors="replace").read()
            except Exception:
                continue
            m = NS.search(text)
            nsname = m.group(1) if m else None
            for d in DECL.finditer(text):
                name = d.group(2)
                shorts.add(name)
                if nsname:
                    fulls.add(nsname + "." + name)

print("  files scanned: %d" % files)
print("  short names  : %d" % len(shorts))
print("  full names   : %d" % len(fulls))

out = os.path.join(ROOT, "Temp", "fallback.txt")
with open(out, "w", encoding="utf-8", newline="\n") as fh:
    fh.write("SHORT\n")
    for s in sorted(shorts):
        fh.write(s + "\n")
    fh.write("FULL\n")
    for s in sorted(fulls):
        fh.write(s + "\n")
print("  wrote " + out)
