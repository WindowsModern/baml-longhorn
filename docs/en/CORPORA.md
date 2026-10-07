# Corpora and how to obtain more

Every sample set in `samples/` is extracted from a Microsoft resource manifest or a
binary resource. This file records which manifest produced which set, and what was ruled
out, so the search does not have to be repeated.

## The sets

| folder | files | source | notes |
|---|---|---|---|
| `samples\xaml` | 103 | `Microsoft.Windows.WCPClient.g.resx`, build 4074 tree | the primary 4074 corpus; 192,327 bytes |
| `samples\wcp4093` | **133** | `Microsoft.Windows.WCPClient.g.resx`, build 4093 tree | the 4093 corpus; **101 filenames are shared with the 4074 set** |
| `samples\extracted-4093` | 5 | `PresentationFramework.g.resx` (1) + `PresentationUI.g.resx` (4), both build 4093 tree | includes a 51,684-byte theme dictionary |
| `samples\xaml-3683` | **32** | `LocalResources.resx`, build 3683 tree | **source XAML text**, not BAML |
| `samples\reference` | 5 | the `4047 BAML` collection | LONG-family samples plus a source/binary pair |

## Tools

```
python tools\extract_gresx.py     <x.g.resx> <outdir> [--list]   # base64 <data> entries
python tools\survey_resx.py       <root>...                       # what might be compiled XAML
python tools\extract_bf_xaml.py   <x.resx>  <outdir> [--list]    # BinaryFormatter MemoryStream
```

`extract_gresx.py` is the one that produced `xaml` and `wcp4093`; the manifest's `<data>`
entries carry the resource base64-encoded, and the resource NAME says what it is.

`extract_bf_xaml.py` exists because build 3683 stores XAML **not** as BAML but as a
BinaryFormatter-serialized `System.IO.MemoryStream` whose `_buffer` holds the text:

```
00 01 00 00 00 ff ff ff ff 01 00 00 00 00 00 00 00   stream header
04 01 00 00 00  "System.IO.MemoryStream"             class name
0a 00 00 00                                          member count = 10
07 "_buffer" 07 "_origin" 09 "_position" 07 "_length" 09 "_capacity"
0b "_expandable" 09 "_writable" 0a "_exposable" 07 "_isOpen"
1d "MarshalByRefObject+__identity"                   member-name table
<values, one per member, in that order>
```

The members come out alphabetically, and `_buffer` is fixed-size alongside the rest, so
its offset is not predictable; the extractor therefore scans for the `<` byte and decodes
a run from there rather than modelling the whole grammar.

## What is NOT available, and how that was established

**Builds 3718, 4015, 4033, 4039 and 4042 ship no BAML at all.** They were checked two ways:

1. **Resource-name scan.** Every `.dll` and `.exe` under each build tree was searched for a
   `.baml` resource name. Result: *none*, in any of the five. This is the reliable test —
   it is what found the WCPClient corpora in 4074 and 4093.
2. **Manifest survey.** `survey_resx.py` over each tree found only `ExceptionStringTable`,
   `ui.resx`, `Images`, `TrustDialog`, `MessageStringTable` and similar string tables —
   no `.g.resx`, so no embedded compiled XAML.

A byte-signature scan for the LONG framing was also tried and **abandoned as unusable**: the
pattern `[int64 size][int16 type]` matches ordinary PE data constantly (`size=98` recurs in
nearly every assembly), producing more false positives than signal. A four-byte alignment
guess like that cannot replace the resource-name test.

So the LONG profiles for 3718/4033/4039/4042 are **defined from each generation's record codes but
have no sample**: they can be selected and they will parse, but nothing has exercised them
against real bytes. `example.baml` (3683) and `481.baml` (4015) remain the only LONG
samples.

## Build 3683 is the source-XAML era

The build 3683 `LocalResources.resx` (765,077 bytes) holds **32 XAML
documents as text**, not compiled BAML, and `Microsoft.Windows.Client.dll` embeds the same
32 under names like `ShellView.xaml`. That makes 3683 useful as **ground truth for what
Avalon-era markup looked like**, and not as a BAML corpus.

The extracted markup reads as expected for the era:

```xml
<Canvas
  xmlns="using:System.Windows;System.Windows.Controls;System.Windows.Documents;System.Windows.Shapes;System.Windows.Media;System.Windows.Presenters"
  xmlns:ShellViewControls="using:ShellInterop#Microsoft.Windows.Client.Shell.View.Controls"
  Height="100%" Width="100%" Background="#FF0000">

    <FlowPanel ID="Background" Width="100%" Height="100%">
        <Canvas Width="100%" Height="20%" Background="#FFFFFF"/>
        <Canvas Width="100%" Height="80%" Background="VerticalGradient #FFFFFF #C5D4E7"/>
    </FlowPanel>
```

Note `Background="VerticalGradient #FFFFFF #C5D4E7"` — the compound brush shorthand that
`BamlBrushExpander` handles, appearing here in a real 3683 document rather than a
synthetic case.

## A generation pair worth studying

Because 101 filenames appear in both the 4074 and 4093 corpora, the same UI can be compared
across the two generations. `shellview\modulesizer.baml`:

```
4074   184 B   <System.Windows.Controls.Primitives.Thumb xmlns="http:////schemas.microsoft.com//2005//xaml//" />

4093   205 B   <System.Windows.Controls.Primitives.Thumb xmlns="http:////schemas.microsoft.com//2005//xaml//"
                                                          xmlns:def="Definition" />
```

The 21-byte difference is the addition of `xmlns:def="Definition"` — a concrete,
checkable change between the two generations, and both decompile correctly. This pair is
the best starting point for any future question of the form "what changed between 4074 and
4093 in the markup itself, as opposed to the encoding?".

## Verified coverage

```
samples\xaml          103 files   103 recognised as 4074   103/103 complete XAML
samples\wcp4093       133 files   133 recognised as 4093   133/133 complete XAML
samples\extracted-4093  5 files     5 recognised as 4093     5/5 complete XAML
```
