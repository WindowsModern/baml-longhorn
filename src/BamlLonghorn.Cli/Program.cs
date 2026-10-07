using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using BamlLonghorn;
using BamlLonghorn.Dialects;

namespace BamlLonghorn.Cli
{
    /// <summary>
    /// baml - a BAML (compiled XAML) reader for Windows Longhorn / pre-release Avalon.
    /// </summary>
    internal static class Program
    {
        private const string Usage =
@"baml - read BAML (compiled XAML) from Windows Longhorn / pre-release Avalon.

usage:
  baml detect <file|dir>     report the detected dialect and confidence
  baml records <file>        decode and dump every record with offsets
  baml tree <file>           reconstruct and print the markup tree
  baml xaml <file>           decompile to XAML text (the reverse of compilation)
                             --expand-brushes  expand compound brush shorthands
  baml tables <file>         print the assembly/type/attribute interning tables
  baml stats <file|dir>      record histogram and dialect summary
  baml recon <file>          reconnaissance dump (string tokens + inter-token gaps)

options:
  -q, --quiet                suppress the banner

supported dialects:
  4074       real Longhorn application BAML (versioned + interned)  [primary]
  4093       same lineage, later build
  CoreAvalon core System.Windows.dll flat record stream

exit codes:
  0 success   1 usage/other   2 parse error   3 file not found
  4 unrecognised dialect (or not all files recognised)
";

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (BamlParseException ex)
            {
                Console.Error.WriteLine("BAML parse error: " + ex.Message);
                return 2;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine("file not found: " + ex.FileName);
                return 3;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            List<string> positional = new List<string>();
            bool quiet = false;
            bool expandBrushes = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-q" || a == "--quiet")
                {
                    quiet = true;
                }
                else if (a == "--expand-brushes")
                {
                    expandBrushes = true;
                }
                else if (a == "-?" || a == "-h" || a == "--help")
                {
                    Console.WriteLine(Usage);
                    return 0;
                }
                else
                {
                    positional.Add(a);
                }
            }

            if (positional.Count == 0)
            {
                Console.WriteLine(Usage);
                return 0;
            }

            string command = positional[0].ToLowerInvariant();
            if (positional.Count < 2)
            {
                Console.Error.WriteLine("error: '" + command + "' needs a file or directory argument");
                return 1;
            }
            string target = positional[1];

            if (!quiet)
            {
                Console.WriteLine("BamlLonghorn - Longhorn BAML reader");
                Console.WriteLine();
            }

            switch (command)
            {
                case "detect":
                    return CommandDetect(target);
                case "records":
                    return CommandRecords(target);
                case "tree":
                    return CommandTree(target);
                case "xaml":
                    return CommandXaml(target, expandBrushes);
                case "tables":
                    return CommandTables(target);
                case "stats":
                    return CommandStats(target);
                case "recon":
                    return CommandRecon(target);
                default:
                    Console.Error.WriteLine("error: unknown command '" + command + "'");
                    Console.WriteLine();
                    Console.WriteLine(Usage);
                    return 1;
            }
        }

        private static int CommandDetect(string target)
        {
            List<string> files = Expand(target);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("error: no .baml files found at " + target);
                return 1;
            }

            int recognised = 0;
            Dictionary<string, int> byDialect = new Dictionary<string, int>();
            for (int i = 0; i < files.Count; i++)
            {
                byte[] data = File.ReadAllBytes(files[i]);
                int confidence;
                IBamlDialectReader reader = BamlDetector.Detect(data, out confidence);

                string label = reader == null ? "(unrecognised)" : reader.Name;
                int existing;
                byDialect.TryGetValue(label, out existing);
                byDialect[label] = existing + 1;
                if (reader != null)
                {
                    recognised++;
                }

                Console.WriteLine("{0,-56} {1,6} bytes  {2,3}%  {3}",
                    Relative(files[i]), data.Length, confidence, label);
            }

            Console.WriteLine();
            Console.WriteLine("files: {0}   recognised: {1}", files.Count, recognised);
            foreach (KeyValuePair<string, int> pair in byDialect)
            {
                Console.WriteLine("  {0,5}  {1}", pair.Value, pair.Key);
            }
            return recognised == files.Count ? 0 : 4;
        }

        private static int CommandRecords(string target)
        {
            byte[] data = File.ReadAllBytes(target);
            BamlDocument document = Load(data, target);
            if (document.Entries.Count > 0)
            {
                Console.Write(BamlPrinter.DumpEntries(document));
            }
            else
            {
                Console.Write(BamlPrinter.DumpRecords(document));
            }
            return 0;
        }

        private static int CommandTree(string target)
        {
            byte[] data = File.ReadAllBytes(target);
            BamlDocument document = Load(data, target);
            if (document.Entries.Count > 0)
            {
                Console.Write(BamlShortTreeBuilder.Render(document));
            }
            else
            {
                Console.Write(BamlPrinter.DumpTree(document));
            }
            return 0;
        }

        /// <summary>
        /// Decompiles to XAML text. This is the reverse of the compilation the
        /// Avalon Compiler performed, and it is only meaningful for the
        /// SHORT-framing dialect, whose record layer carries the type and
        /// attribute interning tables needed to recover names.
        /// </summary>
        private static int CommandXaml(string target, bool expandBrushes)
        {
            byte[] data = File.ReadAllBytes(target);
            BamlDocument document = Load(data, target);
            if (document.Entries.Count == 0)
            {
                Console.Error.WriteLine(
                    "error: this dialect's record layer is not decoded, so XAML "
                    + "output is not available (try 'records' or 'recon')");
                return 2;
            }
            BamlLonghorn.BamlXamlWriter.Options options =
                new BamlLonghorn.BamlXamlWriter.Options();
            options.ExpandCompoundBrushes = expandBrushes;
            Console.Write(BamlXamlWriter.Write(document, options));
            return 0;
        }

        private static int CommandTables(string target)
        {
            byte[] data = File.ReadAllBytes(target);
            BamlDocument document = Load(data, target);
            Console.Write(BamlPrinter.DumpInternTables(document));
            return 0;
        }

        private static int CommandStats(string target)
        {
            List<string> files = Expand(target);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("error: no .baml files found at " + target);
                return 1;
            }

            Dictionary<BamlRecordType, int> totals = new Dictionary<BamlRecordType, int>();
            int parsed = 0;
            int rejected = 0;
            long bytes = 0;

            for (int i = 0; i < files.Count; i++)
            {
                byte[] data = File.ReadAllBytes(files[i]);
                bytes += data.Length;
                int confidence;
                IBamlDialectReader reader = BamlDetector.Detect(data, out confidence);
                if (reader == null || reader.Dialect != BamlDialect.CoreAvalon)
                {
                    rejected++;
                    continue;
                }

                BamlDocument document = reader.Read(data);
                parsed++;
                for (int r = 0; r < document.Records.Count; r++)
                {
                    BamlRecordType type = document.Records[r].RecordType;
                    int existing;
                    totals.TryGetValue(type, out existing);
                    totals[type] = existing + 1;
                }
            }

            Console.WriteLine("files       : {0}", files.Count);
            Console.WriteLine("total bytes : {0}", bytes);
            Console.WriteLine("CoreAvalon parsed : {0}", parsed);
            Console.WriteLine("not CoreAvalon    : {0}", rejected);
            Console.WriteLine();
            Console.WriteLine("record type totals:");

            List<KeyValuePair<BamlRecordType, int>> ordered =
                new List<KeyValuePair<BamlRecordType, int>>(totals);
            ordered.Sort(delegate(KeyValuePair<BamlRecordType, int> a,
                                  KeyValuePair<BamlRecordType, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            for (int i = 0; i < ordered.Count; i++)
            {
                Console.WriteLine("  {0,7}  {1}", ordered[i].Value, ordered[i].Key);
            }
            return 0;
        }

        private static int CommandRecon(string target)
        {
            byte[] data = File.ReadAllBytes(target);
            int confidence;
            IBamlDialectReader reader = BamlDetector.Detect(data, out confidence);

            Console.WriteLine("dialect   : {0} ({1}%)",
                reader == null ? "unrecognised" : reader.Name, confidence);
            Console.WriteLine("size      : {0} bytes", data.Length);
            Console.WriteLine();

            if (reader == null)
            {
                Console.Error.WriteLine("error: stream not recognised");
                return 4;
            }

            if (reader.Dialect == BamlDialect.Build4074)
            {
                BamlDocument document = reader.Read(data);
                Console.Write(BamlPrinter.Dump4074Recon(data, document, 48));
                return 0;
            }

            Console.Write(BamlPrinter.DumpRecords(reader.Read(data)));
            return 0;
        }

        private static BamlDocument Load(byte[] data, string path)
        {
            int confidence;
            IBamlDialectReader reader = BamlDetector.Detect(data, out confidence);
            if (reader == null)
            {
                throw new BamlParseException("unrecognised BAML dialect in " + path);
            }
            Console.WriteLine("file    : {0}", path);
            Console.WriteLine("dialect : {0} ({1}% confidence)", reader.Name, confidence);
            Console.WriteLine("size    : {0} bytes", data.Length);
            Console.WriteLine();

            BamlDocument document = reader.Read(data);
            Console.WriteLine("records : {0}", document.RecordCount);
            if (!document.IsCompleteParse && document.Note != null)
            {
                Console.WriteLine("note    : {0}", document.Note);
            }
            Console.WriteLine();
            return document;
        }

        private static List<string> Expand(string target)
        {
            List<string> files = new List<string>();
            if (Directory.Exists(target))
            {
                string[] found = Directory.GetFiles(target, "*.baml", SearchOption.AllDirectories);
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                files.AddRange(found);
            }
            else if (File.Exists(target))
            {
                files.Add(target);
            }
            return files;
        }

        private static string Relative(string path)
        {
            try
            {
                string cwd = Directory.GetCurrentDirectory();
                if (path.StartsWith(cwd, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(cwd.Length).TrimStart('\\', '/');
                }
            }
            catch (Exception)
            {
            }
            return path;
        }
    }
}
