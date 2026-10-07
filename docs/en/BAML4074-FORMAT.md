# Build-4074 BAML — format resolved from observed stream behaviour

## The decisive correction: the record set differs between builds

An earlier attempt used the record set of the build 4093 tree, which defines **36**
record codes with `DefArrayStart`/`DefArrayEnd` at 24/25; that count is
**unverified**, and the 4093 record set is described below as having 37 members. The
file version `6.0.4051.31026` does not identify a build — the 4074, 4083 and 4093
trees all carry it, so the folder, not the version, is the identifier. The corpus is
**build 4074**, which defines **34** record codes in a *different order*. That single
mismatch is why the walk stalled at 57/103.

Established from observed stream behaviour of the build 4074 corpus, in the build 4074 tree:

| property | fact |
|---|---|
| record type field | 2 bytes, read as a 16-bit integer |
| writer version tuple | `(0, 0)` |
| record type codes | **34** members, and their order *is* their numeric value |
| variable-sized record | a 4-byte size field precedes the payload; the size covers the size field plus the payload, so it does **not** include the 2-byte type field |
| read sequence | read the 2-byte type, then the size field when the record is variable-sized, then that record's payload |
| payload | each record's own, listed under the payload readers below |

## The record type codes (34 members, order == code)

```
 0 Unknown                   17 RoutedEvent
 1 DocumentStart             18 ClrEvent
 2 DocumentEnd               19 XmlnsProperty
 3 ElementStart              20 XmlAttribute
 4 ElementEnd                21 ProcessingInstruction
 5 Property                  22 Comment
 6 PropertyCustom            23 IncludeTag
 7 PropertyComplexStart      24 DefTag
 8 PropertyComplexEnd        25 DefAttribute
 9 PropertyArrayStart        26 EndAttributes
10 PropertyArrayEnd          27 EndStartElement
11 PropertyIListStart        28 PIMapping
12 PropertyIListEnd          29 AssemblyInfo
13 PropertyIDictionaryStart  30 TypeInfo
14 PropertyIDictionaryEnd    31 TypeSerializerInfo
15 LiteralContent            32 AttributeInfo
16 Text                      33 LastRecordType
```

**Codes 20, 21, 22, 24, 26, 27 have no record class** — no corpus stream
carries them and the reader allocates no record for them, so they are never live
records; whether the runtime defines classes for them is **unverified** here. There
is **no `DefArrayStart`/`DefArrayEnd`** in this build.

## Framing

```
<type>  int16 LE   always 2 bytes
<size>  int32 LE   only for BamlVariableSizedRecord subclasses
<payload>

next record = (offset of the size field) + size = recordStart + 2 + size
```

Derived from observed write behaviour of variable-sized records, the piece that took longest:

```
write a variable-sized record:
  position after the type field        = the size-field start   (recordStart + 2)
  write the size field, then the payload
  size                                 = bytes from the size-field start
                                         through the end of the payload
  next record                          = size-field start + size
```

Verified against bytes: the size field at offset 2 reads 41, and `2 + 41 = 43` is
exactly where the next record's type begins.

## Variable vs fixed sizing — corrected against real bytes

`BamlStringValueRecord : BamlVariableSizedRecord`, so every string record carries a
4-byte size field. But `ElementStart` and the `...Start` records that derive from
`PropertyComplexStartRecord` are **plain `BamlRecord`: type field only, no size
field**, with at most an `Int16` payload.

This was got wrong twice, and the second time was caught only by reading the bytes
of `HelloWorld.baml`:

```
@43  03 00        ElementStart, type only
@45  fd ff        Int16 payload (-3), NO size field
@47  1d 00        next record's type
```

Corrected tables:

```python
VARIABLE = {1, 5, 6, 15, 16, 17, 19, 23, 25, 28, 29, 30, 31, 32}

FIXED_WITH_INT16 = {3, 7, 9, 11, 13}    # ElementStart + the four ...Start records
# remaining fixed, no payload: 2, 4, 8, 10, 12, 14, and codes with no class
```

## Proof: two files decode perfectly to EOF

### `HelloWorld.baml` (Longhorn build) — 226 bytes, 12 records, end=226, no error

```
@0    DocumentStart   size=41  featureId='PreAlpha' reader=(0,0) updater=(0,0)
                               writer=(0,0) loadAsync=0 maxAsyncRecords=-1
@43   ElementStart    size=4   int16=-3
@47   XmlnsProperty   size=45  prefix='' value='http://schemas.microsoft.com/2005/xaml/'
@94   ElementStart    size=4   int16=-18
@98   AssemblyInfo    size=28  assemblyId=0 fullName='PresentationFramework'
@128  TypeInfo        size=40  typeId=0 assemblyId=0
                               typeFullName='System.Windows.FrameworkElement'
@170  AttributeInfo   size=11  attributeId=0 ownerTypeId=0 name='ID'
@183  Property        size=16  attributeId=0 value='HelloText'
@201  Text            size=17  value='Hello World!'
@220  ElementEnd      size=2
@222  ElementEnd      size=2
```

Every value is semantically right — the XAML namespace, the assembly, the type, the
`ID` attribute, the `HelloText` property and the `Hello World!` text are exactly
what `HelloWorld.xaml` in the same archive declares.

### `modulesizer.baml` (primary corpus, smallest) — 184 bytes, 7 records, end=184

```
@0    DocumentStart   size=41  featureId='PreAlpha' reader=(0,0) updater=(0,0)
                               writer=(0,0) loadAsync=0 maxAsyncRecords=-1
@43   AssemblyInfo    size=28  assemblyId=0 fullName='PresentationFramework'
@73   TypeInfo        size=49  typeId=0 assemblyId=0
                               typeFullName='System.Windows.Controls.Primitives.Thumb'
@124  ElementStart    size=4   int16=0
@128  XmlnsProperty   size=50  prefix=''
                               value='http:////schemas.microsoft.com//2005//xaml//'
@180  ElementEnd      size=2
@182  DocumentEnd     size=2
```

Note the **doubled slashes** in this corpus's namespace versus the single slashes in
`HelloWorld.baml`. Both are the real bytes; the doubled form is a genuine
Longhorn-era serialization quirk, not a parsing artefact.

## Corpus-wide status: 103 / 103 decompile completely

```
$ python tools/xaml_coverage.py
corpus XAML decompile
  files            : 103
  complete         : 103
  partial          : 0
  no tree / error  : 0
```

The last blocker was `PropertyCustom`, and it was a genuine misreading on my part.
A `PropertyCustom` record is a `Property` record whose payload **overrides** the
base form and carries only the `AttributeId` — there is
no string:

```
[Int16 AttributeId]        no string follows
```

The value is a fixed-width serialized object consumed later by `SetValueObject`,
whose layout depends on the property type — a `uint` for enums, otherwise
`Length` / `GridLength` / `Spacing` / `Brush` / `Thickness` / `FontSize`
`DeserializeFrom` readers. Reading a string there desynchronised every walk that
encountered one, which was the entire 51-file remainder. The reader now takes the
`AttributeId` and records the remaining bytes of the record verbatim, which is
correct at the framing level and defers value decoding to the type-aware stage.

## Decompiling back to XAML

`baml xaml <file>` performs the reverse of compilation. Names come from the
interning tables the stream itself carries:

* `ElementStart.TypeId` negative → `BamlMapTable._knownTypes[-TypeId]`
* `ElementStart.TypeId` non-negative → the `TypeInfo` record with that `typeId`
* `Property.AttributeId` → the `AttributeInfo` record's `name`
* `XmlnsProperty` → `xmlns` / `xmlns:prefix`

### Round-trip against a known source — exact

`HelloWorld.baml` was compiled from a source file the archive also provides:

```xml
<!-- source -->
<Canvas xmlns="http://schemas.microsoft.com/2005/xaml/">
  <Text ID="HelloText">Hello World!</Text>
</Canvas>
```

```xml
<!-- baml xaml HelloWorld-Longhorn-0.0.baml -->
<Canvas>
  <Text ID="HelloText">Hello World!</Text>
</Canvas>
```

The only difference is the omitted default XAML namespace, which is implied and
therefore redundant. `ID="HelloText"` is recovered through `AttributeInfo`, not by
guessing.

### Real application BAML, 34,720 bytes

```xml
<Canvas xmlns="http://schemas.microsoft.com/2003/xaml/" Width="" Height="">
  <Canvas ID="DocumentRootMainScene" Background="" Width="" Height="">
    <Canvas ID="Background2">
      <System.Windows.Controls.TransformDecorator AffectsLayout="false">
        <System.Windows.Media.TranslateTransform X="-0.5" Y="-1.13" />
        <Path ID="Rectangle2_Copy1" Data="M 0 0 L 802 0 L 802 603 L 0 603 Z">
          <System.Windows.Media.LinearGradientBrush EndPoint="1,0.5" StartPoint="0,0.5">
            <System.Windows.Media.GradientStopCollection>
              <System.Windows.Media.GradientStop Color="#FF675BE6" Offset="0" />
              <System.Windows.Media.GradientStop Color="#ADA182EC" Offset="0.25" />
```

Type names, attribute names, path geometry, colours and animation parameters are all
recovered.

## Value decoding: 5660 of 5661 attribute values (99.98%)

Measured by `tools/value_coverage.py` over the whole corpus:

```
attributes total : 5661
VALUE DECODED    : 5660  (99.98%)
RAW BYTES LEFT   :    1  (0.02%)   -- one 'Center' attribute, 2 bytes
```

### The structural fact that shapes this work

`PropertyCustom` values are laid out according to the property's **CLR type**, and
that type is **not in the stream**. It can only be obtained by reflection against a
live property system:

```
resolve the CLR type of a property, in this order:
  if a dependency property is attached  -> its declared property type
  else if an attached-property setter exists
                                       -> the type of that setter's second parameter
  else                                 -> the type on the property info

then hand that resolved type to the value-object setter with the reader
```

So an offline decompiler cannot know a `PropertyCustom` value's type. It is
recoverable only because the encodings are largely **self-describing**, and every
decoder below is validated by requiring that it consume exactly the bytes the size
field allocated.

### The encodings, as observed in the stream

| encoding | shape | bytes | applies to |
|---|---|---|---|
| packed scalar | `[tag]`: `tag & 0x80 == 0` → Pixel, value = tag; else unit = `tag & 0x1F`, width from `tag & 0xE0` (0x80→u8, 0xC0→i16, 0xA0→i32, 0xE0→f64) | 1,2,3,5,9 | length values |
| enum | bare `uint` | 4 | enum-valued properties |
| brush, Other | `[00][string]` — 7-bit length then UTF-8 | 2+len | brushes, non-solid form |
| brush, SolidColor | `[01][uint ARGB]` | 5 | brushes, solid-colour form |
| Thickness | `[count]` 1/2/4 then that many packed scalars | varies | thickness values |

`UnitType` is a three-member enum, **not** a measurement-unit list:

| member | value |
|---|---|
| `Auto` | 0 |
| `Percent` | 1 |
| `Pixel` | 2 |

Getting this wrong is what produced a bogus `Width="100pt"` early on; the correct
reading of the same bytes `81 64` is `Width="100%"`, and of `80 20 03` is
`Width="800"` (pixels, no suffix).

### Colours and brushes — the two serialized forms

The two brush forms are established from the corpus bytes, and match what
`System.Windows.Serialization.IBamlSerialize` produces in the build 4074 tree:

```
discriminator byte, then the payload:

  00  <string>       Other        the brush is written as its string form,
                                  a 7-bit length followed by UTF-8
  01  <uint ARGB>    SolidColor   the colour is written as a packed ARGB uint

writing a brush:
  resolve the string to a KnownColor
  if it resolves to a known colour  -> discriminator 01, then the uint value
  otherwise                         -> discriminator 00, then the string
                                     (the Other form)

reading a brush:
  read one byte
    00 -> read the string and parse it as a brush
    01 -> read a uint and build a solid colour brush from it
```

The key detail is that **`KnownColor` is `: uint` and its members ARE the packed
ARGB values**, so the `uint` can be rendered directly:

```
KnownColor values are packed ARGB, stored as a uint:
  Black  = 4278190080   (0xFF000000)
  Blue   = 4278190335   (0xFF0000FF)
  ...
```

Hence `01 00 00 00 ff` → uint `0xFF000000` → `#FF000000`, which is exactly the
`Background` of the corpus's `DocumentRootMainScene`. The `00`-tagged form is an
ordinary `BinaryWriter.Write(string)`, so it must be read as a 7-bit length plus
UTF-8 — not as a single length byte.

Note that colour strings arrive in two spellings from two different branches:
8-digit `#AARRGGBB` from the `SolidColor` uint, and 6-digit `#RRGGBB` from a
`Brush.Other` string. Both appear in the corpus and both are correct.

### `LiteralContent` carries source positions

```
[string]  a 7-bit length, then UTF-8
[Int32]   the original XAML line
[Int32]   the original XAML column
```

The two `Int32`s are the original XAML line and column, and they are what a
`LiteralContent` record carries beyond its string.

## Generation discrimination: the version tuple

The corpus, the `HelloWorld` pair and the 4093 resources were compared directly.
The finding is that **the `FormatVersion` version tuple — not the signature string
— is what separates the generations**:

| sample set | feature id | reader / updater / writer | verdict |
|---|---|---|---|
| 4074 corpus (103 files) | `PreAlpha` | `0.0 / 0.0 / 0.0` | accepted, 100% |
| `HelloWorld-Longhorn-0.0.baml` | `PreAlpha` | `0.0 / 0.0 / 0.0` | accepted, 100% |
| `HelloWorld-AvalonCTP-0.2.baml` | `PreAlpha` | `0.2 / 0.2 / 0.2` | **refused**, 50% |
| 4093 resources (5 files) | `PreAlpha` | `0.1 / 0.1 / 0.1` | **refused**, 50% |
| `481.baml`, `example.baml` | — | LONG framing | **refused**, 0% |

The feature identifier alone is worthless as a discriminator, because the 4093
generation writes `"PreAlpha"` too and uses the *same* SHORT framing. What differs
is the tuple. Before this check was added, feeding a 4093 stream to the 4074 reader
survived several records and then died on a code that exists in one enum and not
the other:

```
walk stopped at offset 197 of 461: type 33 (LastRecordType) has no record class
walk stopped at offset 192 of 51684: type 27 (EndStartElement) has no record class
```

Both of those are *legal* codes in the 4093 enum (33 = `ResourceInfo`,
27 = `EndStartElement`), which is exactly why the failure looked arbitrary. The
reader now requires a **full score**, so a partial match is a refusal rather than a
confident misparse.

`BamlDetector.MinConfidence = 100` encodes this. The 4074 reader scores 20 for the
framing, 10 for a plausible size, 20 for the signature and 50 for the `(0, 0)`
tuple; a 4093 stream stops at 50 and is rejected.

## Extracting more test material from `.g.resx`

Embedded resources are the richest remaining source of real BAML, and the route is
now automated. `tools/extract_gresx.py` decodes the base64 `<data>` entries of a
generated resource manifest, keeping each resource's original name as its path:

```
python tools/extract_gresx.py <file.g.resx> <outdir> [--list]
```

The primary corpus originally came from `Microsoft.Windows.WCPClient.g.resx` by
exactly this method. Applying it to the build 4093 tree yielded:

| manifest | extracted |
|---|---|
| `PresentationFramework.g.resx` (build 4093 tree) | `themes/classic.baml` — **51,684 bytes**, the largest BAML seen so far |
| `PresentationUI.g.resx` (build 4093 tree) | 4 `.baml` (`installationcancelled` 607, `installationerror` 461, `installationprogress` 631, `trustuicontent` 14,044) plus 4 `.ico` and 4 `.png` |

Those five files are delivered as `samples/extracted-4093/`. They are **not** 4074
material — they are 4093, and they are kept precisely as **generation-discrimination
fixtures**: the 4074 reader must refuse all five, and it does.

`themes/classic.baml` at 51,684 bytes is the single most valuable artefact for any
future 4093 reader: it is a complete theme dictionary, an order of magnitude larger
than anything in the 4074 corpus.

No `.g.resx` was found in the 4074 trees, so the 4074 corpus cannot be enlarged this
way from the material currently on hand.


1. **One attribute value is not decoded**: a 2-byte `Center` (`23 17`), whose type
   is unknown and whose width matches no self-describing encoding.
2. **A 4-byte value is inherently ambiguous** between an enum `uint` and a packed
   scalar. The tie is broken by the tag's high bits, which is correct for every
   sample here but is not a guarantee.
3. **The LONG-framing lineage (3683 / 481) has no C# reader** — only the Python
   reference in `docs/reference-bamlread.py`.
4. **Namespace prefixes are not regenerated**; namespaces are emitted as default
   `xmlns` declarations, though `def:` attributes are preserved and `xmlns:prefix`
   declarations from `XmlnsProperty` are reproduced.
5. **Runtime type info is unavailable**, so values are rendered by encoding rather
   than by declared type. See the section above.

## Build trees: complete inventory and verification

Two build trees are on hand, `4074\` and `4093\`, each holding these assemblies:

| build | assemblies |
|---|---|
| 4074 | `PresentationBuildTasks`, `PresentationCore`, `PresentationCore2`, `PresentationFramework`, `System.Windows`, `WindowsBase` |
| 4093 | `PresentationBuildTasks`, `PresentationCore`, `PresentationCore2`, `PresentationFramework`, `PresentationUI`, `System.Windows`, `WindowsBase` |

### The 4074 record set is confirmed to be the one the decoder is built from

The decoder's tables reproduce the 4074 record set code for code. The corroborating
count is that `BamlRecordType` from the `PresentationFramework`
`System.Windows.Serialization` tree (build 4074) defines 34 members, the same count as
the record set used throughout this work. Per-member name-to-code agreement between
that enum and the tables is **unverified**: only the member count is compared.

The `BamlRecordType` of the `System.Windows` `MS.Internal` tree (build 4074) likewise
defines 26 members, the count of the LONG-lineage record set used earlier
(`MSDotnetAvalon` / `MS.Internal`). That correspondence too is **unverified**: only the
member count is compared.

### Why the 4093 tree must not be used for the corpus

The build 4093 record enum has **37** members, and the extra four
sit in the middle of the list:

```
... IncludeTag, DefArrayStart, DefArrayEnd, DefTag, DefAttribute, EndAttributes,
    PIMapping, AssemblyInfo, TypeInfo, TypeSerializerInfo, AttributeInfo,
    ResourceInfo, PropertyResourceReference, LastRecordType
```

versus 4074's 34:

```
... IncludeTag, DefTag, DefAttribute, EndAttributes, EndStartElement,
    PIMapping, AssemblyInfo, ...
```

`DefArrayStart`/`DefArrayEnd` inserted before `DefTag` shifts every later code, and
`ResourceInfo`/`PropertyResourceReference` were added at the end. Feeding the 4093
tables to a 4074 corpus is precisely what capped the walk at 57/103 earlier.

### `WindowsBase` carries `FormatVersion` — the home DocumentStart's version is read from

The `WindowsBase` tree (build 4074) holds, under `System.IO.CompoundFile\`, the trio
that the `DocumentStart` version tuple depends on:

```
System.IO.CompoundFile\FormatVersion.cs
System.IO.CompoundFile\VersionTuple.cs
System.IO.CompoundFile\ContainerUtilities.cs
```

Earlier the equivalent resources were taken from the `System.Windows` tree. The
`WindowsBase` copy is the one used here, and there is a `4093\WindowsBase`
counterpart for cross-generation comparison. Which tree is authoritative cannot be
established from the stream bytes alone, so that choice remains **unverified**.

## What the material does and does not cover

The format itself is fully covered for the 4074 generation:

* 4074 `PresentationFramework` — record framing, the 34-member enum, every
  record payload, `BamlMapTable._knownTypes`
* 4074 `PresentationCore` — `IBamlSerialize`, `Brush`, `SolidColorBrush`,
  `KnownColor`, `Parsers.ParseBrush`, and the packed scalar types

The **LONG-framing lineage (build 3683 and the older 481-format file) cannot get a
C# reader**: the build 3683 and 481 trees are both unavailable, and the user cannot
supply them. The `4074\System.Windows` tree describes the *class* of that lineage
(`MS.Internal.BamlRecord`, `BamlRecordManager` with its fixed
`[int64 size][int16 type]` framing), and the Python reference in
`docs/reference-bamlread.py` already walks `example.baml` and `481.baml` to EOF, so
that lineage stays at reference-implementation status rather than becoming a
first-class dialect. Its samples remain in `samples/reference/` as negative tests:
the SHORT reader must reject them, and it does.




## Corpus

103 files, 192,327 bytes, 9 topic folders, extracted from
`Microsoft.Windows.WCPClient.g.resx`. Every file begins at offset 0 with
`DocumentStart` (verified: scanning all offsets for `int16 == 1` finds exactly one,
at 0).

## Tooling

| tool | purpose |
|---|---|
| `tools/baml4074.py` | Python reference decoder, authoritative 34-member enum |
| `tools/xaml_coverage.py` | drives `baml xaml` over the corpus and reports coverage |
| `tools/diagnose_walk.py` | reports each failing sample's divergence offset and bytes |
| `tools/solve_size_base.py` | brute force that established the base `recordStart + 2` |
| `tools/solve_sizing.py` | sizing-classification search |
| `tools/align4074.py` | proves DocumentStart is at offset 0 in every sample |
| `tools/field_census.py` | per-string length evidence |
| `tools/metadump/` | metadata reader for Longhorn assemblies |

### Note on file encoding

An earlier `tools/decode4074.py` was corrupted by a PowerShell `Set-Content` round
trip (the file is UTF-8; the shell wrote it in the console code page) and was
replaced by `tools/baml4074.py`. When editing these tools, prefer a UTF-8-aware
editor: shell text round-trips have now corrupted a Python tool and a Markdown doc
twice.



## Payload readers, as observed for each record code

| record | payload |
|---|---|
| DocumentStart | `FormatVersion` + `Boolean LoadAsync` + `Int32 MaxAsyncRecords` |
| ElementStart | `Int16 TypeId` |
| Property / PropertyCustom / RoutedEvent | `Int16 AttributeId` + string |
| PropertyComplex/Array/IList/IDictionary Start | `Int16 AttributeId` |
| ...End records, DocumentEnd, ElementEnd | (none) |
| Text / IncludeTag | string |
| LiteralContent | string + `Int32` + `Int32` |
| DefAttribute | string Value + string Name |
| XmlnsProperty | string Prefix + string Value |
| PIMapping | string Xmlns + string Clrns + `Int16 AssemblyId` |
| AssemblyInfo | `Int16 AssemblyId` + string FullName |
| TypeInfo | `Int16 TypeId` + `Int16 AssemblyId` + string TypeFullName |
| TypeSerializerInfo | TypeInfo + `Int16 SerializerTypeId` |
| AttributeInfo | `Int16 AttributeId` + `Int16 OwnerTypeId` + string Name |

The `DocumentStart` version tuple is read by `FormatVersion.Read`, which uses
`new BinaryReader(s, Encoding.Unicode)` and
`ContainerUtilities.ReadByteLengthPrefixedDWordPaddedUnicodeString`: an `Int32`
byte length, `length/2` UTF-16 chars, DWord padding — then reader/updater/writer as
three `(Int16, Int16)` pairs.

## Proof the tables are now right

`tools/baml4074.py` on the smallest sample decodes real, semantically correct
content with a strictly advancing walk and correct type names:

```
modulesizer.baml (184 bytes) -> 5 records, end=130
  @0    DocumentStart  size=41  featureId='PreAlpha' reader=(0,0) updater=(0,0)
                                writer=(0,0) loadAsync=0 maxAsyncRecords=-1
  @43   AssemblyInfo   size=28  assemblyId=0 fullName='PresentationFramework'
  @73   TypeInfo       size=49  typeId=0 assemblyId=0
                                typeFullName='System.Windows.Controls.Primitives.Thumb'
  @124  ElementStart   size=4   int16=0
  @128  XmlnsProperty  size=2
```

`'PresentationFramework'` and `'System.Windows.Controls.Primitives.Thumb'` are
exactly the strings an independent string scanner found, now reached by structure
rather than by searching. That is the strongest available confirmation that the
framing and the enum are correct.

**One residual issue:** the walk stops at offset 130 with `type 50`, so the tail of
the stream is not yet split correctly. `@128 XmlnsProperty size=2` is suspicious —
`XmlnsProperty` is a `BamlStringValueRecord` and should carry two strings — so
either its size field or the boundary before it is off. The first four records are
certainly correct; the disagreement is confined to the element/attribute tail.

## Corpus

103 files, 192,327 bytes, 9 topic folders, extracted from
`Microsoft.Windows.WCPClient.g.resx`. Every file begins at offset 0 with
`DocumentStart` (verified: scanning all offsets for `int16 == 1` finds exactly one,
at 0).

## Tooling

| tool | purpose |
|---|---|
| `tools/baml4074.py` | the decoder with the authoritative 34-member enum |
| `tools/diagnose_walk.py` | reports each failing sample's divergence offset and bytes |
| `tools/solve_size_base.py` | brute force that established the base `recordStart + 2` |
| `tools/solve_sizing.py` | sizing-classification search |
| `tools/align4074.py` | proves DocumentStart is at offset 0 in every sample |
| `tools/field_census.py` | per-string length evidence |
| `tools/metadump/` | metadata reader for Longhorn assemblies |

### Note on file encoding

`tools/baml4074.py` replaces an earlier `tools/decode4074.py` that was corrupted by
a PowerShell `Set-Content` round trip (the file is UTF-8; the shell wrote it in the
console code page). When editing these tools, prefer a UTF-8-aware editor: shell
text round-trips have now corrupted a Python tool and a Markdown doc twice.

## Next steps

1. Fix the tail: resolve `XmlnsProperty size=2` at offset 128 and the `type 50` at
   130. The likely culprit is the boundary around the fixed `ElementStart` at 124.
2. Move the now-settled tables into the C# `BamlDialect4074` reader and
   re-run the 103-sample regression through the CLI.
3. Then decompile to XAML text: ids resolve through the `TypeInfo` /
   `AttributeInfo` / `PIMapping` tables that the reader populates.
