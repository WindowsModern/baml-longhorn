# The two BAML generations, and which assembly owns which

Status: **generation ownership settled by evidence.** The corpus is the
`PresentationFramework` generation. Both decompiled `System.Windows.dll` trees are
the *other* generation. The remaining task is bounded and named at the end.

## The distinction, verified from three independent sources

### Generation A — `MSDotnetAvalon.Windows` / `MS.Internal` (the class-record reader)

Owned by `System.Windows.dll` (6.0.3708.0 / 6.0.4051.31026).

Framing, quoted from the decompiled `MS.Internal\BamlRecordManager.cs`:

```csharp
internal BamlRecord GetNextRecord(BinaryReader bamlBinaryReader)
{
    long num = bamlBinaryReader.ReadInt64();      // fixed int64 record size
    if (0 > num) { AvUtility.Throw(1254); }
    return GetNextRecord(bamlBinaryReader, num);
}

internal BamlRecord GetNextRecord(BinaryReader bamlBinaryReader, long recordSize)
{
    BamlRecordType bamlRecordType = (BamlRecordType)bamlBinaryReader.ReadInt16();  // fixed int16
    ...
}
```

and `MS.Internal\BamlRecord.cs`:

```csharp
bamlBinaryWriter.Write(RecordSize);        // long
bamlBinaryWriter.Write((short)RecordType); // short
WriteRecordData(bamlBinaryWriter);
```

25-member `MS.Internal.BamlRecordType`:
`Unknown StartDocument EndDocument Element EndElement ParseLiteralContent
XmlnsProperty DynamicProperty DynamicEvent GenericAttribute Text AssemblyInfo
TypeInfo AttributeInfo ComplexDynamicProperty EndComplexDynamicProperty ClrObject
EndClrObject ClrProperty ClrArrayProperty EndClrArrayProperty ClrComplexProperty
EndClrComplexProperty IncludeTag DynamicPropertyCustom LastRecordType`

### Generation B — `System.Windows.Serialization` (the corpus's actual format)

Owned by **`PresentationFramework.dll` 6.0.4023.30521**. Types read from its
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

Record header, from metadata:

```csharp
abstract class BamlRecord {
    static readonly int RecordTypeFieldLength;        // width of the type field
    static readonly VersionTuple BamlWriterVersion;
}
abstract class BamlVariableSizedRecord : BamlRecord {
    static readonly int RecordSizeFieldLength;        // width of the size field
    int _recordSize;
}
```

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

## The two decompiled trees the user supplied are BOTH Generation A

`E:\Profiles\Bruce\Desktop\4093 - System.Windows` (2777 .cs) and
`E:\Profiles\Bruce\Desktop\System.Windows` (2778 .cs) are the same generation.
File hashes over the BAML core:

| file | 4093 tree | other tree | verdict |
|---|---|---|---|
| `MS.Internal\BamlRecordType.cs` | `3370FBA3660523D5` | `3370FBA3660523D5` | **same** |
| `MS.Internal\BamlRecord.cs` | `7F9A2C4B24718D82` | `7F9A2C4B24718D82` | **same** |
| `MS.Internal\BamlNodeRecord.cs` | `FE64551E6EA57352` | `FE64551E6EA57352` | **same** |
| `MS.Internal\BamlRecordManager.cs` | `502D5758C42043F4` | `502D5758C42043F4` | **same** |
| `MS.Internal\BamlReader.cs` | `A1F53B4F3B62E686` | `1DB7968A6064C5B0` | differ (1131 vs ~1353 lines) |

Neither tree contains `BamlNodeType`, `BamlPIMappingRecord`,
`BamlDocumentStartRecord` or `BamlElementStartRecord` — confirmed by searching all
2777 files for those names: **none**.

So a decompile of `PresentationFramework.dll` is what is still missing.

## Why PresentationFramework cannot simply be decompiled or executed here

* It **will not load for execution** on .NET 9 or .NET Framework 4.8:
  `BadImageFormatException 0x8013110E` ("file is corrupted") — the pre-release
  assembly references cannot be resolved by a modern loader.
* No decompiler is installed (`ilspycmd`, `dotPeek`, dnSpy all absent).
* Its **metadata is intact and readable**, which is how the type list and enum
  above were obtained.

Three sibling copies exist, and they are all the same FileVersion but different
binaries — another reminder not to match by version string:

```
lhx86\...\PresentationFramework.dll        2,879,488
lhx64\...\PresentationFramework.dll        3,088,384
lh4093x86\...\PresentationFramework.dll    3,166,208
```

## What is still missing, and the two ways to get it

The generation-B record header depends on two `static readonly` widths
(`RecordTypeFieldLength`, `RecordSizeFieldLength`) whose initialisers are not in
the metadata tables — they are in the static constructor's IL.

1. **Disassemble the two static constructors.** `System.Reflection.Metadata` can
   read method bodies (`MethodDefinition.RelativeVirtualAddress` →
   `PEReader.GetMethodBody`), so `metadump` can be extended to walk the IL of
   `BamlRecord..cctor` and `BamlVariableSizedRecord..cctor` and report the literal
   assigned to each width. No decompiler required. This is the cheapest path.
2. **Derive the widths from the corpus.** Type tags observed run to at least `1d`,
   and the size field must chain records; a two-parameter search over
   (typeWidth, sizeWidth, and whether the size includes its own field) constrained
   by "walk 103 files exactly to EOF" should pin them down.

Then transcribe the per-record `LoadRecordData` bodies, for which the metadata
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
