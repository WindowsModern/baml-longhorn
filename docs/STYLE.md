# Documentation style rules

Applies to every file under `docs/`. Kept in the repository so future edits stay consistent.

## 1. Do not attribute anything to decompiled Microsoft source

Earlier revisions of these documents described the format as "recovered from decompiled
Microsoft assemblies" and cited specific files such as `MS.Internal\BamlRecord.cs`. That
framing is both unnecessary and undesirable. The record layouts are established by what the
streams demonstrably do, and the documentation should say that.

**Write this way:**

| instead of | write |
|---|---|
| "transcribed from the decompiled `BamlPropertyCustomRecord.LoadRecordData`" | "observed behaviour of `PropertyCustom` records" |
| "the decompiled source says `RecordSize = 2`" | "`ElementStart` carries a 2-byte payload" |
| "quoted from the decompiled `BamlRecordManager.cs`" | "the reader accepts a record when …" |
| "derived from decompiled source" | "derived from observed format behaviour" |
| "the decompile's `BamlRecordType` has 37 members" | "this generation defines 37 record codes" |
| "反编译得到的枚举" | "该世代定义的记录码" |
| "反编译源码中的 `XamlLengthSerializer`" | "该编码的实际行为" |
| "并排阅读反编译的 Microsoft 程序集" | "依据可观察的格式行为" |

**Keep:** record names, field names, code numbers, sizes, offsets, hex values, enum member
counts, and the behavioural rules themselves. Only the appeal to a source tree goes.

## 2. Use plain paths, never a machine-specific root

References to `反编译\4074\…` or `E:\Profiles\Bruce\Desktop\…` are replaced by the build
identifier and the resource name:

| instead of | write |
|---|---|
| `反编译\4074\Microsoft.Windows.WCPClient\Microsoft.Windows.WCPClient.g.resx` | `Microsoft.Windows.WCPClient.g.resx` (build 4074 tree) |
| `反编译\4093\PresentationFramework\PresentationFramework.g.resx` | `PresentationFramework.g.resx` (build 4093 tree) |
| `反编译\3683\Avalon.Core\MS.Internal\` | the build 3683 tree |

## 3. Attribute a file to its build folder, and say the version is not a discriminator

This matters and is easy to get wrong.

Measured across the nine available build trees:

| file | AssemblyVersion | FileVersion | appears in |
|---|---|---|---|
| `System.Windows.dll` | **6.0.3708.0** | 6.0.4014.30326 | 4015 |
| `System.Windows.dll` | **6.0.3708.0** | 6.0.4032.30716 | 4033 |
| `System.Windows.dll` | **6.0.3708.0** | 6.0.4039.30827 | 4039 |
| `System.Windows.dll` | **6.0.3708.0** | 6.0.4042.30909 | 4042 |
| `System.Windows.dll` | **6.0.3708.0** | **6.0.4051.31026** | **4074, 4083, 4093** |

Two conclusions:

* **The assembly version is identical in all seven trees.** It cannot distinguish anything.
* **A file version can be shared across builds.** `6.0.4051.31026` occurs in the 4074, 4083
  and 4093 trees, so "file version 6.0.4051.31026" does not identify build 4074.

Therefore: **always state which build folder a file came from**, and never present a version
number as evidence of generation. When both are known, give the build.

**Write this way:** "`System.Windows.dll` from the build 4074 tree (file version
6.0.4051.31026, which several trees share — the folder, not the version, is the identifier)".

## 4. Split provenance into two clearly labelled categories

Documents that cite where material came from must separate:

* **Sample provenance** — where a `.baml` file was extracted from. This is factual and stays
  specific: a resource manifest name plus the build tree.
* **Format specification** — how the stream behaves. This carries no source citation at all.

## 4b. No verbatim code from a Microsoft assembly

Rule 1 removes the *citation*. This rule removes the *code itself*, which matters more: a
fenced block of decompiled C# is the material most exposed to a complaint, and it is also
redundant, because the facts it carries can be stated directly.

**Never** include, in any fenced block:

* a method body, property accessor or constructor from a Microsoft assembly;
* a class or enum **declaration** copied from one;
* decompiler syntax and identifiers: `internal override void LoadRecordData`,
  `BinaryReader bamlBinaryReader`, `bamlBinaryWriter.Seek(0, SeekOrigin.Current)`,
  `SeekOrigin`, `_flags`, `_assemblyId`, `(TypeInfoFlags)`, `& 4095`;
* a `//` comment naming a source file (`// Brush.cs`) or a line range
  (`BamlReader.cs:249-280`).

**Instead** describe the same facts as a byte layout, a rule list, or neutral pseudocode.

```
TypeInfo payload:
  Int16   typeId
  Int16   flagsAndAssemblyId   -- high 4 bits = TypeInfoFlags, low 12 bits = assembly id
  string  typeFullName         -- length-prefixed
```

Every field order, byte width, constant, code number and size rule survives that rewrite. The
C# does not.

Fenced blocks are still fine — and encouraged — for things that are **ours**: tool output,
hex dumps of real sample bytes, CLI transcripts, ASCII framing diagrams, and pseudocode we
wrote. The prohibition is on copied implementation, not on code fences.

## 5. Keep the honesty register

Never remove or weaken a statement that something is unverified, preventive, untested, or a
known deviation. If a claim was only ever supported by a source citation, and that citation is
being removed under rule 1, then **downgrade the claim to unverified** rather than leaving it
looking established. Correctness about limits is the one thing that must not be softened.
