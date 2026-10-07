# Generation status and the 4093 payload differences

This file records what is decoded, what is enabled, and what is known-but-unfinished, so
the state does not have to be re-derived from the code.

## Where every generation stands

| generation | framing | enum | version tuple | status |
|---|---|---|---|---|
| **4074** | SHORT | 34 | `(0, 0)` | **complete**: 103/103 corpus decompiles to XAML, 5660/5661 attribute values decoded |
| **4083** | SHORT | 34 | `(0, 0)` | **complete by identity** — see below |
| **4093** | SHORT | 37 | `(0, 1)` | **complete**: 133/133 WCPClient corpus + 5/5 extracted resources decompile to XAML |
| **3683** | LONG | 23 | none | **complete**: `example.baml` 35 records to EOF, decompiles to XAML |
| **4015** | LONG | 26 | none | **complete**: `481.baml` 2149 records to EOF, 710 lines of XAML |
| 3718 | LONG | 25 | none | profile defined; **no sample exists** |
| 4033 | LONG | 28 | none | profile defined; **no sample exists** |
| 4039 | LONG | 33 | none | profile defined; **no sample exists** |
| 4042 | LONG | 33 | none | profile defined; **no sample exists** |
| CoreAvalon | LONG | 26 (`MS.Internal`) | none | no sample exists |

## LONG XAML output

`BamlXamlWriter.Element.Build` dispatches on `BamlDialect.Build3683` and calls
`BuildLong`, so the LONG lineage produces real XAML rather than the "not implemented"
placeholder it used to emit.

**Nesting** comes from the record stream, not from the offset links:
`Element` … `EndElement` and `ClrObject` … `EndClrObject` delimit the tree, so a stack
suffices. Every tree-node record also carries a 12-byte header of `depth`, `parentOffset`,
`rightSiblingOffset` and `leftElementSiblingsCount`, but those do not have to be followed —
they are kept in the dump because they make the structure self-checking.

**Names** resolve differently from the SHORT lineage:

| | SHORT | LONG |
|---|---|---|
| element type | `ElementStart.TypeId`, **negative** means an entry in `BamlMapTable._knownTypes` (`index = -TypeId`) | `Element.Id` looked up **directly** in `TypeInfo.TypeId`; no negative encoding, no known-types table |
| attribute | `Property.AttributeId` → `AttributeInfo.Name` | `DynamicProperty.AttributeId` → `AttributeInfo.Name` |
| CLR objects | not a distinct record | `ClrObject.Id` shares the **same** `TypeInfo` id space |

The `ClrObject` case was initially wrong and is worth recording, because the fix is a
one-line lookup. In `481.baml`:

```
TypeInfo typeId=5  "System.Windows.Media.LinearGradient"
ClrObject     id=5                                 <- same id space
TypeInfo typeId=6  "System.Windows.Media.GradientStop"
ClrObject     id=6   (four times, one per stop)
```

Labelling those `clr#5`/`clr#6` looked plausible but produced six meaningless tags in an
otherwise correct document. Resolving them through the same table gives
`<System.Windows.Media.LinearGradient>` containing four
`<System.Windows.Media.GradientStop>` elements.

### Verified against the source

`example.baml` was compiled from `example.xaml`, both on disk:

```xml
<!-- decompiled -->
<Application1.Example xmlns="using:System;System.Windows;System.Windows.Controls"
                      Background="LightBlue">
  <System.Windows.Controls.Button Height="100px" ID="__El2__">Clicky</…>
  <System.Windows.Controls.TextPanel Foreground="White" FontFamily="Trebuchet MS"
                                     FontSize="72pt" ID="TextArea">Hello, world!</…>
</Application1.Example>
```

```xml
<!-- source -->
<FlowPanel xmlns="using:…" xmlns:def="Definition" def:Language="C#" Background="LightBlue">
  <Button Height="100px" Click="handleClick">Clicky</Button>
  <TextPanel Foreground="White" FontFamily="Trebuchet MS" FontSize="72pt" ID="TextArea">…
```

Three differences, and **all three are correct BAML semantics rather than parser errors**:

1. **Root name `Application1.Example`, not `FlowPanel`.** The BAML's `TypeInfo id=0` really
   is `Application1.Example`: `ac` compiled the markup into a generated class deriving from
   `FlowPanel`, and the root element points at that class. The archive contains
   `example.dll` holding it.
2. **`ID="__El2__"` on the Button.** That string is in the BAML. It is the compiler's
   auto-assigned id, and the source never mentions it.
3. **`Click="handleClick"` is absent.** The compiler moved the handler into `example.dll`,
   so the event no longer appears in the compiled form.

So the decompiler is faithful to the bytes; the differences are what compilation did.

## Complex properties are scopes, not attributes

This was a real bug, and a costly one: it silently lost 111 subtrees in `481.baml`.

The LONG vocabulary opens a nested scope for a complex property and closes it separately:

```
ComplexDynamicProperty      Int16 AttributeId   opens a scope
  ... the property value ...
EndComplexDynamicProperty   (no payload)        closes it

ClrComplexProperty          string Name         opens a CLR-backed scope
EndClrComplexProperty       (no payload)        closes it
```

Treating `ComplexDynamicProperty` as an empty attribute produced `<Fill>` with no closing
tag, so everything inside the scope became a child of the **enclosing element** instead of
the property. The output still parsed as XML and looked superficially plausible, which is
exactly why it needed a structural check rather than an eyeball.

The fix pushes a property element onto the tree stack and pops it on the matching
`EndComplex*` record. `481.baml` now renders:

```xml
<System.Windows.Shapes.Path Data="M 0 0 L 149.6 0 L 149.6 238.81 L 0 238.81 Z">
  <Fill>
    <System.Windows.Media.LinearGradient>
      <System.Windows.Media.GradientStop Offset="0.03" Color="#005190DA" />
      <System.Windows.Media.GradientStop Offset="0.46" Color="#BB5190DA" />
      <System.Windows.Media.GradientStop Offset="0.59" Color="#935190DA" />
      <System.Windows.Media.GradientStop Offset="1" Color="#005190DA" />
    </System.Windows.Media.LinearGradient>
  </Fill>
  <TransformEffect>
    <System.Windows.Media.Transform>
      <System.Windows.Media.TranslateTransform Y="-119" X="-74.8" />
      <System.Windows.Media.RotateTransform Center="0,0" Angle="8.6" />
    </System.Windows.Media.Transform>
  </TransformEffect>
</System.Windows.Shapes.Path>
```

### The structural check that caught it

Tag balance, which needs no knowledge of the format:

```
example.baml   open=3    close=3    selfclosed=0     balanced=YES
481.baml       open=648  close=300  selfclosed=348   balanced=YES
```

Before the fix `481.baml` was unbalanced. This is worth keeping as a regression assertion:
a decompiler that emits markup which does not parse is wrong regardless of how the record
layer looks, and balance is the cheapest possible test of that.

### Record usage in the two LONG samples

Useful for knowing what is actually exercised:

| record | `example.baml` (3683) | `481.baml` (4015) |
|---|---|---|
| ClrProperty | — | 747 |
| ClrObject / EndClrObject | — | 464 / 464 |
| ComplexDynamicProperty / End | — | **111 / 111** |
| DynamicProperty | 7 | 65 |
| ClrComplexProperty / End | — | 11 / 11 |
| Element / EndElement | 3 / 3 | 62 / 62 |
| DynamicPropertyCustom | — | 7 |
| TypeInfo | 7 | 16 |
| AttributeInfo | 7 | 13 |
| Text | 2 | — |

`example.baml` is a small, early document; `481.baml` is what actually exercises the CLR
and complex-property machinery. Any change to the LONG writer should be checked against
`481.baml` first.


Checked rather than assumed, because the two generations differ in their record payloads.
`XamlLengthSerializer.ConvertCustomBinaryToObject` in the 4093 decompile is byte-for-byte
the same algorithm that `BamlCustomValue.ReadPackedLength` implements from 4074:

```csharp
byte b = reader.ReadByte();
if ((b & 0x80) == 0) { type = UnitType.Pixel; value = (int)b; }
else
{
    type = (UnitType)(b & 0x1F);
    switch ((byte)(b & 0xE0))
    {
        case 128: value = (int)reader.ReadByte(); break;
        case 192: value = reader.ReadInt16();     break;
        case 160: value = reader.ReadInt32();     break;
        default:  value = reader.ReadDouble();    break;
    }
}
```

`XamlBrushSerializer` delegates to `SolidColorBrush.DeserializeFrom`, and
`XamlFontsizeSerializer` uses the same tag/width scheme with `FontSizeType` in the low
five bits. So no new decoders were needed for 4093 — the existing ones apply unchanged,
which is why the 4093 corpus reaches the same decoding rate as 4074.

Measured over the 133-file 4093 corpus, 6,803 attributes:

```
decoded       6802   99.99%
looks raw        1   (the Center="23 17" text-string false positive, as in 4074)
```

## No samples exist for 3718 / 4033 / 4039 / 4042

These four profiles are defined from decompiled enums and the LONG XAML writer is generic
across the lineage, but **nothing has exercised them against real bytes**. That was
established two ways, both of which are reliable for this question:

1. **Resource-name scan.** Every `.dll` and `.exe` in each build tree was searched for a
   `.baml` resource name. Result: none, in any of the four. This is the test that
   successfully located the WCPClient corpora in 4074 and 4093, so its silence is
   informative rather than a limitation of the search.
2. **Manifest survey.** `tools\survey_resx.py` over each tree finds only string tables
   (`ExceptionStringTable`, `ui.resx`, `Images`, `TrustDialog`, `MessageStringTable`) —
   no `.g.resx`, and therefore no embedded compiled XAML.

`4042\Microsoft.NET\Avalon\Microsoft.Windows.WCPClient.dll` was the most promising lead,
since a WCPClient assembly in the 4042 build could plausibly have carried markup. It is
126,976 bytes and contains **no** `.baml` resource names.

A byte-signature scan for the LONG framing was tried and **abandoned as unusable**: the
pattern `[int64 size][int16 type]` matches ordinary PE data constantly (`size=98` recurs in
nearly every assembly). It produced far more false positives than signal and cannot
substitute for the resource-name test.

So the honest position is: the parser handles the lineage, and four of its six profiles are
unverified. Anyone with a 3718-, 4033-, 4039- or 4042-era BAML sample would close that gap
immediately, because the profile table and writer need no changes to consume it.



## 4083 needs no profile: it is 4074 on the wire

The 4083 decompile's `BamlRecordType` has the **same 34 members, in the same order**, as
4074 — both at the compiler side (`PresentationBuildTasks`) and at runtime
(`PresentationFramework\System.Windows.Serialization`).

Five runtime files differ between 4074 and 4083, and the one that could have mattered does
not: `BamlPropertyCustomRecord.LoadRecordData` reads only `AttributeId`, byte-for-byte the
same body in both. The 4083 additions (`_serializerType`, `_parserContext`,
`SerializerType`, `ParserContext`) are fields that the **write** path may populate but the
**read** path never consumes, so they do not appear in the stream.

Conclusion: 4083 and 4074 share one profile, and the difference is invisible to any
decompiler. That is a property of the data, not a gap here. If a future 4083 sample ever
desynchronises under the 4074 profile, the five differing files above are where to look.

## 4093 is a genuinely different payload format: five differing files

Diffing `PresentationFramework\System.Windows.Serialization` between 4074 and 4093:

```
DIFFER    BamlRecord.cs                version constant (0, 1) vs (0, 0)
DIFFER    BamlDocumentStartRecord.cs   version validation logic, not payload
DIFFER    BamlPropertyCustomRecord.cs
DIFFER    BamlTypeInfoRecord.cs        <- payload change, SOLVED
DIFFER    BamlAttributeInfoRecord.cs   <- payload change, applied but still desyncs
ONLY4093  BamlDefArrayStartRecord.cs
ONLY4093  BamlDefArrayEndRecord.cs
ONLY4093  BamlResourceInfoRecord.cs
ONLY4093  BamlPropertyResourceReferenceRecord.cs
```

### Solved: `BamlTypeInfoRecord` packs flags with the assembly id

```csharp
internal override void LoadRecordData(BinaryReader bamlBinaryReader)
{
    TypeId = bamlBinaryReader.ReadInt16();
    AssemblyId = bamlBinaryReader.ReadInt16();
    TypeFullName = bamlBinaryReader.ReadString();
    _flags = (TypeInfoFlags)(AssemblyId >> 12);
    _assemblyId &= 4095;
}
```

The high four bits of the `Int16` are `TypeInfoFlags` (`DemandLoadChildren = 1`,
`UnusedOne/Two/Three`), and only the low twelve bits are the assembly id. Reading the
field naively yields nonsense such as `assemblyId=4096` for an id of 0, which is exactly
what the first 4093 attempt produced. `RecordProfile.PacksTypeInfoFlags` now carries this,
and the reader reports `assemblyId=0 typeFlags=1`.

### Solved: `BamlAttributeInfoRecord` carries a usage byte

```csharp
internal override void LoadRecordData(BinaryReader bamlBinaryReader)
{
    AttributeId = bamlBinaryReader.ReadInt16();
    OwnerTypeId = bamlBinaryReader.ReadInt16();
    AttributeUsage = (BamlAttributeUsage)bamlBinaryReader.ReadByte();
    Name = bamlBinaryReader.ReadString();
}
```

`RecordProfile.AttributeInfoHasUsage` carries this.

### Solved: two sizing classifications were wrong

Both were mechanical slips when the 4093 table was derived from the 4074 one, and both
produced the same symptom — a plausible-looking record at the wrong offset:

| code | record | wrong | right | why |
|---|---|---|---|---|
| 33 | `AttributeInfo` | fixed | **variable** | `BamlAttributeInfoRecord : BamlVariableSizedRecord`, and 4093's `AttributeInfo` sits at 33 (4074 had it at 32) |
| 35 | `PropertyResourceReference` | fixed + Int16 | **variable** | `BamlPropertyResourceReferenceRecord : BamlPropertyRecord : BamlStringValueRecord : BamlVariableSizedRecord` |

A mis-classified record is the most expensive kind of error here, because reading 2 payload
bytes where there is really a 4-byte size field does not fail immediately — it lands on a
byte pair that frequently looks like a valid code, so the walk continues for a while before
collapsing. That is what made the symptom look like a payload problem.

### The method that actually found them

Walking the stream twice and tabulating, for every record, the **declared end** against the
**offset reached by reading its fields**:

```
off     name                     size   declaredEnd readTo   ok
0       DocumentStart            41     43       43       OK
192     DefAttribute             28     222      222      OK
222     AttributeInfo            17     241      241      OK
...
619     TypeInfo                 45     666      666      OK
670     DefAttribute             77     749      749      OK
766     ResourceInfo             42     810      810      OK
810     PropertyResourceReference (fixed) 814     818      MISMATCH by +4
```

The first row whose two columns differ IS the broken record, with no guesswork. This is far
more reliable than reasoning about raw bytes by hand, which produced two wrong conclusions
during this investigation (a phantom "missing 4 bytes before DefAttribute", and a mistaken
belief that `ElementStart` had no payload).

### Four 4093-only records

```csharp
BamlDefArrayStartRecord  : BamlElementStartRecord   // inherited payload, Int16 TypeId
BamlDefArrayEndRecord    : BamlElementEndRecord     // no payload
BamlResourceInfoRecord   : BamlVariableSizedRecord  // Int16 ResourceId; string Value
BamlPropertyResourceReferenceRecord : BamlPropertyRecord
                                                    // Int16 AttributeId; Int16 ResourceId
```

All four are in `RecordProfile.Build4093`.

### 4093 verified

```
installationerror.baml         22 records   clean
installationcancelled.baml     26 records   clean
installationprogress.baml      32 records   clean
trustuicontent.baml          1157 records   clean
themes-classic.baml          3986 records   clean   (51,684 bytes)
```

and it decompiles to XAML:

```xml
<TextPanel xmlns="http://schemas.microsoft.com/2003/xaml" xmlns:def="Definition"
           XmlSpace="preserve" FontSize="20" ID="_El1_">
    Application installation cancelled. To continue installation, click the link below:
  <HyperLink FontSize="20" ID="AppHyperLinkID">Install App</HyperLink>
</TextPanel>
```


## LONG markup names elements by CLR type, not by prefix

The two lineages describe namespaces differently, which is why LONG output looks unusual
next to SHORT output.

`481.baml` (4015) declares a single namespace, and it is a `using:` directive with no URI
and no prefix:

```
XmlnsProperty  prefix=""  value="using:System;System.Windows;System.Windows.Controls;
    System.Windows.Documents;System.Windows.Media;System.Windows.Media.Animation;
    System.Windows.Navigation;System.Windows.Presenters;System.Windows.Shapes;
    System.Windows.Explorer#System.Windows.Desktop"
```

Everything is then referred to by its full CLR type name:

```
typeId=0   System.Windows.Controls.Canvas          (assemblyId=0  System.Windows)
typeId=5   System.Windows.Media.LinearGradient
typeId=6   System.Windows.Media.GradientStop
typeId=8   System.Windows.Media.TranslateTransform
typeId=10  System.Windows.Media.Animation.FloatAnimation
typeId=15  System.Windows.Desktop.ImageResource    (assemblyId=1  System.Windows.Explorer)
```

`4074` by contrast writes `xmlns="http://schemas.microsoft.com/2005/xaml/"` and can use
unprefixed local names. The XML URI namespace and the prefix mechanism arrive with the later
generation; the earlier one is a purely CLR-facing format.

So emitting full type names in LONG output is faithful to the directive, not a naming bug.
The one place a prefix would still help is `GenericAttribute`, which currently renders as
`{uri}local` — valid XML, but not what a writer would have produced. It is listed as a known
deviation rather than silently changed.
## Known deviations and deliberate omissions

Recorded so they are not mistaken for bugs later.

### `GenericAttribute` renders as `{uri}local`

The record carries `namespaceUri`, `localName` and `value`, and the writer emits the
attribute as `{uri}local` — the standard XAML way to name an attribute in a namespace with
no prefix in scope. It is valid and lossless, but a real writer would have declared a prefix
and emitted `prefix:local`.

**Left as is on purpose.** `GenericAttribute` appears in **zero** of the roughly 350 samples
under `samples\`, so any prefix-assignment scheme would be unverifiable, and an unverifiable
change to naming is worse than a documented, valid fallback.

### Collection properties have no sample

`ClrArrayProperty`, `IListProperty` and `IDictionaryProperty` open a named scope closed by
`EndClrArrayProperty` / `EndIListProperty` / `EndIDictionaryProperty`. All three now open a
property element and close it, matching the complex-property handling.

Before this they fell through to the default case, so their children attached to the
enclosing element with no closing tag — the same defect class as the complex-property bug.
It was found by looking for it, not by a failing sample. No available sample contains these
records, so this fix is **preventive and unverified**.

## Right-to-left layout in the GUI

Arabic and Hebrew mirror the interface:

```
en-US  IsRightToLeft=False  formRTL=No   RTLlayout=False  boxRTL=No
ar-SA  IsRightToLeft=True   formRTL=Yes  RTLlayout=True   boxRTL=No
he-IL  IsRightToLeft=True   formRTL=Yes  RTLlayout=True   boxRTL=No
zh-CN  IsRightToLeft=False  formRTL=No   RTLlayout=False  boxRTL=No
```

`Localization.IsRightToLeft` lists the two RTL tags explicitly rather than consulting
`CultureInfo`, because a `CultureInfo`'s `TextInfo` does not reliably report direction on
.NET Framework, and listing them is more predictable than inferring from the script.

`ApplyLanguage` sets `RightToLeft` and `RightToLeftLayout` **before** assigning any text, so
sizes are recomputed with the mirrored layout already in place.

### The content panes stay left-to-right

`boxRTL=No` on every language, including the RTL ones. Every box built by `NewText()` holds
machine-readable output — record dumps, XAML, interning tables — whose direction is a
property of the data, not of the interface language. Without pinning it, selecting Arabic
would flip hex offsets and markup to right-to-left and make them unreadable. The decision
lives in the single `NewText()` factory so it cannot drift between tabs.

### Two latent index faults fixed while probing

`ApplyLanguage` runs from the constructor and indexed `_tabs.TabPages[0]` and
`_files.Columns[0..2]` without checking counts, so a build with an empty tab set or fewer
columns would have thrown during construction. Both are guarded now.

These surfaced because the probe built a form per language. The first failure looked like an
RTL bug in `ar-SA`; only running each language in its own process showed it was an artifact
of constructing several forms in one process. A probe that reuses process state can
manufacture failures that do not exist.
## 4093's value encodings are identical to 4074's

Checked rather than assumed, because the two generations differ in their record payloads.
`XamlLengthSerializer.ConvertCustomBinaryToObject` in the 4093 decompile is the same
algorithm that `BamlCustomValue.ReadPackedLength` implements from 4074:

```csharp
byte b = reader.ReadByte();
if ((b & 0x80) == 0) { type = UnitType.Pixel; value = (int)b; }
else
{
    type = (UnitType)(b & 0x1F);
    switch ((byte)(b & 0xE0))
    {
        case 128: value = (int)reader.ReadByte(); break;
        case 192: value = reader.ReadInt16();     break;
        case 160: value = (int)reader.ReadInt32(); break;
        default:  value = reader.ReadDouble();    break;
    }
}
```

`XamlBrushSerializer` delegates to `SolidColorBrush.DeserializeFrom`, and
`XamlFontsizeSerializer` uses the same tag/width scheme with `FontSizeType` in the low five
bits. So no new decoders were needed for 4093, which is why the 4093 corpus reaches the
same decoding rate as 4074: 6,803 attributes, 6,802 decoded, the single remainder being the
`Center="23 17"` text-string false positive described earlier.
## Detection is unaffected by any of this

`BamlDialectShort` scores on the `FormatVersion` tuple, and all four SHORT-family
observations behave:

```
4074 corpus (103 files)   tuple (0,0)   100%   accepted
HelloWorld-Longhorn       tuple (0,0)   100%   accepted
4093 extracted (5 files)  tuple (0,1)   100%   accepted by the 4093 reader
HelloWorld-AvalonCTP      tuple (0,2)    50%   refused
```

The tuple requirement is what keeps a 4093 stream from being parsed with the 4074 code
table, where code 33 is `AttributeInfo` rather than `ResourceInfo`.
