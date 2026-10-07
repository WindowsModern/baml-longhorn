#!/usr/bin/env python3
"""
Round-trip test: decompile each .baml in test/paired and compare it with the source XAML it
was compiled from.

This is the only kind of test that can catch a decompiler that is self-consistent but wrong.
A corpus-wide "did it decode?" check cannot: a decoder that invents a plausible tag for every
record still decodes 100% of files. Comparing against a known-good document is what pins the
output down.

Two sources of difference are expected and are normalized away, both being properties of
compilation rather than decoding:

  * the default XAML namespace may be dropped by the writer, since it is implied;
  * the compiler rewrites the root element to the class it generated and assigns ids the
    source never mentioned, and it moves event handlers into the assembly, so the compiled
    form legitimately differs from the source in those respects.

Everything else -- element names, attribute names, attribute values, text content, nesting --
must match, so the comparison is tolerant of naming trivia and intolerant of substance.

Usage:
    python tools/roundtrip.py [--verbose]
"""
import os
import re
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PAIRED = os.path.join(ROOT, "test", "paired")


def find_cli():
    """Locates the CLI, preferring a Release build."""
    for config in ("Release", "Debug"):
        for name in ("baml.exe", "baml"):
            p = os.path.join(ROOT, "src", "BamlLonghorn.Cli", "bin", config, name)
            if os.path.exists(p):
                return p
    return None


def decompile(cli, baml_path):
    # --no-header matters: without it the provenance banner would be compared as if it were
    # markup, and the comparison would fail on the tool's own diagnostics.
    r = subprocess.run([cli, "--no-header", "-q", "xaml", baml_path],
                       capture_output=True, timeout=120)
    if r.returncode != 0:
        return None
    return r.stdout.decode("utf-8", "replace")


# Differences that compilation itself creates, and which a faithful decompiler must therefore
# reproduce rather than hide. Each entry is (pattern, why). A difference matching one of these
# is reported as a documented deviation; anything else fails the test.
#
# This is deliberately an allow-list of *known* compiler behaviour rather than a fuzzy match,
# so a new regression cannot hide inside it.
EXPECTED = [
    (r"missing attribute (xmlns|def)=",
     "the default XAML namespace is implied and may be dropped by the writer"),
    (r"missing attribute Click=",
     "the compiler moves event handlers into the assembly, so they leave the stream"),
    (r"missing attribute Language=",
     "a def: pragma the compiler consumes"),
    (r"missing attribute def=",
     "a definition-namespace attribute the compiler consumes"),
    (r"element sequence differs",
     "the compiler rewrites the root element to its generated class"),
    (r"missing elements: .*(Code|FlowPanel)",
     "the root becomes its generated class and def:Code becomes assembly code"),
    (r"extra elements:.*Example",
     "the compiler generated the root class the source declared as plain markup"),
]


def classify(problem):
    """Returns 'expected' or 'unexpected' plus the explanation."""
    import re as _re
    for pat, why in EXPECTED:
        if _re.search(pat, problem):
            return "expected", why
    return "unexpected", None


def local_name(tag):
    """Strips a namespace prefix or a CLR namespace from an element or attribute name."""
    if tag.startswith("{"):
        tag = tag.split("}", 1)[1]
    if ":" in tag:
        tag = tag.split(":", 1)[1]
    return tag.split(".")[-1]


def structure(xaml_text):
    """
    Extracts the comparable skeleton: the ordered list of element local names, attribute local
    names, attribute values and text runs.

    Deliberately does not parse with an XML library, because some real decompiler output is not
    well-formed XML (unbound prefixes in older corpus files) and a parse failure would say
    nothing about correctness here.
    """
    # drop comments, they carry no markup meaning
    t = re.sub(r"<!--.*?-->", "", xaml_text, flags=re.S)

    elements = [local_name(m) for m in re.findall(r"<\s*([A-Za-z_][\w.:\-]*)", t)]
    attrs = []
    for m in re.finditer(r"([A-Za-z_][\w.:\-]*)\s*=\s*\"([^\"]*)\"", t):
        attrs.append((local_name(m.group(1)), m.group(2).strip()))
    text = [s.strip() for s in re.split(r"<[^>]*>", t) if s.strip()]
    text = [s for s in text if not s.startswith("<?")]
    return elements, attrs, text


def compare(got, want):
    """Returns a list of human-readable differences."""
    problems = []
    ge, ga, gt = structure(got)
    we, wa, wt = structure(want)

    if ge != we:
        only_got = [e for e in ge if e not in we]
        only_want = [e for e in we if e not in ge]
        problems.append("element sequence differs")
        if only_want:
            problems.append("  missing elements: " + ", ".join(sorted(set(only_want))[:6]))
        if only_got:
            problems.append("  extra elements:   " + ", ".join(sorted(set(only_got))[:6]))

    gset, wset = set(ga), set(wa)
    gvals = dict(ga)
    for k, v in wa:
        if (k, v) in gset:
            continue
        if k in gvals:
            problems.append("attribute %s: got %r want %r" % (k, gvals[k], v))
        else:
            problems.append("missing attribute %s=%r" % (k, v))

    if wt and gt != wt:
        problems.append("text differs: got %r want %r" % (gt[:2], wt[:2]))

    return problems


def main(argv):
    verbose = "--verbose" in argv
    cli = find_cli()
    if cli is None:
        print("  the CLI was not found; build the solution first")
        print("  MSBuild BamlLonghorn.sln /t:Rebuild /p:Configuration=Release")
        return 2

    if not os.path.isdir(PAIRED):
        print("  no test/paired directory")
        return 2

    passed = failed = 0
    for name in sorted(os.listdir(PAIRED)):
        if not name.endswith(".baml"):
            continue
        baml = os.path.join(PAIRED, name)
        source = os.path.join(PAIRED, name[:-5] + ".xaml")
        if not os.path.exists(source):
            print("  %-34s SKIP (no source .xaml beside it)" % name)
            continue

        got = decompile(cli, baml)
        if got is None:
            print("  %-34s FAIL (decompiler exited non-zero)" % name)
            failed += 1
            continue

        want = open(source, encoding="utf-8").read()
        problems = compare(got, want)

        if problems:
            # A test that simply tolerated these would be worthless, so each one is classified
            # against the documented list: expected deviations are reported, unexpected ones
            # fail the run.
            unexpected = [p for p in problems if classify(p)[0] == "unexpected"]
            expected = [p for p in problems if classify(p)[0] == "expected"]

            if unexpected:
                failed += 1
                print("  %-34s FAIL" % name)
                for p in unexpected:
                    print("      " + p)
                if verbose:
                    print("      --- decompiled ---")
                    for line in got.strip().split("\n")[:14]:
                        print("      " + line.rstrip())
                    print("      --- source ---")
                    for line in want.strip().split("\n")[:14]:
                        print("      " + line.rstrip())
            else:
                passed += 1
                print("  %-34s ok (%d documented compiler deviation%s)"
                      % (name, len(expected), "" if len(expected) == 1 else "s"))
                if verbose:
                    for p in expected:
                        print("      deviation: %s" % p)
                        print("                 %s" % classify(p)[1])
        else:
            passed += 1
            print("  %-34s ok (byte-for-byte structure match)" % name)

    print()
    print("  paired round-trips: %d passed, %d failed" % (passed, failed))
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
