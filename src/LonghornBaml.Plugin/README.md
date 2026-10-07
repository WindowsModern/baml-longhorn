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

All three checks below were run; none is asserted.

**Builds clean** against `ILSpy_binaries_10.1.0.8386-x64`, and the output implements
`ICSharpCode.ILSpy.IResourceFileHandler` — the compiler checked every signature.

**Discovery works through ILSpy's own composition mechanism.** ILSpy does not use the standalone
MEF container (`System.Composition.Hosting` is not present in its folder). Its `deps.json` lists
`TomsToolbox.Composition` over `Microsoft.Extensions.DependencyInjection`, which exposes one
binding entry point, `IServiceCollection BindExports(IServiceCollection, Assembly[])`. Called on
the plugin assembly, it registers three descriptors:

```
LonghornBaml.LonghornBamlResourceHandler
ICSharpCode.ILSpy.IResourceFileHandler
TomsToolbox.Composition.IExport<ICSharpCode.ILSpy.IResourceFileHandler>
```

with the export attribute resolving to `System.Composition.ExportAttribute` from
`System.Composition.AttributedModel` — the flavour ILSpy scans for.

**The decode path works on real embedded resources.** `Microsoft.Windows.WCPClient.dll` from the
build 4074 tree carries 109 `.baml` entries inside `Microsoft.Windows.WCPClient.g.resources`.
Driven through the handler's exact call path — `BamlDetector.Detect`, then
`BamlDetector.Load`, then `BamlXamlWriter.Write` — all 109 decoded, 0 refused, 0 failed, every one
identified as build 4074 SHORT framing. That is the same shape as what ILSpy hands the handler: a
name and an opened stream from a resource container.

### What could not be verified, and why

**No other Longhorn assembly could be tested.** The 4074 `WCPClient` loads under .NET 10; every
other candidate fails with `BadImageFormatException` before any resource is reached — `0x80131107
"Old version error"` for the 4083 and 4093 `WCPClient` assemblies, `0x8013110E "File is corrupt"`
for the 4042 `PresentationFramework`. Their metadata is from the pre-release CLR and modern loaders
reject it. ILSpy is subject to the same limit, so this bounds the plugin as much as the test.

**The 4042 `WCPClient` carries no BAML at all** — 126,976 bytes with no `.resources` container —
so the one generation whose profile has no sample remains without one.

Two details were read out of the shipped assemblies rather than guessed, because both are easy to
get wrong and fail silently:

* `ExportAttribute` comes from **`System.Composition.AttributedModel`**, not
  `System.ComponentModel.Composition`. Both MEF flavours define that attribute, and the wrong one
  compiles and is then never discovered.
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
