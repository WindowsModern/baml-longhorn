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

The plugin references the ILSpy contracts, so it must target **net10.0** — the framework ILSpy
10.1 and 11.1 themselves target. Point it at an ILSpy installation:

```
dotnet build src/LonghornBaml.Plugin/LonghornBaml.Plugin.csproj -c Release ^
    -p:ILSpyBinDir="C:\path\to\ILSpy"
```

Then copy `LonghornBaml.dll` into that installation's `Plugins` folder and restart ILSpy.
(The official plugin ships as `ILSpy.BamlDecompiler.Plugin.dll` beside `ILSpy.dll`; a plugin is
just a library MEF discovers there.)

Requires the **.NET 10 SDK**. Only the net8.0 and net9.0 targeting packs are present on the
machine this was developed on — the .NET 10 *runtime* is installed, but not the SDK — so the
build was not run here. What that means for confidence in the code:

* **The decode path is verified.** The plugin calls the decoder through exactly one seam,
  `LonghornBamlDecoder.ToXaml`, and that logic was compiled and exercised over all 241 corpus
  files: 103/103, 133/133, 5/5 decoded, with `HelloWorld-AvalonCTP-0.2` correctly refused.
  The decoder core has no framework dependencies, so it compiles unchanged for net9.0; all 24
  of its source files were checked for Windows Forms, registry, `System.Drawing` and other
  Windows-only API and none is used.
* **The ILSpy interface implementation is not verified**, because the contracts can only be
  referenced from net10.0 and only a net9.0 toolchain is available. The signatures were read
  out of `ILSpy.dll` with `MetadataLoadContext` rather than guessed, and the one contract used
  is the same one the official plugin implements:

  ```
  interface ICSharpCode.ILSpy.IResourceFileHandler
      string EntryType { get; }
      bool CanHandle(string name, ResourceFileHandlerContext context)
      string WriteResourceToFile(LoadedAssembly assembly, string fileName,
                                 Stream stream, ResourceFileHandlerContext context)
  ```

  That is a three-member interface with no ambiguity, but it has not been compiled against, so
  treat the first build as the real check.

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
