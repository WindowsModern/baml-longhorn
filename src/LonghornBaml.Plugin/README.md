# LonghornBaml — an ILSpy plugin

Reads **Windows Longhorn / pre-release Avalon** BAML inside ILSpy and exports it as XAML.

This is **not** the official `ILSpy.BamlDecompiler` plugin, and the two are meant to coexist.
That one decodes released WPF BAML. Longhorn's formats predate it and are wire-incompatible:
two mutually exclusive record framings across eight generation profiles, told apart
structurally because the assemblies carrying them report identical versions — the
`AssemblyVersion` is `6.0.3708.0` in every build tree examined, and the file version
`6.0.4051.31026` appears in the 4074, 4083 *and* 4093 trees. Neither number identifies a
generation; only the record vocabulary does.

## What it registers

One extension point, `ICSharpCode.ILSpy.IResourceFileHandler`:

| member | behaviour |
|---|---|
| `EntryType` | `"Longhorn BAML"` |
| `CanHandle` | accepts a `.baml` resource name |
| `WriteResourceToFile` | decompiles to XAML, returns the name with a `.xaml` extension |

`WriteResourceToFile` returns the output name rather than writing a file, because ILSpy decides
where extracted resources go.

**It refuses anything it cannot decode.** Detection goes through
`BamlDetector.Detect`, whose `MinConfidence` is 100, so a partial match yields no reader. A
released WPF BAML resource therefore returns `null` and keeps its original name, leaving it to
the official plugin. Claiming by file extension alone would make the two plugins fight and
would produce wrong output for WPF assemblies.

## Building

The plugin references the ILSpy contracts, so it targets **net10.0-windows** — the framework
ILSpy 10.1.0.8386 and 11.1 target. (net9.0 is not possible: the contracts reference
`System.Runtime` 10.0 and the compiler enforces CS1705 on that.) Point it at an ILSpy
installation:

```
dotnet build src/LonghornBaml.Plugin/LonghornBaml.Plugin.csproj -c Release ^
    -p:ILSpyBinDir="C:\path\to\ILSpy"
```

Then copy `LonghornBaml.dll` into that installation's `Plugins` folder and restart ILSpy.
(The official plugin ships as `ILSpy.BamlDecompiler.Plugin.dll` beside `ILSpy.dll`; a plugin is
just a library MEF discovers there.)

Requires the **.NET 10 SDK**.

## Verification status

**Builds clean** against `ILSpy_binaries_10.1.0.8386-x64`, and the output implements
`ICSharpCode.ILSpy.IResourceFileHandler` — the compiler checked every signature rather than
this being asserted.

**The decode path is verified.** The plugin calls the decoder through exactly one seam,
`LonghornBamlDecoder.ToXaml`, and that logic was compiled and exercised over all 241 corpus
files: 103/103, 133/133 and 5/5 decoded, with `HelloWorld-AvalonCTP-0.2` correctly refused. The
decoder core has no framework dependencies — all 24 of its source files were checked for
Windows Forms, registry, `System.Drawing` and other Windows-only API and none is used — so it
compiles unchanged for net10.0.

**Not verified: that ILSpy actually loads and discovers the plugin.** No ILSpy run was
performed, so MEF discovery of the exported type is untested. The contract is right and the
reference list matches the official plugin's, but the end-to-end check is loading it once.

Two details that were read out of the shipped assemblies rather than guessed, because both are
easy to get wrong and fail silently:

* `ExportAttribute` comes from **`System.Composition.AttributedModel`**, not
  `System.ComponentModel.Composition`. Both MEF flavours define that attribute, and the wrong
  one compiles and is then never discovered.
* `LoadedAssembly` lives in **`ICSharpCode.ILSpyX`**, not `ICSharpCode.ILSpy`, even though the
  interface that uses it is in the latter.

## Why the decoder is compiled in, not referenced

The core library targets .NET Framework 4.5, which a net10.0 assembly cannot reference. Rather
than maintain a second build, the plugin compiles the core's sources directly. One set of
sources means the CLI, the GUI and this plugin cannot disagree about how a stream is decoded.

## Not implemented

* No tree node of its own. The handler covers export; a node that shows the XAML inside ILSpy's
  tree would need `IResourceNodeFactory` and a `Resource`-typed factory, and the official
  plugin already occupies that space for embedded resources.
* No `.baml` files opened from disk. ILSpy's handlers are driven by assembly resources, so a
  standalone file still needs `baml.exe xaml`.
