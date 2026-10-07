# Build-4074 BAML — format resolved from the correct decompile

## The decisive correction: the enum differs between builds

An earlier attempt used the decompile of **6.0.4051.31026**
(`4093 - PresentationFramework`), whose `BamlRecordType` has **36** members with
`DefArrayStart`/`DefArrayEnd` at 24/25. The corpus is **build 4074**, whose
`BamlRecordType` has **34** members in a *different order*. That single mismatch is
why the walk stalled at 57/103.

Authoritative source:
`E:\Profiles\Bruce\Desktop\4074 - PresentationFramework True\System.Windows.Serialization\`

| file | fact |
|---|---|
| `BamlRecord.cs` | `RecordTypeFieldLength = 2`; `BamlWriterVersion = new VersionTuple(0, 0)` |
| `BamlRecordType.cs` | `enum BamlRecordType : short` — **34** members, order == code |
| `BamlVariableSizedRecord.cs` | `RecordSizeFieldLength = 4`; size = size-field + payload |
| `BamlRecordManager.cs` | `ReadNextRecord`: `ReadInt16()` type, then `LoadRecordSize`, then `LoadRecordData` |
| each `Baml*Record.cs` | its `LoadRecordData` body |

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

**Codes 20, 21, 22, 24, 26, 27 have no record class** —
`BamlRecordManager.AllocateRecord` returns `null` for them, so they are never live
records. There is **no `DefArrayStart`/`DefArrayEnd`** in this build.

## Framing

```
<type>  int16 LE   always 2 bytes
<size>  int32 LE   only for BamlVariableSizedRecord subclasses
<payload>

next record = (offset of the size field) + size = recordStart + 2 + size
```

Derived from `BamlVariableSizedRecord.Write`, the piece that took longest:

```csharp
long num = bamlBinaryWriter.Seek(0, SeekOrigin.Current);
bamlBinaryWriter.Write((short)RecordType);
num += 2;                                   // num = SIZE FIELD start
WriteRecordSize(bamlBinaryWriter);
WriteRecordData(bamlBinaryWriter);
long num2 = bamlBinaryWriter.Seek(0, SeekOrigin.Current);
RecordSize = (int)(num2 - num);             // size field + payload
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
`BamlPropertyCustomRecord` derives from `BamlPropertyRecord` but its
`LoadRecordData` **overrides** the base and reads only the `AttributeId` — there is
no string:

```csharp
internal class BamlPropertyCustomRecord : BamlPropertyRecord
{
    internal override void LoadRecordData(BinaryReader bamlBinaryReader)
    {
        base.AttributeId = bamlBinaryReader.ReadInt16();
        _valueObjectSet = false;              // no ReadString() at all
    }
}
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
that type is **not in the stream**. The original reader obtains it by reflection:

```csharp
// BamlAttributeInfoRecord
internal Type GetPropertyType()
{
    DependencyProperty dP = DP;
    if (dP == null)
    {
        MethodInfo setter = AttachedPropertySetter;
        if ((object)setter == null) return PropInfo.PropertyType;   // reflection
        return setter.GetParameters()[1].ParameterType;
    }
    return dP.PropertyType;
}

// BamlRecordReader
bamlPropertyRecord.SetValueObject(
    isDp ? ((DependencyProperty)dpOrPi).PropertyType
         : ((PropertyInfo)dpOrPi).PropertyType, reader);
```

So an offline decompiler cannot know a `PropertyCustom` value's type. It is
recoverable only because the encodings are largely **self-describing**, and every
decoder below is validated by requiring that it consume exactly the bytes the size
field allocated.

### The encodings, transcribed

| encoding | shape | bytes | source |
|---|---|---|---|
| packed scalar | `[tag]`: `tag & 0x80 == 0` → Pixel, value = tag; else unit = `tag & 0x1F`, width from `tag & 0xE0` (0x80→u8, 0xC0→i16, 0xA0→i32, 0xE0→f64) | 1,2,3,5,9 | `Length.DeserializeFrom` |
| enum | bare `uint` | 4 | `BamlPropertyCustomRecord.WriteRecordData` |
| brush, Other | `[00][string]` — 7-bit length then UTF-8 | 2+len | `Brush.SerializeOn` |
| brush, SolidColor | `[01][uint ARGB]` | 5 | `SolidColorBrush.SerializeOn` |
| Thickness | `[count]` 1/2/4 then that many packed scalars | varies | `Thickness.SerializeOn` |

`UnitType` is a three-member enum, **not** a measurement-unit list:

```csharp
public enum UnitType { Auto = 0, Percent = 1, Pixel = 2 }
```

Getting this wrong is what produced a bogus `Width="100pt"` early on; the correct
reading of the same bytes `81 64` is `Width="100%"`, and of `80 20 03` is
`Width="800"` (pixels, no suffix).

### Colours and brushes — from PresentationCore

The two brush forms come from `System.Windows.Serialization.IBamlSerialize` and its
implementations in **`4074 - PresentationCore`**:

```csharp
// Brush.cs
public void SerializeOn(BinaryWriter writer, string stringValue)
{
    writer.Write((byte)0);              // SerializationBrushType.Other
    writer.Write(stringValue);          // BinaryWriter.Write(string)
}
public static object DeserializeFrom(BinaryReader reader)
{
    switch ((SerializationBrushType)reader.ReadByte())
    {
        case SerializationBrushType.Other:      return Parsers.ParseBrush(reader.ReadString(), null);
        case SerializationBrushType.SolidColor: return SolidColorBrush.DeserializeFromReader(reader);
    }
}

// SolidColorBrush.cs
public new void SerializeOn(BinaryWriter writer, string stringValue)
{
    KnownColor knownColor = KnownColors.ColorStringToKnownColor(stringValue);
    if (knownColor != KnownColor.UnknownColor)
    {
        writer.Write((byte)1);
        writer.Write((uint)knownColor);
    }
    else base.SerializeOn(writer, stringValue);
}
internal static object DeserializeFromReader(BinaryReader reader)
{
    return KnownColors.SolidColorBrushFromUint(reader.ReadUInt32());
}
```

The key detail is that **`KnownColor` is `: uint` and its members ARE the packed
ARGB values**, so the `uint` can be rendered directly:

```csharp
internal enum KnownColor : uint
{
    Black = 4278190080u,   // 0xFF000000
    Blue  = 4278190335u,   // 0xFF0000FF
    ...
}
```

Hence `01 00 00 00 ff` → uint `0xFF000000` → `#FF000000`, which is exactly the
`Background` of the corpus's `DocumentRootMainScene`. The `00`-tagged form is an
ordinary `BinaryWriter.Write(string)`, so it must be read as a 7-bit length plus
UTF-8 — not as a single length byte.

Note that colour strings arrive in two spellings from two different branches:
8-digit `#AARRGGBB` from the `SolidColor` uint, and 6-digit `#RRGGBB` from a
`Brush.Other` string. Both appear in the corpus and both are correct.

### `LiteralContent` carries source positions

```csharp
Value = ReadString(); ReadInt32(); ReadInt32();   // line, position
```

The two `Int32`s are the original XAML line and column, which is why a
`LiteralContent` record is 15 bytes for a 6-character string.

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
exactly this method. Applying it to the decompile tree yielded:

| manifest | extracted |
|---|---|
| `反编译\4093\PresentationFramework\PresentationFramework.g.resx` | `themes/classic.baml` — **51,684 bytes**, the largest BAML seen so far |
| `反编译\4093\PresentationUI\PresentationUI.g.resx` | 4 `.baml` (`installationcancelled` 607, `installationerror` 461, `installationprogress` 631, `trustuicontent` 14,044) plus 4 `.ico` and 4 `.png` |

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

## Decompiled assemblies: complete inventory and verification

A consolidated decompile tree now exists at `E:\Profiles\Bruce\Desktop\反编译`, split
into `4074\` and `4093\`:

| build | assemblies |
|---|---|
| 4074 | `PresentationBuildTasks`, `PresentationCore`, `PresentationCore2`, `PresentationFramework`, `System.Windows`, `WindowsBase` |
| 4093 | `PresentationBuildTasks`, `PresentationCore`, `PresentationCore2`, `PresentationFramework`, `PresentationUI`, `System.Windows`, `WindowsBase` |

### The 4074 tree is confirmed to be the one the decoder is built from

`反编译\4074\PresentationFramework\System.Windows.Serialization\BamlRecordType.cs`
hashes to `39AD3B637C6848C8`, **byte-identical** to the
`4074 - PresentationFramework True` copy used throughout this work, and both have
the same 34 members. So the decoder's tables are provably sourced from the right
build.

`反编译\4074\System.Windows\MS.Internal\BamlRecordType.cs` hashes to
`3370FBA3660523D5`, matching the LONG-lineage decompile used earlier (26 members,
`MSDotnetAvalon` / `MS.Internal`).

### Why the 4093 tree must not be used for the corpus

`反编译\4093\PresentationFramework`'s enum has **37** members, and the extra four
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

### `WindowsBase` owns `FormatVersion` — the correct home for DocumentStart's version

`反编译\4074\WindowsBase\System.IO.CompoundFile\` contains the trio that
`BamlDocumentStartRecord.LoadRecordData` depends on:

```
System.IO.CompoundFile\FormatVersion.cs
System.IO.CompoundFile\VersionTuple.cs
System.IO.CompoundFile\ContainerUtilities.cs
```

Earlier the equivalent files were read out of the `System.Windows` tree. The
`WindowsBase` copy is the authoritative one, and there is a `4093\WindowsBase`
counterpart for cross-generation comparison.

## What the material does and does not cover

The format itself is fully covered for the 4074 generation:

* 4074 `PresentationFramework` — record framing, the 34-member enum, every
  `LoadRecordData`, `BamlMapTable._knownTypes`
* 4074 `PresentationCore` — `IBamlSerialize`, `Brush`, `SolidColorBrush`,
  `KnownColor`, `Parsers.ParseBrush`, and the packed scalar types

The **LONG-framing lineage (build 3683 and the older 481-format file) cannot get a
C# reader**: a 3683 and a 481 decompile are both unavailable, and the user cannot
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



## Payload readers, transcribed from each LoadRecordData

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

`FormatVersion.Read` (in the `System.Windows.dll` decompile at
`MSDotnetAvalon.IO.CompoundFile\FormatVersion.cs`) uses
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
2. Transcribe the now-settled tables into the C# `BamlDialect4074` reader and
   re-run the 103-sample regression through the CLI.
3. Then decompile to XAML text: ids resolve through the `TypeInfo` /
   `AttributeInfo` / `PIMapping` tables that the reader populates.
