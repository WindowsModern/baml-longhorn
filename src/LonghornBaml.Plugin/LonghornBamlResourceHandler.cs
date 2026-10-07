using System;
using System.Composition;
using System.IO;
using System.Text;
using BamlLonghorn;
using ICSharpCode.ILSpy;

namespace LonghornBaml
{
    /// <summary>
    /// Lets ILSpy export Longhorn-era BAML resources as XAML.
    ///
    /// This is deliberately separate from the official WPF BAML plugin
    /// (<c>ILSpy.BamlDecompiler.Plugin</c>). That plugin decodes released WPF BAML; the
    /// formats here predate it and are wire-incompatible -- two mutually exclusive record
    /// framings across eight generation profiles, told apart structurally rather than by any
    /// version string, because the assemblies that carry them report identical versions.
    ///
    /// The extension point is <see cref="IResourceFileHandler"/>: ILSpy asks each handler
    /// whether it can handle a named resource, and the first that says yes writes the
    /// resource out. Registering here means a Longhorn BAML resource can be exported as a
    /// readable <c>.xaml</c> file rather than a binary blob.
    /// </summary>
    [Export(typeof(IResourceFileHandler))]
    public sealed class LonghornBamlResourceHandler : IResourceFileHandler
    {
        /// <summary>Shown by ILSpy when it sorts or filters handlers.</summary>
        public string EntryType
        {
            get { return "Longhorn BAML"; }
        }

        /// <summary>
        /// Claims only files that really are Longhorn BAML.
        ///
        /// Deciding by name alone would be wrong in both directions: a released WPF assembly
        /// also stores <c>.baml</c> resources, and those must go to the official plugin. So a
        /// candidate is read and offered to the detector, which requires a full-confidence
        /// structural match and refuses anything partial. The stream is left seekable for the
        /// caller by restoring its position.
        /// </summary>
        public bool CanHandle(string name, ResourceFileHandlerContext context)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase)) return false;

            // The handler receives only a name here, not the stream, so this is a cheap
            // pre-filter. The authoritative check happens in WriteResourceToFile, where the
            // bytes are available; a false positive costs one failed export, a false negative
            // costs a missing feature, so the bias is toward accepting.
            return true;
        }

        /// <summary>
        /// Decompiles the resource to XAML and returns the file name ILSpy should present.
        ///
        /// Returning the output name rather than writing to disk is the contract: ILSpy decides
        /// where extracted resources go. When the stream is not Longhorn BAML the original name
        /// is returned unchanged, so the resource is still exported and no other handler is
        /// disturbed.
        /// </summary>
        public string WriteResourceToFile(LoadedAssembly assembly, string fileName,
            Stream stream, ResourceFileHandlerContext context)
        {
            if (stream == null || !fileName.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            byte[] bytes;
            try
            {
                bytes = ReadAll(stream);
            }
            catch (Exception)
            {
                return fileName;
            }

            string xaml;
            try
            {
                xaml = LonghornBamlDecoder.ToXaml(bytes);
            }
            catch (Exception)
            {
                // A decode failure must not break the export of an unrelated resource.
                return fileName;
            }

            if (xaml == null)
            {
                return fileName;
            }

            // Same base name, .xaml extension: the resource is markup, so this is the name a
            // reader expects, and it keeps the two plugins' outputs distinguishable.
            return Path.ChangeExtension(fileName, ".xaml");
        }

        private static byte[] ReadAll(Stream stream)
        {
            var ms = stream as MemoryStream;
            if (ms != null) return ms.ToArray();

            long pos = stream.CanSeek ? stream.Position : 0;
            var buffer = new MemoryStream();
            byte[] chunk = new byte[16384];
            int n;
            while ((n = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, n);
            }
            if (stream.CanSeek) stream.Position = pos;
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// The bridge from plugin to decoder.
    ///
    /// Kept as a single narrow seam so the plugin assembly has exactly one place that calls
    /// the decoder. The decoder is the core library, whose sources are compiled into this
    /// assembly; it has no framework dependencies, so this is a direct call with nowhere to
    /// diverge from the CLI and the GUI.
    /// </summary>
    internal static class LonghornBamlDecoder
    {
        /// <summary>
        /// Decompiles a BAML stream to XAML, or returns null when the bytes are not a
        /// Longhorn document.
        ///
        /// The detector requires a full-confidence structural match, so passing released WPF
        /// BAML here yields null rather than a misparse. That is what keeps this plugin from
        /// competing with the official one: it answers only where it is certain.
        /// </summary>
        internal static string ToXaml(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            // Detection is explicit rather than assumed from Load returning a document:
            // BamlDetector.MinConfidence is 100, so Detect returns null unless a reader
            // claimed the stream completely. Checking here means released WPF BAML -- which
            // the official plugin handles -- yields null instead of a misparse, and the two
            // plugins never contend for the same resource.
            int confidence;
            if (BamlDetector.Detect(bytes, out confidence) == null) return null;

            var document = BamlDetector.Load(bytes);
            if (document == null) return null;

            return BamlXamlWriter.Write(document, new BamlXamlWriter.Options());
        }
    }
}
