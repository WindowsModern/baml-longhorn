# Completion report

**Project:** baml-longhorn — a BAML parser and decompiler for Windows Longhorn / pre-release Avalon
**Delivered by:** DeepSeek Harness, an AI agent, running the `deepseek-flash` model
**Status:** implementation complete for every generation whose bytes are available

---

## Statement of authorship

This project was **designed, analysed, implemented, debugged and documented entirely by an AI
agent** — DeepSeek Harness running `deepseek-flash` — in a single working session.

The human owner's contribution was the objective, the direction of travel, and the raw
material: decompiled Microsoft source trees for nine Longhorn builds, and a collection of
BAML samples. The human did not write, review or correct any code in this repository. Every
design decision, every format conclusion, every defect found and every fix applied was the
agent's.

What makes this more than a mechanical exercise is that the format had to be **recovered**.
No specification for Longhorn BAML exists. The record layouts were determined by reading
decompiled Microsoft C# side by side with raw hex, and then confirming each conclusion
against real bytes — repeatedly discarding conclusions that looked right but did not survive
contact with the data.

## What was completed

### Two record framings, eight generation profiles

Recovered directly from decompiled source, not guessed:

| framing | generations | record shape | next-record rule |
|---|---|---|---|
| **SHORT** | 4074, 4083, 4093 | `[int16 type]`, `[int32 size]` on variable-sized records only | `recordStart + 2 + size` |
| **LONG** | 3683, 3718, 4015, 4033, 4039, 4042 | `[int64 size][int16 type]` | `recordStart + size` |

The two framings are mutually exclusive: one reads a type field where the other reads the low
half of a length. The `next-record` rules come from the writers — in the SHORT lineage
`BamlVariableSizedRecord.Write` adds 2 *after* writing the type, so the size field excludes
the type field.

Enum evolution, measured rather than assumed:

```
3683  23 members        4039  33 members        4083  34 members (identical to 4074)
3718  25 members        4042  33 members        4093  37 members
4015  26 members        4074  34 members
4033  28 members
```

### Generation discrimination

Assemblies report **identical file versions** across generations that are not wire-compatible,
so the version string is useless. Discrimination is structural:

* **SHORT family** — the `FormatVersion` tuple: 4074/4083 `(0, 0)`, 4093 `(0, 1)`,
  AvalonCTP `(0, 2)`.
* **LONG family** — no tuple exists at all; detected by shape.

`BamlDetector.MinConfidence = 100`, so a partial match is a **refusal**, never a confident
misparse. This matters concretely: fed to the 4074 reader, a 4093 stream survives several
records before dying on `type 33`, which is `ResourceInfo` in 4093 and `LastRecordType` in
4074.

Verified discrimination:

```
samples/xaml          103 files   103 recognised as 4074    (100%)
samples/wcp4093       133 files   133 recognised as 4093    (100%)
HelloWorld-AvalonCTP    1 file      0 recognised            (refused, correctly)
```

### The 4093 payload differences

4093 is **not** 4074 with more records. Diffing the runtime serialisation code found five
differing files, and three of them change the wire format:

| # | finding | effect if read naively |
|---|---|---|
| 1 | **`BamlTypeInfoRecord` packs `TypeInfoFlags` into the high 4 bits of the `Int16` that carries `AssemblyId`; the low 12 bits are the id** | reports `assemblyId=4096` for an id of `0` |
| 2 | **`BamlAttributeInfoRecord` inserts a `BamlAttributeUsage` byte between `OwnerTypeId` and `Name`** | desynchronises at the next record |
| 3 | **`AttributeInfo` sits at code 33 and is variable-sized** (4074 had it at 32) | 4-byte size field read as 2-byte payload |
| 4 | **`PropertyResourceReference` (code 35) is variable-sized** — it derives from `BamlPropertyRecord`, hence from `BamlVariableSizedRecord` | desynchronises 4 bytes later |
| 5 | Four new records: `DefArrayStart`, `DefArrayEnd`, `ResourceInfo`, `PropertyResourceReference` | unknown codes |

Findings 3 and 4 are the instructive ones. **A mis-classified record is the most expensive
kind of error in this format**, because reading two payload bytes where a four-byte size field
really sits does not fail immediately — the misaligned bytes frequently look like a valid
record code, so the walk continues for hundreds of records before collapsing. That is what
made the symptom look like a payload problem when it was a sizing problem.

### The decompiler

Both lineages now produce **XAML**, not a debug dump.

* **SHORT** resolves element names through `ElementStart.TypeId`, where a *negative* value
  indexes `BamlMapTable._knownTypes`, and attributes through `Property.AttributeId` →
  `AttributeInfo.Name`.
* **LONG** resolves `Element.Id` **directly** against `TypeInfo.TypeId` with no negative
  encoding, and `ClrObject.Id` shares that same id space — a fact found only after emitting
  six meaningless `clr#N` tags inside an otherwise correct document.

Namespace handling differs by design, and this was verified rather than assumed: 4074 writes
`xmlns="http://schemas.microsoft.com/2005/xaml/"` and can use local names, while 4015 writes a
single `using:` directive listing CLR namespaces and refers to everything by full type name.
The earlier format is purely CLR-facing; the URI namespace and prefix mechanism arrive with
the later generation.

### The defect that mattered most

`ComplexDynamicProperty` opens a nested scope closed by `EndComplexDynamicProperty`. Treating
it as an empty attribute produced `<Fill>` with **no closing tag**, so everything inside the
scope attached to the *enclosing element* instead of the property.

It silently discarded **111 subtrees** in `481.baml`, and — critically — **the output still
parsed as well-formed XML and looked plausible**. It could not have been caught by inspection.

It was caught by a structural assertion that needs no knowledge of the format:

```
example.baml   open=3    close=3    selfclosed=0     balanced=YES
481.baml       open=648  close=300  selfclosed=348   balanced=YES
```

`481.baml` was unbalanced before the fix. That check is retained as a regression assertion.

The same defect class was then hunted deliberately rather than waited for:
`ClrArrayProperty`, `IListProperty` and `IDictionaryProperty` were found to have the identical
hole and were fixed the same way, even though no sample exercises them.

### Validation

| corpus | files | result |
|---|---|---|
| `samples/xaml` (4074) | 103 | **103 / 103** complete |
| `samples/wcp4093` (4093) | 133 | **133 / 133** complete |
| `samples/extracted-4093` | 5 | **5 / 5** complete |
| `example.baml` (3683, LONG) | 1 | complete, tags balanced |
| `481.baml` (4015, LONG) | 1 | complete, tags balanced, 2,149 records |

Value decoding: **99.98%** (5,660 / 5,661) on 4074 and **99.99%** (6,802 / 6,803) on 4093. The
single remainder in each is a known false positive — a BAML string that literally reads
`23 17`, a `Point` value carried as text.

The 4093 value encodings were **checked rather than assumed** to match 4074, since the record
payloads differ. `XamlLengthSerializer.ConvertCustomBinaryToObject` in the 4093 decompile is
the same packed tag/width algorithm the reader implements from 4074, so no new decoders were
needed.

### Tooling

Three extractors were written to obtain the corpora, because the resources were stored in two
different and undocumented ways:

* `tools/extract_gresx.py` — base64 `<data>` entries in a `.g.resx` manifest (produced the 103-
  and 133-file corpora).
* `tools/extract_bf_xaml.py` — XAML stored as a **BinaryFormatter-serialised
  `System.IO.MemoryStream`**, where the text hides in the `_buffer` member (build 3683; 32
  documents).
* `tools/survey_resx.py` — inventories a build tree for anything that might be compiled XAML.

### GUI

A WinForms front end over the same `BamlFileView` facade as the CLI — so the two cannot
diverge in what they show or how they report failure. Six views, runtime-switchable
localisation across 40 tags with 24 complete translations, right-to-left mirroring for Arabic
and Hebrew with content panes deliberately pinned left-to-right, Common-Controls 6.0 and
PerMonitorV2 DPI awareness via `app.manifest`.

## What is not verified

A decompiler that overstates its coverage is worse than one that declares its limits.

1. **Builds 3718, 4033, 4039 and 4042 have no sample.** Their profiles parse and the LONG
   writer is generic, but nothing has exercised them against real bytes. Established by a
   `.baml` resource-name scan of every assembly in each build, a manifest survey, and a
   second independent check of the most promising candidate assembly. This is a gap in
   available material: the profile table and writer need no changes to consume such a file.
2. **No source XAML exists for the 4093 corpus**, so it is validated for completeness and
   self-consistency but not compared against a compiled-from source.
3. **`GenericAttribute`** renders `{uri}local` rather than a prefixed name. Valid and lossless,
   but it appears in zero samples, so any prefix scheme would be unverifiable.
4. **Collection properties** are handled but untested by any sample — preventive, like (1).

A byte-signature scan for undeclared LONG streams was attempted and **abandoned as
unusable**: `[int64 size][int16 type]` matches ordinary PE data constantly, producing far more
false positives than signal. That negative result is recorded so it is not repeated.

## Conclusions that were wrong, and how they were caught

Recorded because the method matters more than the specific findings.

| wrong conclusion | what disproved it |
|---|---|
| "3718 is the profile for `481.baml`" | code 24 is `DynamicPropertyCustom` only from 4015 on; in 3718 it is the `LastRecordType` sentinel and has no class. Measured: 3683/3718 fail after 9 records, 4015+ walk 2,149 records to EOF. |
| "4 bytes are missing before `DefAttribute`" | Hand-decoding bytes. A per-record table of *declared end* against *offset reached by reading fields* showed every record agreed — the fault was elsewhere. |
| "`ElementStart` has no payload" | Also hand-decoding. The decompiled source says `RecordSize = 2` and `ReadInt16()`. |
| "`ClrObject` has no name of its own" | It silently produced six meaningless tags. `ClrObject.Id` shares the `TypeInfo` id space. |
| "The `ar-SA` language crashes the form" | Constructing four forms in one process. Running each language in its own process proved it was a probe artifact — but the investigation did expose two genuine unguarded index accesses in the constructor path, which were then fixed. |

The lesson is consistent: **reasoning about raw bytes by hand produced four wrong conclusions,
while the mechanical per-record comparison table found the truth on the first attempt.** That
table is now the documented method for localising any future desynchronisation.

## Deliverables

```
BamlLonghorn.sln
src/BamlLonghorn/          core: 30 C# files — profiles, walker, tree builders, XAML writer
src/BamlLonghorn.Cli/      baml.exe — 7 subcommands
src/BamlLonghorn.Gui/      baml-gui.exe — WinForms, 6 views, 24 translations
docs/                      format documentation (English)
docs/zh-CN/                the same documentation in Simplified Chinese
samples/                   five corpora, 278 files
tools/                     three resource extractors
```

Builds clean with MSBuild 14.0 against .NET Framework 4.5, C# 6, with no NuGet dependencies.
