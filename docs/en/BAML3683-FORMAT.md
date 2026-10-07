# The LONG framing lineage (3683, 3718), and the enum's evolution

The `example.baml` and `481.baml` samples use a different record framing from the
primary 4074 corpus. This document is the result of examining the **3683** record set,
available in the build 3683 tree, and of walking both
samples with the C# reader.

## Framing

The record framing:

```
Write sequence:
  start = current stream position              -- the record's first byte
  if the record's start position is unset (-1), set it to `start`
  Int64  recordSize
  Int16  recordType
  payload                                      -- the record's own fields
  end = current stream position
  if recordSize < 1:                           -- size not yet known
      recordSize = end - start                 -- whole record, from its start
      rewrite recordSize at `start`
      return to `end`
```

```
[int64 size][int16 type][payload]      next record = recordStart + size
```

The size counts the size field, the type field and the payload together. Note this is
a **different rule** from the SHORT framing used from 4047 onward, where the size field
covers only itself plus the payload and is present only on variable-sized records.

## The 3683 enum — 23 members

The record type codes:

```
 0 Uknown                        12 TypeInfo
 1 StartDocument                 13 AttributeInfo
 2 EndDocument                   14 ComplexDynamicProperty
 3 Element                       15 EndComplexDynamicProperty
 4 EndElement                    16 ClrObject
 5 ParseLiteralContent           17 EndClrObject
 6 XmlnsProperty                 18 ClrProperty
 7 DynamicProperty               19 ClrArrayProperty
 8 DynamicEvent                  20 EndClrArrayProperty
 9 GenericAttribute              21 ClrComplexProperty
10 Text                          22 EndClrComplexProperty
11 AssemblyInfo                  23 LastRecordType
```

The first member really is spelled `Uknown` in the stream; the spelling is
preserved in the code table so it matches the stream. There is **no `IncludeTag`** and
**no `DynamicPropertyCustom`** in this build, and the namespace is `MS.Internal`, not
`System.Windows.Serialization`.

## Tree-node records

`BamlNodeRecord` prefixes a 12-byte header, read before the record's own fields:

```
Node header, read before the record's own fields (12 bytes):
  Int16  depth
  Int32  parentOffset          -- raw value plus the record's start position,
                               --   i.e. offset-relative to the record start
  Int32  rightSiblingOffset    -- rebased the same way
  Int16  leftElementSiblingsCount
```

`Element` (3), `ParseLiteralContent` (5), `Text` (10) and `ClrObject` (16) derive from
it. The two offsets are rebased to absolute by the reader, which is what makes them
useful in a dump.

## Payload layouts

Identical in shape to the later `System.Windows` lineage, which is why one name-based
dispatch serves both:

| record | payload |
|---|---|
| StartDocument | `Int32 RootElement` (offset-relative) + `Int32 MaxAsyncRecords` |
| Element | node header + `Int16 Id`, `Int16 ChildNodes`, `Int16 ElementNodes`, `Int32 FirstChildOffset` (offset-relative) |
| ClrObject | node header + `Int16 Id` |
| Text | node header + string |
| ParseLiteralContent | node header + string + `Int32` line + `Int32` position |
| XmlnsProperty | string Prefix + string Value |
| DynamicProperty / DynamicEvent | `Int16 AttributeId` + string |
| ComplexDynamicProperty | `Int16 AttributeId` |
| GenericAttribute | string NamespaceUri + string LocalName + string Value |
| ClrProperty | string Name + string Value + `Int16 FieldTypeId` |
| ClrArrayProperty / ClrComplexProperty | string Name |
| AssemblyInfo | `Int16 AssemblyId` + string AssemblyFullName |
| TypeInfo | `Int16 TypeId` + `Int16 AssemblyId` + string TypeFullName |
| AttributeInfo | `Int16 AttributeId` + `Int16 OwnerTypeId` + string Name |
| EndDocument, EndElement, EndClrObject, End* | (none) |

## Verified against the samples

`example.baml` (965 bytes) walks **35 records to exactly EOF**, and the content is
coherent:

```
@0    StartDocument   size=19
@19   AssemblyInfo    size=20  assemblyId=0 fullName="example"
@39   TypeInfo        size=35  typeId=0 assemblyId=0 typeFullName="Application1.Example"
@74   Element         size=32  depth=0 parentOffset=-1 rightSiblingOffset=-1
                               leftElementSiblingsCount=0 id=0 childNodes=0
                               elementNodes=0 firstChildOffset=-1
@106  XmlnsProperty   size=63  prefix="" value="using:System;System.Windows;System.Windows.Controls"
@169  AssemblyInfo    size=22  assemblyId=1 fullName="Avalon.UI"
```

That matches the independently written Python reference in
`docs\reference-bamlread.py`, which also reports 35 records and 965 bytes.

`HelloWorld-Longhorn-0.0.baml` (the SHORT framing) is **correctly rejected** by the LONG
reader, which completes the mutual-exclusion property used for detection.

## `481.baml` is NOT build 3683 — its actual lower bound is 4015

Walking `481.baml` under each candidate profile settles the question by measurement
rather than by the label on the folder:

| profile | members | result on `481.baml` |
|---|---|---|
| 3683 | 23 | fails after 9 records: `type 24 not in the 3683 enum` |
| 3718 | 25 | fails after 9 records: `type 24 (LastRecordType) has no record class` |
| **4015** | **26** | **2149 records, end = 43627, clean** |
| 4033 | 28 | 2149 records, clean |
| 4039 | 33 | 2149 records, clean |
| 4042 | 33 | 2149 records, clean |

The nine records every profile reads are coherent, so the framing is right and only the
enum is too small. Type code 24 is `DynamicPropertyCustom` from 4015 onward; in 3718 the
code-24 slot is `LastRecordType`, the sentinel, which has no class — so 3718 fails too.

The file the collection labels "481" is therefore from the **4015 era or later**, which
is exactly what the enum's growth predicts:

| build | members | namespace | what it adds |
|---|---|---|---|
| 3683 | 23 | `Avalon.Core\MS.Internal` | — |
| 3718 | 25 | `Avalon.Core\MS.Internal` | `IncludeTag` |
| 4015 | 26 | `System.Windows\MS.Internal` | `DynamicPropertyCustom` |
| 4033 | 28 | `PresentationFramework\System.Windows.Serialization` | `Dynamic*` renamed to `DependencyID*`; `IListProperty` |
| 4039 | 33 | `PresentationFramework\MSAvalon.Windows.Serialization` | dictionary records, `DictionaryKeyTag`, `PIMapping`, `ClrPropertyCustom` |
| 4042 | 33 | identical to 4039 | — |
| 4074 | 34 | `PresentationFramework\System.Windows.Serialization` | the SHORT framing begins |

The `Dynamic` → `DependencyID` → `Dependency` rename across 3718/4015/4033/4039 is the
Avalon-to-WPF rearchitecture reaching the wire format. It is purely lexical, which is why
one name-based payload dispatch still covers all six profiles.

The collection's own note calls the file "from build 4074, but it's an even older BAML
format" — the label describes the *format* and its vintage, not a build number.

## A profile is not always uniquely determined, and that is fine

`example.baml` walks **cleanly under all six profiles**, 35 records to EOF, because it
only uses codes that existed in 3683. The bytes genuinely cannot say which build wrote
it. The reader therefore reports the **oldest profile that walks cleanly**, as the
tightest claim the data supports:

```
example.baml  ->  Longhorn LONG framing (3683 profile)
481.baml      ->  Longhorn LONG framing (4015 profile)
```

Reporting the newest match instead would silently accept codes the document never used.
A document only narrows the profile when it actually needs a later code — which is what
makes `481.baml` informative and `example.baml` not.

## Cross-checked against the Python reference

`docs\reference-bamlread.py`, written independently before this reader existed, also
reports **35 records / 965 bytes** for `example.baml` and **2149 records** for
`481.baml`. The C# walker agrees on both, and for `481.baml` it reaches EOF exactly
(43627 of 43627 bytes) under the 4015 profile.

## Why this matters beyond two files

The enum grows monotonically (23 → 25 → 26 → 28 → 33 → 33 → 34) while the framing
changes only once. That gives two independent discriminators:

* **framing** separates the 3683..4042 era from 4074 and later
* **enum size and member order** separate builds within each era

The reader expresses this directly: `RecordProfile` carries the framing, the code table
and the sizing classification, so adding a generation is a table rather than a parser.
`RecordProfile.LongLineage` lists all six in age order and `BamlDialectLong` walks them
oldest-first.

## Why this matters beyond one file

The enum grows monotonically (23 → 25 → 26 → 28 → 33 → 33 → 34) while the framing
changes only once. That gives two independent discriminators:

* **framing** separates the 3683/3718 era from everything 4047 and later
* **enum size and member order** separate the builds within each era

The reader now expresses this directly: `RecordProfile` carries the framing, the code
table and the sizing classification, so adding a generation is a table, not a parser.
`BamlFraming.Long` currently has the 3683 profile; the 3718 profile is the next entry.
