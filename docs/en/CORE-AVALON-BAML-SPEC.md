# Longhorn BAML Format Specification (v0 — early `MS.Internal` variant)

**Format variant:** the early `MS.Internal` BAML of Avalon, which is distinct from the
`System.Windows.Serialization` variant that the 4074 and 4093 corpora use.
**Material examined:** `System.Windows.dll` from the **build 4074 tree**
(`Microsoft.NET\Avalon\System.Windows.dll`). Note that its file version, 6.0.4051.31026, is
**not** an identifier: the same file version also appears in the 4083 and 4093 trees, and the
assembly version 6.0.3708.0 is identical across all seven build trees examined. The folder is
the reliable identifier; the version number is not.
**Status:** structure established from observed stream behaviour and the record set this
variant defines. **Not validated against real bytes of this variant** — no such sample has been
found, so this variant is implemented but unexercised. See §7.

---

## 1. Stream framing

There is **no magic number and no version header**. The stream is a flat sequence of records
(read until a record's size field is `<= 0`, or fewer than 8 bytes remain).

```
+0   int64   recordSize   // LE, TOTAL record length INCLUDING this 8-byte field
+8   int16   recordType   // LE, BamlRecordType (0..24)
+10  ...     payload      // recordSize - 10 bytes, plus a pad byte when needed
```

> **CORRECTED.** The first draft of this spec said `recordSize` *excludes* the 8-byte field.
> That is wrong: both the real reader and any correct parser advance `pos += recordSize`, so
> it must span the whole record — `8 + 2 + len(payload)`, rounded up to even. The debugging
> that established this is in `baml/STATUS.md` §2.

Padding: the record is kept even so the next record starts on an even offset; the pad byte
is counted **inside** `recordSize`.

Verified reader guards, as observed on the stream:
```
A record is accepted only when all of these hold:
  at least 8 bytes remain                     -- otherwise stop; no record
  the leading Int64 size is > 0               -- otherwise step back, then stop; no record
  the size does not exceed the bytes remaining
                                              -- otherwise step back, then stop; no record
The accepted record spans `size` bytes.

Framing helper used by the record loop:
  the leading Int64 size must be >= 0         -- a negative size is error 1254,
                                                 "Invalid recordSize in Baml"
  otherwise framing continues for a record of `size` bytes
```

### 1.1 `FilePos` semantics (important)

`FilePos` is set to the position of the **recordType** field … but the writer records it
*before* writing the 8-byte size, and **never re-syncs it for the write path**. The read
path sets `FilePos = -1` and never assigns it at all. Every stored pointer is therefore
best treated as
**an offset relative to the record's own framing position**, and the safest reader rule is:

> Treat `recordPos` = offset of the `recordType` field (= `filePos + 8`).
> All "offset" fields are relative to the record's own position, so recover the absolute
> target as `target = relativeField + recordPos` (with the reader adding `base.FilePos`).

⚠️ The write path computes `ParentOffset - base.FilePos` / `RightSiblingOffset - base.FilePos`,
so **if `FilePos` is `filePos+8` the writer emits negative offsets** — self-consistent (the reader
adds `FilePos` back) but hostile to a third-party parser. **This must be confirmed against real
bytes before relying on it** (§7, item A).

---

## 2. Record type codes

The numeric values are the record codes, in this order:

```
0  Unknown                      12 TypeInfo
1  StartDocument                13 AttributeInfo
2  EndDocument                  14 ComplexDynamicProperty
3  Element                      15 EndComplexDynamicProperty
4  EndElement                   16 ClrObject
5  ParseLiteralContent          17 EndClrObject
6  XmlnsProperty                18 ClrProperty
7  DynamicProperty              19 ClrArrayProperty
8  DynamicEvent                 20 EndClrArrayProperty
9  GenericAttribute             21 ClrComplexProperty
10 Text                         22 EndClrComplexProperty
11 AssemblyInfo                 23 IncludeTag
                                24 DynamicPropertyCustom
```

---

## 3. Node header (present only in `BamlNodeRecord` subclasses)

Applies to: **Element (3), ParseLiteralContent (5), Text (10), ClrObject (16)**.
A payload begins with this 12-byte block *before* the record's own fields:

```
+0   int16   depth                       // -1 == unset
+2   int32   parentOffset                // RELATIVE to record position
+6   int32   rightSiblingOffset          // RELATIVE to record position
+10  int16   leftElementSiblingsCount
```

The read and write behaviour of these records:
```
The fields are consumed in this order:
  depth                     Int16
  parentOffset              Int32; the value read is stored as `read + FilePos`
  rightSiblingOffset        Int32; the value read is stored as `read + FilePos`
  leftElementSiblingsCount  Int16
Writing mirrors the read, in the same order, with each offset field
written as its stored value minus `FilePos`.
```

Navigation helpers rely on the sign convention:
```
SeekNextChild: if rightSiblingOffset <= 0, there is no sibling -- stop
               otherwise seek to rightSiblingOffset
SeekToParent : if depth <= 0, there is no parent -- stop
               otherwise seek to parentOffset
```
So **`<= 0` means "none"** — the relative encoding is what makes that test work.

---

## 4. Payload layout per record type

| # | Record | Base | Payload after the 12-byte node header (where applicable) |
|---|---|---|---|
| 0 | `Unknown` | BamlRecord | — (not allocated; `AllocateRecord` returns null) |
| 1 | `StartDocument` | BamlRecord | `int32 rootElementOffset` (relative), `bool loadAsync`, `int32 maxAsyncRecords` |
| 2 | `EndDocument` | BamlRecord | (empty) |
| 3 | `Element` | **Node** | `int16 id`, `int16 childNodes`, `int16 elementNodes`, `int32 firstChildOffset` (relative) |
| 4 | `EndElement` | BamlRecord | (empty) |
| 5 | `ParseLiteralContent` | **Node** | `string value`, `int32 lineNumber`, `int32 linePosition` |
| 6 | `XmlnsProperty` | BamlRecord | `string prefix`, `string value` |
| 7 | `DynamicProperty` | BamlRecord | `int16 attributeId`, `string value`, `bool complex` |
| 8 | `DynamicEvent` | BamlRecord | `int16 attributeId`, `string value` |
| 9 | `GenericAttribute` | BamlRecord | `string namespaceUri`, `string localName`, `string value` |
| 10 | `Text` | **Node** | `string value` |
| 11 | `AssemblyInfo` | BamlRecord | `int16 assemblyId`, `string assemblyFullName` |
| 12 | `TypeInfo` | BamlRecord | `int16 typeId`, `int16 assemblyId`, `string typeFullName` |
| 13 | `AttributeInfo` | BamlRecord | `int16 attributeId`, `int16 ownerTypeId`, `string name` |
| 14 | `ComplexDynamicProperty` | BamlRecord | `int16 attributeId` |
| 15 | `EndComplexDynamicProperty` | BamlRecord | (empty) |
| 16 | `ClrObject` | **Node** | `int16 id` |
| 17 | `EndClrObject` | BamlRecord | (empty) |
| 18 | `ClrProperty` | BamlRecord | `string name`, `string value`, `int16 fieldTypeId` |
| 19 | `ClrArrayProperty` | BamlRecord | `string name` |
| 20 | `EndClrArrayProperty` | BamlRecord | (empty) |
| 21 | `ClrComplexProperty` | BamlRecord | `string name` |
| 22 | `EndClrComplexProperty` | BamlRecord | (empty) |
| 23 | `IncludeTag` | BamlRecord | `string value` |
| 24 | `DynamicPropertyCustom` | **DynamicProperty** | `int16 attributeId` (+ optional payload; see §6) |

All strings are **7-bit-encoded length prefix + UTF-8 bytes**.
`bool` = 1 byte. All integers little-endian.

### 4.1 `Element` is special and easy to get wrong
An `Element` record is a node record, and it consumes the inherited 12-byte node header first —
so the **element record's 12-byte node header is followed by its own four fields**, and
`firstChildOffset` is *also* relative. Non-element node records (Text, ParseLiteralContent,
ClrObject) carry the header too but no `firstChildOffset`.

### 4.2 Interning tables
`AssemblyInfo` / `TypeInfo` / `AttributeInfo` build three tables keyed by id
(`BamlMapTable`). Element records and property records store **ids, not names** — this is the
main size win over XAML. `BamlRecordManager` deliberately does **not** cache these three record
types (`AllocateRecord` throws `"Attempted to allocate uncacheable records"`) because each one is
mutable stateful table input, whereas every other record type is pooled/reused.

---

## 5. Reader state machine

`ReadRecord` is a switch over `RecordType` that mutates a reader-side context
stack. Load-bearing behaviours for a re-implementation:

- **`EndDocument` terminates**: the pending element is added to the tree, the read loop returns `false`, and `EndOfDocument` is set to `true`.
- **`XmlnsProperty` only applies inside Element / ClrObject / ComplexProperty contexts**, and it
  writes into both a reader-level `XmlnsDictionary` and the element's own sealed
  `XmlAttributes.XmlnsDictionary(element)` (unseal → set → seal).
- **`DynamicEvent` is fatal without a Designer** — error 1253
  (1253 == `"DynamicEvent in Baml file."`). i.e. a pure BAML→object reader must be told how to
  bind event handlers, or it must refuse.
- **`BamlMapTable.AddNamespaceMap` is an empty method** (dead hook).

Each numeric error id indexes a 623-entry message table, with the lookup doing
**`id - 1000`**. That makes every numeric throw site's text recoverable and
cross-checkable — a very useful oracle when validating a parser. Known ids in this area:
`1253` DynamicEvent in Baml file · `1254` Invalid recordSize in Baml · `1255` Designer callback is
not support in baml · `1256/1257` ParserContext / xmlns+mapper · `1258/1259` Element/textreader
null · `1266` stream argument is null · `1267` Parser Context is null · `1268` parserContext
argument is null · `1286` Cannot specify multiple roots on an asynchronous Parse call ·
`1287` No Root to Attach Resource to.

---

## 6. Known asymmetries / likely bugs to guard against

These are places where reader and writer disagree, or where the stream behaves oddly. A
parser must choose a behaviour and document it:

1. **`DynamicPropertyCustom` (24) read/write asymmetry**:
   the read path consumes **only** `AttributeId`, while the write path emits
   `AttributeId` **plus** an enum / `IBamlSerialize` payload. The writer also **rewrites the
   record type short to `7` (= DynamicProperty)** when a `TypeDescriptor` conversion fails
   — i.e. a writer-time fallback that *changes the record's identity in the stream*.
   → A reader keying on type 24 may therefore miss records a writer emitted as type 7.
2. **`XamlTreeBuilderBamlWriter` always bypasses the custom
   path** — so in the XAML→BAML direction, type 24 may never be produced.
3. **`BamlReader`'s `GenericAttribute` case discards the record** in at least one path.
4. **Self-assignment of the `fIsATDP` field to itself** in `PropertyManager.DPData` ctor — a CS1717
   self-assignment reported when this region is read; harmless, but a parser should not
   treat it as anything meaningful.
5. `Element.RoleProperty` has **no known consumers** (OPEN question from
   the property-system survey) — relevant only because `Role` participates in `PropertySelector`
   matching that BAML can encode.

---

## 7. What must be verified against real bytes (ordered by risk)

- **A. Offset encoding.** Are `parentOffset`/`rightSiblingOffset`/`firstChildOffset`/`rootElement`
  stored relative to the record position, to the size field, or absolute? Resolve empirically:
  for the first `Element` record, decode `firstChildOffset` and check which candidate base lands
  exactly on a valid record boundary (`int64 size` followed by `int16 type in 0..24`).
- **B. `recordSize` semantics.** Confirm `recordSize == 2 + payloadLen` (type field included) and
  locate the pad byte on odd sizes.
- **C. First records.** Expected order: `StartDocument`, then the `AssemblyInfo` / `TypeInfo` /
  `AttributeInfo` table-filling block, then the element tree. Confirm.
- **D. String encoding.** Confirm the plain 7-bit-length + UTF-8 string encoding.
- **E. Node-header presence.** Confirm which records actually carry the 12-byte header in samples
  (e.g. does a `Text` child inside an element appear with a node header?).
- **F. Whether any `DynamicPropertyCustom` (24) appears at all.**

---

## 8. Hard problem: where do BAML bytes actually come from?

A parser needs BAML to read. Three candidate sources, in order of value:

1. **`.baml` files on disk** — the `lh` build tree contains built BAML from
   *later* WPF projects (WPF.Themes, Sidebar SDK, AvalonBar). These are **modern WPF BAML**, not
   Longhorn 6.0.3708 BAML, so they are useful as *format contrast* only — record type codes and
   the record set differ (modern BAML has a version header and different records). **Do not
   validate the Longhorn parser against them.**
2. **BAML embedded in Longhorn's own assemblies** — `TrustManager.xml.cache` is a *BAML cache* of a
   security-configuration XAML file, produced by `XamlParser` + `BamlWriter` + `BamlTreeBuilder`
   (per the XAML survey). If that cache is embedded as a managed resource in a Longhorn assembly,
   it is a **genuine, era-correct** sample. Prime candidates to inspect:
   `System.Windows.dll`, `System.Windows.Explorer.dll`, `Microsoft.Windows.Client.dll`,
   `System.Help.Pane.dll` in `lh\lhx86\Microsoft.NET\Windows\v6.0.4030\`.
3. **Synthesize** — drive the generation's own `BamlWriter`/`XamlParser` (recompiled) over a small XAML
   file to emit known-good BAML. Highest confidence, but blocked on the tree compiling at all
   (see the unresolved ~50-file gap and the missing `CollectionView` family).

---

## 9. Immediate next steps

1. Finish staging a copy of `System.Windows.dll` and enumerate embedded managed resources;
   identify any BAML/`.cache` resource (task delegated to a staging subagent).
2. Locate a genuine Longhorn-era BAML sample (source 2 above).
3. Write a byte-level parser driven **only** by §1–§4, run it on the sample, and reconcile every
   disagreement — that reconciliation *is* the remaining spec work (resolves §7 A–F).
4. Only then build tree reconstruction (depth + sibling/parent links → document tree) and
   decompilation (ids → names via the three interning tables).
