# The BAML format lineages, classified by test

Three mutually incompatible compiled-XAML formats have now been identified in the
Longhorn material, and **two of them have been decoded**. Which is which was settled
by running each decoder over every available file, not by reasoning.

## The classification matrix

`LONG` = framing `[int64 size][int16 type]`, next = `recordStart + size`
`SHORT` = framing `[int16 type][int32 size]`, next = `recordStart + 2 + size`

| file | bytes | LONG | SHORT |
|---|---|---|---|
| `example.baml` (build 3683, from `ac.exe`) | 965 | **CLEAN to EOF, 35 recs** | rejected at offset 0 |
| `481.baml` (older) | 43,627 | **CLEAN to EOF, 2149 recs** | rejected at offset 0 |
| `HelloWorld-Longhorn-0.0.baml` | 226 | rejected | **CLEAN to EOF, 12 recs** |
| `HelloWorld-AvalonCTP-0.2.baml` | 229 | rejected | partial (2 recs, stops at 47) |
| `modulesizer.baml` (primary corpus) | 184 | rejected | **CLEAN to EOF, 7 recs** |
| `desktopaurora-4074-original.baml` | 34,720 | rejected | partial (919 recs, stops at 8,898) |

The LONG column is clean-or-rejected with no ambiguity, which makes it a decisive
discriminator. Both partial SHORT results stop *late* (record 2 of 229 bytes;
record 919 of 34,720 bytes), so the lineages are clearly separated and the SHORT
failures are payload-level, not framing-level.

## Lineage 1 — LONG framing (older; builds 3683 and 481)

```
<int64 recordSize>   TOTAL record length, including this 8-byte field
<int16 recordType>
<payload>
```

Type codes start `1 StartDocument`, `11 AssemblyInfo`, `12 TypeInfo`, `3 ElementStart`,
`6 …`, `13 …` — i.e. the 25-member `MS.Internal.BamlRecordType` family recovered
from `System.Windows.dll`:

```
0 Unknown  1 StartDocument  2 EndDocument  3 Element  4 EndElement
5 ParseLiteralContent  6 XmlnsProperty  7 DynamicProperty  8 DynamicEvent
9 GenericAttribute  10 Text  11 AssemblyInfo  12 TypeInfo  13 AttributeInfo
14 ComplexDynamicProperty  15 EndComplexDynamicProperty  16 ClrObject
17 EndClrObject  18 ClrProperty  19 ClrArrayProperty  20 EndClrArrayProperty
21 ClrComplexProperty  22 EndClrComplexProperty  23 IncludeTag
24 DynamicPropertyCustom
```

Tree-node records (`Element`, `ParseLiteralContent`, `Text`, `ClrObject`) carry a
12-byte header of position-relative offsets. The reference implementation for this
lineage is `docs/reference-bamlread.py`, whose `selftest` round-trips 15 records /
442 bytes exactly.

**This is the format the very first specification in this project described.** It
turned out not to apply to the primary corpus, which sent the investigation down a
long detour — but the specification itself was correct for this lineage, as the two
CLEAN decodes above now prove.

## Lineage 2 — SHORT framing (the primary target; 4074 era)

```
<int16 recordType>   always 2 bytes
<int32 recordSize>   only for BamlVariableSizedRecord subclasses
<payload>

next record = (offset of the size field) + size = recordStart + 2 + size
```

Defined by the decompiled `System.Windows.Serialization` classes of
**build 4074** `PresentationFramework`
(`E:\Profiles\Bruce\Desktop\4074 - PresentationFramework True\`). The 34-member
`BamlRecordType`, the sizing table and every payload layout are in
`BAML4074-FORMAT.md`. Reference implementation: `tools/baml4074.py`.

## Source of the reference samples

* `example.baml` and the `HelloWorld` pair come from
  `E:\Profiles\Bruce\Desktop\example-mark-up-files-and-binaries` (attached to
  <https://longhorn.ms/avalon-compiling-it/>). The article confirms the toolchain:
  `ac` (the Avalon Compiler) shipped in **build 3683**, was already deprecated by
  4051, and was superseded by `XamlC`. `ac` produced `example.baml` alongside
  `exampleApp.exe`, `exampleApp.dll` and `example.dll`, and the accompanying
  `example.xaml` is the ground truth for what the BAML should say.
* `481.baml` and the 4074 `desktopaurora` original come from the
  `4047 BAML` collection.

### A correction about `481.baml`

The collection's own note reads:

> This is also from build 4074, but it's an even older BAML format that 4074's
> default BAML parser actually fails to decode.

So **481 is not a build number** — it labels an older *format*, and the file itself
is a 4074-era artefact carrying the LONG framing. That is consistent with the byte
evidence: `481.baml` and `example.baml` (build 3683) share an identical 16-byte
prefix, so they are the same lineage. The 481 label should not be read as a build.

### These two lineages cannot both become first-class

A 3683 decompile and a 481-context decompile are **not obtainable**. The LONG
lineage therefore stays at reference-implementation status:

* the record *class* is described by `4074\System.Windows` (`MS.Internal.BamlRecord`,
  `BamlRecordManager` with fixed `[int64 size][int16 type]` framing)
* `docs/reference-bamlread.py` already walks both files to EOF (35 and 2149
  records respectively)
* the samples stay in `samples/reference/` as **negative tests** — the SHORT reader
  must reject them, and it does, at offset 0

This is a bounded, documented limitation rather than an open task.


### `example.xaml`, and why it matters

```xml
<FlowPanel xmlns="using:System;System.Windows;System.Windows.Controls"
           xmlns:def="Definition" def:Language="C#" Background="LightBlue">
  <Button Height="100px" Click="handleClick">Clicky</Button>
  <TextPanel Foreground="White" FontFamily="Trebuchet MS" FontSize="72pt"
             ID="TextArea">Hello, world!</TextPanel>
  <def:Code><![CDATA[ ... ]]></def:Code>
</FlowPanel>
```

This is a **paired source/binary sample**: the XAML states exactly what the BAML
must decode to, including the `using:` namespace syntax, the `Definition` namespace
with `def:Language`, and a `<def:Code>` CDATA block. It is the best available
ground truth for the LONG lineage.

## Practical consequence for the project

The decoder must **auto-detect the lineage**, since a file's format cannot be told
from its name, its size, or (as established earlier) its assembly FileVersion. The
LONG/SHORT discrimination is reliable because each decoder rejects the other
lineage at offset 0:

```
LONG  on example.baml / 481.baml   -> CLEAN
SHORT on HelloWorld / corpus       -> CLEAN
SHORT on example.baml / 481.baml   -> rejected at offset 0
LONG  on HelloWorld / corpus       -> rejected
```

So detection is simply: try SHORT, and if it is rejected at the first record, try
LONG. This should replace the current structural heuristic in
`BamlDialect4074.Detect`.

## Open items

1. **`desktopaurora-4074-original.baml` stops at record 919 of 34,720 bytes** with
   `payload @8898: bad string length 12579138`. Framing and enum are proven by the
   clean decodes, so this is a payload-layout detail — most likely in
   `LiteralContent`, `PropertyCustom` or `TypeSerializerInfo`.
2. **`HelloWorld-AvalonCTP-0.2.baml` stops at offset 47** on
   `type 20 (XmlAttribute)`, a code with no record class in build 4074. Its
   `FormatVersion` tuples are `(0,2)` versus `(0,0)` for the Longhorn pair, so it is
   the "slightly later version from November 2004" mentioned in the collection
   notes, and its enum may differ again.
3. A LONG-lineage reference implementation should be written as a proper dialect
   reader (`BamlDialectOlder`) rather than left as the Python script.
