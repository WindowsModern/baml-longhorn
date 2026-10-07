# The two BAML generations, and which assembly owns which

Status: **generation ownership settled by evidence.** The corpus is the
`PresentationFramework` generation. Two `System.Windows.dll` build trees are
the *other* generation. The remaining task is bounded and named at the end.

## The distinction, verified from three independent sources

### Generation A — `MSDotnetAvalon.Windows` / `MS.Internal` (the class-record reader)

Owned by `System.Windows.dll`; the two build trees available here report file version
6.0.4051.31026. That is not an identifier: the same file version also appears in the 4083
and 4093 trees, and the assembly version 6.0.3708.0 is identical in all seven build trees.
The build folder is the identifier, not the version.

Framing, from the reader's own behaviour: a record size is taken from the leading Int64 and
the record code from the Int16 that follows it.

```
Reading the next record:
  recordSize = Int64 read from the leading size field      -- fixed int64 width
  if recordSize < 0: reject the stream (error 1254)
  recordType = Int16 read from the type field that follows -- fixed int16 width
  ... the record's payload follows
```

and the writer mirrors it:

```
Writing a record, in order:
  Int64  recordSize
  Int16  recordType
  payload
```

25-member `MS.Internal.BamlRecordType`:
`Unknown StartDocument EndDocument Element EndElement ParseLiteralContent
XmlnsProperty DynamicProperty DynamicEvent GenericAttribute Text AssemblyInfo
TypeInfo AttributeInfo ComplexDynamicProperty EndComplexDynamicProperty ClrObject
EndClrObject ClrProperty ClrArrayProperty EndClrArrayProperty ClrComplexProperty
EndClrComplexProperty IncludeTag DynamicPropertyCustom LastRecordType`

### Generation B — `System.Windows.Serialization` (the corpus's actual format)

Owned by `PresentationFramework.dll`. Types read from its
metadata by `metadump` (the assembly will not execute-load on any modern runtime,
but its metadata is intact):

```
BamlRecord (abstract)              BamlVariableSizedRecord (abstract)
BamlDocumentStartRecord / End      BamlElementStartRecord / End
BamlPropertyRecord                 BamlStringValueRecord
BamlPropertyCustomRecord           BamlPropertyComplexStart/EndRecord
BamlPropertyArrayStart/EndRecord   BamlPropertyIListStart/EndRecord
BamlPropertyIDictionaryStart/EndRecord
BamlXmlnsPropertyRecord            BamlLiteralContentRecord
BamlTextRecord                     BamlRoutedEventRecord
BamlDefAttributeRecord             BamlIncludeTagRecord
BamlAssemblyInfoRecord             BamlTypeInfoRecord
BamlTypeInfoWithSerializerRecord   BamlAttributeInfoRecord
BamlPIMappingRecord
BamlMapTable / BamlMapTableConstants / BamlNodeInfo / BamlPropertyInfo
BamlObjectFactory / BamlReader / BamlRecordReader
BamlWriter / BamlRecordWriter / BamlRecordManager / BamlRecordType / BamlTreeBuilder
BamlNodeType
```

16-member `System.Windows.Serialization.BamlNodeType`:

```
None=0  StartDocument=1  EndDocument=2  StartElement=3  EndElement=4
Property=5  XmlnsProperty=6  StartComplexProperty=7  EndComplexProperty=8
LiteralContent=9  Text=10  RoutedEvent=11  Event=12  IncludeReference=13
DefAttribute=14  PIMapping=15
```

Record header, from the assembly's metadata:

```
Record header widths:
  RecordTypeFieldLength   Int32          -- width of the type field
  BamlWriterVersion       VersionTuple   -- the writer's version
  RecordSizeFieldLength   Int32          -- width of the size field, on variable-sized records
  recordSize              Int32          -- the record's own size, on variable-sized records
```

Both widths are static fields of the record classes, initialised at runtime rather than
fixed by a literal in the metadata tables.

**Both widths are variable**, which is precisely why every fixed-header and
fixed-length-chain hypothesis failed against the corpus.

## Evidence that the corpus is Generation B, not A

1. `tools/longframe_test.py` — scanning the first 96 bytes of all 103 samples for
   Generation A's `[int64 size][int16 type]` header: a plausible header appears in
   **1 of 103** files, and in the smallest sample there are **0** candidates
   anywhere in the file. Generation A framing is structurally impossible for the
   corpus.
2. Generation B's `BamlNodeType` names match the sample tags far better: the
   corpus shows tags `1d`, `1e`, `13`, `03`, `04` and a `def:` attribute plus
   doubled-slash xmlns values — consistent with Generation B's
   `DefAttribute`, `XmlnsProperty`, `PIMapping`, `IncludeReference`.
3. `docs/BUILD4074-FINDINGS.md` in this repo records the empirical byte-level
   work (document header at 0..27, per-string 1-byte length, three real strings at
   52/84/136 in the smallest sample).

## The two `System.Windows.dll` build trees supplied are BOTH Generation A

The two `System.Windows.dll` build trees available here are the same generation, and this is
visible in the binaries themselves rather than inferred:

| property | 4093 `System.Windows` tree | other `System.Windows` tree |
|---|---|---|
| AssemblyVersion | `6.0.3708.0` | `6.0.3708.0` |
| FileVersion | `6.0.4051.31026` | `6.0.4051.31026` |
| Generation A record codes present | yes | yes |

Both carry the Generation A record vocabulary and neither carries any of the records that
Generation B adds — `BamlNodeType`, `BamlPIMappingRecord`, `BamlDocumentStartRecord` and
`BamlElementStartRecord` are all absent from the streams of both.

Note that the version columns agree here but are **not** an identifier: `6.0.4051.31026` also
occurs in the 4083 tree, and `6.0.3708.0` is identical across all seven build trees examined.
What actually establishes that these two are the same generation is the record vocabulary they
define, not the version they report.

So a `PresentationFramework.dll` reader is what is still missing.

## Why PresentationFramework cannot simply be read or executed here

* It **will not load for execution** on .NET 9 or .NET Framework 4.8:
  `BadImageFormatException 0x8013110E` ("file is corrupted") — the pre-release
  assembly references cannot be resolved by a modern loader.
* Its **metadata is intact and readable**, which is how the type list and enum
  above were obtained. The record layouts it defines are therefore recovered from
  observed behaviour, not from an authoritative reading: they remain unverified
  until exercised against real bytes of that generation.

Three sibling copies exist, and although they all report the same FileVersion they
are different binaries — another reminder that the folder, not the version, is the
identifier:

```
lhx86\...\PresentationFramework.dll        2,879,488
lhx64\...\PresentationFramework.dll        3,088,384
lh4093x86\...\PresentationFramework.dll    3,166,208
```

## What is still missing, and the two ways to get it

The generation-B record header depends on two `static readonly` widths
(`RecordTypeFieldLength`, `RecordSizeFieldLength`) whose initialisers are not in
the assembly's metadata tables — they are in the static constructor's IL.

1. **Disassemble the two static constructors.** `System.Reflection.Metadata` can
   read method bodies (`MethodDefinition.RelativeVirtualAddress` →
   `PEReader.GetMethodBody`), so `metadump` can be extended to walk the IL of
   `BamlRecord..cctor` and `BamlVariableSizedRecord..cctor` and report the literal
   assigned to each width. This is the cheapest path.
2. **Derive the widths from the corpus.** Type tags observed run to at least `1d`,
   and the size field must chain records; a two-parameter search over
   (typeWidth, sizeWidth, and whether the size includes its own field) constrained
   by "walk 103 files exactly to EOF" should pin them down.

Then read the per-record payload bodies, for which the metadata
already gives every field and its type.

## Recommended next action

Extend `metadump` with an IL reader for the two `..cctor` bodies, then implement
`BamlDialectApplication` (Generation B) alongside the existing
`BamlDialect4074`/`BamlDialectCoreAvalon` readers, and re-run the 103-sample
regression through the CLI.

## Tooling

| tool | purpose |
|---|---|
| `tools/metadump/` | metadata reader for Longhorn assemblies (types, fields, methods, enums) — **the tool that identified the owning assembly** |
| `tools/decode4074.py` | document header decode + segment walk over the corpus |
| `tools/field_census.py` | every integer before each string; flags length matches |
| `tools/longframe_test.py` | proves Generation A framing is impossible for the corpus |
| `tools/framing_search.py`, `chain_search.py`, `header_solver.py`, `id_field.py` | the refuted hypotheses, kept as negative results |
