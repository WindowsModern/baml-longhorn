# Test content

Only material that can be published: compiled BAML and the markup it came from. There is no
decompiled Microsoft source here, and nothing under `samples/` is a decompiler product either.

## `paired/` — round-trip fixtures

Each `.baml` has the `.xaml` it was **compiled from** sitting beside it. That pairing is what
makes these worth having: a decoder that invents a plausible tag for every record still
decodes 100% of a corpus, so a "did it decode?" check cannot catch it. Comparing against a
known-good document can.

| fixture | framing | source of the pair |
|---|---|---|
| `example-3683` | LONG, 3683 profile | the `example-mark-up-files-and-binaries` set |
| `HelloWorld-4074` | SHORT, 4074 | the `4047 BAML` collection |

A 4015-profile pair (`481.baml`, 2,149 records) exercises the CLR and complex-property
machinery, but no source markup for it is available, so it cannot be compared and is not here.
`GENERATION-STATUS.md` records what is known about it.

## `discrimination/` — inputs that must be refused

`HelloWorld-AvalonCTP-0.2` is an early Avalon CTP document. It must be **refused**, not
misparsed: `BamlDetector.MinConfidence` is 100, so a partial match returns no reader at all.

This matters for the ILSpy plugin specifically. If the plugin claimed everything ending in
`.baml` it would contend with the official WPF BAML plugin and produce wrong output for
released WPF assemblies. Refusing where it is not certain is what keeps the two independent.

## Running

```
python tools/roundtrip.py            # compare paired fixtures against their sources
python tools/roundtrip.py --verbose  # also explain each difference
```

The comparison is tolerant of naming trivia and intolerant of substance: element names,
attribute names, attribute values, text and nesting must agree. Differences are classified
against a list of **known compiler behaviour** in `tools/roundtrip.py`, and anything outside
that list fails the run. A fuzzy match would let a regression hide; an allow-list cannot.

### Documented compiler deviations

These are properties of compilation, so a faithful decompiler reproduces them rather than
hiding them:

* **the default XAML namespace is dropped.** `xmlns="http://schemas.microsoft.com/2005/xaml/"`
  is implied, so the writer may omit it.
* **the root element becomes its generated class.** `example.xaml` declares `<FlowPanel>`;
  `ac` compiled it into a generated class and the BAML root points at that class, so the
  decompiler correctly emits `Application1.Example`. This is not a naming bug — the BAML
  really does say so.
* **`def:Code` and `def:Language` disappear.** The compiler consumed them into the assembly.
* **`Click="handleClick"` disappears** for the same reason: event handlers leave the stream.
* **an id appears that the source never mentioned.** `ID="__El2__"` is the compiler's own
  auto-assigned id.

Every one of these is reported by the test rather than silently ignored, so the test says what
it is tolerating and why.
