# Structural obstacles to rendering converted markup

The converter currently performs renames and namespace repair. What blocks rendering now is
**structure**, not naming: the Longhorn object model nested elements differently from WPF's, and the
converter reproduces Longhorn's nesting.

This file records the obstacles with the evidence, so the work is bounded rather than exploratory.

## The measurement tool

`src/BamlLonghorn.Validate` decodes BAML, converts it to WPF XAML, and then hands the result to the
real WPF XAML reader. That last step is the point: everything before it can produce markup that looks
plausible and still fails in a reader, and those failures are exactly what a reader reports.

```
dotnet build BamlLonghorn.sln
src\BamlLonghorn.Validate\bin\Release\baml-validate.exe samples\xaml -v
```

Options: `-v` prints one line per document, `--out <dir>` also writes the `.xaml` and `.wpf.xaml`,
`--only <substring>` filters by file name. Exit code is 0 when nothing failed for a reason other than
a Longhorn-only type, so it can gate a build.

It is a separate project because the WPF reference is the whole point of it. The core library targets
net45 and deliberately has no presentation dependency, so the decode and convert layers stay usable
without one.

### The distinction the report is built on

**Expected loss.** A Longhorn-only type has no WPF equivalent, so the converter marks it with the
`lh` prefix and no reader will ever load it. Counting those as failures would bury the real ones.

**A converter defect.** Any other failure. Classified by wording rather than exception type, because
the type alone does not separate "unknown member" from "unknown type" and those need different fixes.
Both English and localized wording is matched, since the runtime reports in the system language.

## Current state

| corpus | documents | loaded | failed, has lh: elements | failed, no lh: element |
|---|---|---|---|---|
| 4074 | 103 | 15 | 62 | 26 |
| 4093 | 133 | 17 | 81 | 35 |

The "no lh: element" column is the work list. The other column is expected.

## Fixed, and how it was found

Each of these was invisible to the earlier tests, which checked that the converter produced markup and
never asked whether a reader accepted it.

* **A comment containing `--` made 18 of 103 documents unloadable.** XML forbids a double hyphen
  inside a comment, and the conversion report used a run of hyphens as a group separator. Every
  comment site now runs its text through `BamlXamlWriter.SafeComment`.
* **A percentage length is not a WPF length.** `Width="100%"` fails with "cannot create Width from
  the text 100%", and renaming cannot help. A 100% dimension becomes the corresponding `Stretch`
  alignment, which is the same layout; a partial percentage has no WPF equivalent and the attribute
  is dropped and reported.
* **A namespaced element skipped every attribute rule.** Element names are shortened from a full CLR
  type name, and that path returned early with the attribute text copied verbatim, so `Width="100%"`,
  `Dock` and `ID` survived untouched on most elements.
* **`Dock` is an attached property and belongs on the child**, not on the `DockPanel` that defines it.
  The mapping named the DockPanel as owner, which is the one place the attribute never appears.
* **`RectangleWidth` / `RectangleHeight`** are Longhorn's names for a `Rectangle`'s fill size; WPF has
  only `Width` and `Height`.

## Remaining obstacles, by frequency

### unknown type — 6 documents (4074)

`CommunityBackground`, `SearchBackground`, `Top10Background` and similar. These are application types
from the Longhorn shell that the type index resolved as present, so the converter emitted them
unprefixed against the WPF presentation namespace, where nothing declares them. They should be marked
unconvertible instead. The likely cause is the type index matching a name that exists in a probed
assembly for an unrelated reason; that needs checking rather than guessing.

### no content property — 5 documents (4074)

`System.Windows.Controls.Control` receives children directly. `Control` is abstract and has no content
property, so its children need an explicit property element. This is the per-type semantic decision
described below.

### value not parsable — 4 documents (4074)

`"4" is not a valid value for the property "Dock"`. Longhorn's `Dock` enumeration evidently has more
than four members, or orders them differently from WPF's `Left`, `Top`, `Right`, `Bottom`. The mapping
is right; the enum values are not. Needs a value map rather than a name map.

### ResourceDictionary placement — 4 documents (4074)

`ResourceDictionary` appears as a child element where a `UIElementCollection` is expected. It belongs
in a `Resources` property element.

## Suggested order of work

1. Fix the six unknown types. They are a defect in the unconvertible classification rather than new
   conversion work, so they should be the quickest.
2. Map `Dock` enumeration values. Mechanical, and verifiable by re-running the validator.
3. Handle `ResourceDictionary` as a child by promoting it to a property element. One rule, several
   occurrences.
4. Decide what an abstract type should become when it appears as an element. Per-type judgement, so
   drive it by frequency rather than attempting it exhaustively.
