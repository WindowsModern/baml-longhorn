# baml-longhorn

**A BAML (compiled XAML) parser and decompiler for Windows Longhorn / pre-release Avalon.**

[中文文档](docs/zh-CN/README.md) · [Completion report](COMPLETION.md) · [Format documentation](docs/en/)

---

## This project was completed by an AI agent

**Every line of this project was written by an AI agent: DeepSeek Harness, running the
`deepseek-flash` model.**

Nothing here is a port of existing code. The record layouts were recovered by reading
decompiled Microsoft assemblies side by side and confirming each conclusion against real
BAML bytes — **two mutually exclusive record framings** and **eight generation profiles**,
with **five payload differences** separating build 4093 from 4074 on the wire. The result is
validated against **241 real BAML files** with **100% clean decompilation on every set**.

See [`COMPLETION.md`](COMPLETION.md) for the full statement of what was completed, what was
verified, and what remains unverified.

---

## What it does

Longhorn-era Avalon compiled its markup into **BAML** before the format settled. This tool
reads those streams and turns them back into XAML text.

It handles **two framings** and **eight generation profiles**:

| framing | generations | record shape |
|---|---|---|
| **SHORT** | 4074, 4083, 4093 | `[int16 type]`, then `[int32 size]` on variable-sized records |
| **LONG** | 3683, 3718, 4015, 4033, 4039, 4042 | `[int64 size][int16 type]` |

Generations are told apart by **structure**, never by assembly version string — the 4074 and
4093 binaries report identical file versions yet declare different record enums.

## Verified results

| corpus | files | outcome |
|---|---|---|
| `samples/xaml` (4074) | 103 | **103 / 103** decompiled complete |
| `samples/wcp4093` (4093) | 133 | **133 / 133** decompiled complete |
| `samples/extracted-4093` | 5 | **5 / 5** decompiled complete |
| `example.baml` (3683, LONG) | 1 | complete; tags balanced |
| `481.baml` (4015, LONG) | 1 | complete; tags balanced; 2,149 records |

Attribute value decoding reaches **99.98%** on the 4074 corpus (5,660 / 5,661) and **99.99%**
on the 4093 corpus (6,802 / 6,803). The single remainder in each is a known false positive:
a BAML string that literally reads `23 17`.

## Front ends

### GUI — `baml-gui.exe`

```
baml-gui.exe [optional-folder]
```

A folder tree and file list on the left, one tab per view on the right:

| tab | shows |
|---|---|
| **XAML** | the decompiler output — the reverse of compilation |
| **Records** | every record with offset, size and payload fields in declaration order |
| **Tree** | the reconstructed markup tree with resolved type names |
| **Tables** | the assembly / type / attribute interning tables and namespace mappings |
| **Summary** | dialect, confidence, size, parse completeness, record histogram |
| **Recon** | string tokens plus the raw bytes between them |

Opening a folder labels each file with its detected generation and confidence, which is the
quickest way to see whether a stream is 4074, 4093 or one of the LONG-lineage builds. Views
render on first visit and cache, so browsing a 51 KB stream stays responsive.

`Ctrl+F` searches the visible view (`Enter`/`F3` forward, `Shift+Enter`/`Shift+F3` back, both
wrapping, with an `n/total` counter) — necessary because a 51 KB stream decodes to far more
text than fits on screen.

`File > Save XAML as…` writes the current document. `File > Export all XAML…` decompiles every
`.baml` under the selected folder, mirroring the source layout; files the reader refuses are
counted as skipped rather than written as empty output.

### CLI — `baml.exe`

```
baml xaml <file>          decompile to XAML text
baml records <file>       decode and dump every record with offsets
baml tree <file>          reconstruct and print the markup tree
baml tables <file>        print the assembly/type/attribute interning tables
baml detect <file|dir>    report the detected dialect and confidence
baml stats <file|dir>     record histogram and dialect summary
baml recon <file>         reconnaissance dump (string tokens + inter-token gaps)
```

Both front ends render through one facade, `BamlFileView`, so they cannot drift apart in what
they show or how they report failure.

## Building

Visual Studio 2015 or MSBuild 14.0, .NET Framework 4.5, C# 6. No NuGet dependencies.

```
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" BamlLonghorn.sln /t:Rebuild /p:Configuration=Release
```

Outputs:

```
src\BamlLonghorn\bin\Release\BamlLonghorn.dll
src\BamlLonghorn.Cli\bin\Release\baml.exe
src\BamlLonghorn.Gui\bin\Release\baml-gui.exe
```

## Layout

```
BamlLonghorn.sln
src/
  BamlLonghorn/            core: profiles, walker, tree builders, XAML writer
  BamlLonghorn.Cli/        baml.exe
  BamlLonghorn.Gui/        baml-gui.exe (WinForms)
docs/                      format documentation
docs/zh-CN/                the same, in Simplified Chinese
samples/                   BAML corpora used for validation
tools/                     extractors for pulling BAML out of Microsoft manifests
```

Adding a generation is a table in `RecordProfile`, not a parser: framing, code list and
sizing classification travel together, and `BamlRecordWalker` dispatches payloads by record
**name**, so one reader serves enums whose numbering differs.

## How generations are told apart

The feature identifier `"PreAlpha"` is useless as a discriminator — 4074 and 4093 both write
it. What separates them is the `FormatVersion` tuple, `(0, 0)` versus `(0, 1)`.

`BamlDetector.MinConfidence` is therefore **100**, so a partial match is a **refusal** rather
than a confident misparse. Without that check, a 4093 stream fed to the 4074 reader survives
several records and then dies on a code that is legal in one enum and not the other — for
example `type 33`, which is `ResourceInfo` in 4093 but `LastRecordType` in 4074.

The LONG lineage carries **no FormatVersion at all**, so it is detected by shape: an `int64`
reading as a sane record length, followed by an `int16` that is `StartDocument`. The two
framings are mutually exclusive, since one reads a type field where the other reads the low
half of a size.

### A profile is not always uniquely determined

`example.baml` walks cleanly under all six LONG profiles, because it only uses codes that
existed in 3683. The bytes genuinely cannot say which build wrote it, so the reader reports
the **oldest** profile that walks cleanly — the tightest claim the data supports. Reporting
the newest would silently accept codes the document never used. A document narrows the
profile only when it needs a later code, which is why `481.baml` resolves to 4015 while
`example.baml` stays at 3683.

## Verifying the decompiler against a source file

The archive containing `example.baml` also contains the `example.xaml` it was compiled from:

```xml
<!-- decompiled from example.baml -->
<Application1.Example xmlns="using:System;System.Windows;System.Windows.Controls"
                      Background="LightBlue">
  <System.Windows.Controls.Button Height="100px" ID="__El2__">Clicky</…>
  <System.Windows.Controls.TextPanel Foreground="White" FontFamily="Trebuchet MS"
                                     FontSize="72pt" ID="TextArea">Hello, world!</…>
</Application1.Example>
```

```xml
<!-- example.xaml, the source -->
<FlowPanel xmlns="using:…" xmlns:def="Definition" def:Language="C#" Background="LightBlue">
  <Button Height="100px" Click="handleClick">Clicky</Button>
  <TextPanel Foreground="White" FontFamily="Trebuchet MS" FontSize="72pt" ID="TextArea">…
```

Three differences, and all three are **correct BAML semantics rather than parser errors**:

1. **Root name `Application1.Example`, not `FlowPanel`.** The BAML's `TypeInfo id=0` really is
   `Application1.Example`: `ac` compiled the markup into a generated class deriving from
   `FlowPanel`, and the root element points at that class.
2. **`ID="__El2__"` on the Button.** That string is in the BAML — the compiler's auto-assigned
   id, which the source never mentions.
3. **`Click="handleClick"` is absent.** The compiler moved the handler into `example.dll`, so
   the event no longer appears in the compiled form.

The decompiler is faithful to the bytes; the differences are what compilation did.

## Sample sets

| folder | files | source |
|---|---|---|
| `samples/xaml` | 103 | `4074\Microsoft.Windows.WCPClient\Microsoft.Windows.WCPClient.g.resx` |
| `samples/wcp4093` | 133 | `4093\Microsoft.Windows.WCPClient\Microsoft.Windows.WCPClient.g.resx` |
| `samples/extracted-4093` | 5 | `4093\PresentationFramework` + `PresentationUI` `.g.resx` |
| `samples/xaml-3683` | 32 | `3683\Microsoft.Windows.Client\LocalResources.resx` — **source XAML text, not BAML** |
| `samples/reference` | 5 | the `4047 BAML` collection |

`samples/xaml` and `samples/wcp4093` share **101 filenames**, so the same UI can be compared
across two generations. See [`docs/CORPORA.md`](docs/en/CORPORA.md) for how each set was
extracted and what was ruled out.

## What is not verified

Stated plainly, because a decompiler that overstates its coverage is worse than one that
declares its limits.

* **Builds 3718, 4033, 4039 and 4042 have no sample.** Their profiles are defined from
  decompiled enums and the LONG writer is generic across the lineage, but nothing has
  exercised them against real bytes. This was established twice — a `.baml` resource-name
  scan of every assembly in each build, and a manifest survey — and confirmed from a second
  angle once more. It is a gap in available material, not in the implementation: the profile
  table and the writer need no changes to consume such a file.
* **The 4093 corpus is decompiled but not yet source-compared**, because no matching source
  XAML for it was available.
* **`GenericAttribute` renders as `{uri}local`.** Valid XAML and lossless, but a real writer
  would have declared a prefix. It appears in **zero** of the ~350 samples, so any
  prefix-assignment scheme would be unverifiable — and an unverifiable change to naming is
  worse than a documented, valid fallback.
* **Collection properties** (`ClrArrayProperty`, `IListProperty`, `IDictionaryProperty`) are
  handled, but no sample contains them, so that handling is preventive rather than tested.

## Licence and provenance

The code in this repository is released under the [MIT Licence](LICENSE).

The BAML samples under `samples/` are extracted, unmodified, from Microsoft Longhorn
pre-release builds and remain the property of Microsoft. They are included only as
format-discrimination fixtures and test data; they are not covered by the MIT grant. This
project is an independent interoperability tool for abandoned pre-release software and is
not affiliated with or endorsed by Microsoft.
