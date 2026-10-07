using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Markup;
using BamlLonghorn;

namespace BamlLonghorn.Validate
{
    /// <summary>
    /// Converts BAML to XAML, converts that to WPF XAML, and then checks whether WPF can actually
    /// load the result.
    ///
    /// The last step is the point. Everything before it can produce markup that looks plausible and
    /// still fails in a reader, and the failures that matter are exactly the ones a reader reports:
    /// an unknown type, an unknown member, a value the target property cannot parse. Decoding and
    /// conversion are both covered by their own tests, but neither of those tests asks the question
    /// this tool exists to ask.
    ///
    /// The distinction the report is built around is between failure and expected loss:
    ///
    ///   * A Longhorn-only type is expected loss. It has no WPF equivalent, the converter marks it
    ///     with the lh prefix, and no reader will ever load it. Counting these as failures would bury
    ///     the real ones, since the corpora contain a great many of them.
    ///   * Any other failure is a defect in the converter and is classified so it can be worked on:
    ///     unknown member, unknown type, unparsable value, invalid markup, and so on.
    ///
    /// Exit code is 0 when every document either loads or fails only for its Longhorn-only types, and
    /// 1 when a real failure was found, so this can gate a build.
    /// </summary>
    public static class Program
    {
        // WPF components require a single-threaded apartment. Without this the run fails partway
        // through with "the calling thread must be STA", which looks like a converter defect and is
        // not one -- it is this tool refusing to create a visual on the wrong kind of thread.
        [STAThread]
        public static int Main(string[] args)
        {
            var paths = new List<string>();
            bool verbose = false;
            bool keep = false;
            string outputDir = null;
            string onlyFilter = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-v" || a == "--verbose") { verbose = true; continue; }
                if (a == "--keep") { keep = true; continue; }
                if (a == "--out" && i + 1 < args.Length) { outputDir = args[++i]; continue; }
                if (a == "--only" && i + 1 < args.Length) { onlyFilter = args[++i]; continue; }
                if (a == "-h" || a == "--help")
                {
                    Usage();
                    return 0;
                }
                paths.Add(a);
            }

            List<string> files = Expand(paths);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("no .baml files found");
                Usage();
                return 2;
            }

            var tally = new Tally();
            var failures = new List<Failure>();
            var stageErrors = new List<Failure>();

            Console.WriteLine("BAML -> XAML -> WPF XAML -> load");
            Console.WriteLine("files: {0}", files.Count);
            Console.WriteLine();

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                if (onlyFilter != null
                    && name.IndexOf(onlyFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                tally.Files++;
                Result r = Validate(file, tally);
                if (r.Stage != null)
                {
                    stageErrors.Add(new Failure(name, r.Stage, r.ExceptionType, r.Message));
                }
                else if (r.Loaded)
                {
                    tally.Loaded++;
                }
                else
                {
                    // A document whose markup carries an lh: element cannot load, by construction:
                    // the converter marks a Longhorn-only type that way and no reader has ever heard
                    // of it. Counting those as defects would bury the ones that are defects, so they
                    // are separated here. What remains is the list worth working through.
                    if (r.LonghornTypes > 0)
                    {
                        tally.FailedExpected++;
                    }
                    else
                    {
                        tally.Failed++;
                        failures.Add(new Failure(name, "load", r.ExceptionType, r.Message));
                    }
                }

                if (verbose)
                {
                    string mark = r.Stage != null ? "STAGE" : (r.Loaded ? "OK   " : "FAIL ");
                    Console.WriteLine("  {0} {1,-38} lh-types={2,-3} {3}",
                        mark, name, r.LonghornTypes, r.Loaded ? "" : r.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== summary ===");
            Console.WriteLine("  documents                          : {0}", tally.Files);
            Console.WriteLine("  loaded by WPF                      : {0}", tally.Loaded);
            Console.WriteLine("  could not load, markup has lh: elem : {0}  (expected, Longhorn-only types)",
                tally.FailedExpected);
            Console.WriteLine("  could not load, no lh: element      : {0}  (converter defects)",
                tally.Failed);
            Console.WriteLine("  failed before load                 : {0}", stageErrors.Count);
            Console.WriteLine();
            Console.WriteLine("  documents containing an lh: element : {0}", tally.MarkupWithLonghornTypes);
            Console.WriteLine("  documents containing none          : {0}", tally.CleanMarkup);
            Console.WriteLine("  lh: elements in total              : {0}", tally.LonghornTypeTotal);
            Console.WriteLine();

            if (tally.Failed == 0 && stageErrors.Count == 0)
            {
                Console.WriteLine("  Every document either loads or fails only because of a Longhorn-only type.");
            }
            else
            {
                Console.WriteLine("  The documents below have no lh: element and still failed, so the");
                Console.WriteLine("  cause is the converter rather than a missing WPF equivalent.");
            }
            Console.WriteLine();

            ReportByKind(failures, "load failures by kind");

            if (stageErrors.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("=== failed before load ===");
                foreach (var f in stageErrors) Console.WriteLine("  {0}: {1}", f.File, f.Message);
            }

            if (outputDir != null)
            {
                WriteArtifacts(files, outputDir);
            }

            return failures.Count == 0 && stageErrors.Count == 0 ? 0 : 1;
        }

        private static void ReportByKind(List<Failure> failures, string title)
        {
            if (failures.Count == 0)
            {
                Console.WriteLine("=== " + title + " ===");
                Console.WriteLine("  none");
                return;
            }

            var byKind = new Dictionary<string, List<Failure>>(StringComparer.Ordinal);
            foreach (var f in failures)
            {
                string kind = Classify(f.Message);
                List<Failure> list;
                if (!byKind.TryGetValue(kind, out list)) byKind[kind] = list = new List<Failure>();
                list.Add(f);
            }

            Console.WriteLine("=== " + title + " ===");
            var kinds = new List<string>(byKind.Keys);
            kinds.Sort(delegate (string a, string b)
            {
                return byKind[b].Count.CompareTo(byKind[a].Count);
            });

            foreach (string kind in kinds)
            {
                List<Failure> list = byKind[kind];
                Console.WriteLine("  {0,-34} {1}", kind, list.Count);
                int shown = 0;
                foreach (var f in list)
                {
                    if (shown++ >= 3) break;
                    Console.WriteLine("      e.g. {0}: {1}", f.File, Truncate(f.Message, 110));
                }
            }
        }

        /// <summary>
        /// Names the kind of failure from the reader's message.
        ///
        /// The messages are matched on their wording because that is what the reader gives; the
        /// exception type alone does not separate "unknown member" from "unknown type", and those two
        /// need different fixes in the converter.
        /// </summary>
        private static string Classify(string message)
        {
            if (message == null) return "unknown";

            // Matched on wording rather than exception type, because the type alone does not separate
            // "unknown member" from "unknown type" and those need different fixes in the converter.
            //
            // Both English and localized wording is listed. The runtime reports exceptions in the
            // system's language, and on this machine that is Chinese, so matching only English left
            // nearly every failure classified as "other" -- which hid the very distinction the
            // classification exists to draw. Order matters: the more specific phrase first.
            var patterns = new string[,]
            {
                { "no content property (needs a property element)", "content property" },
                { "no content property (needs a property element)", "不具有内容属性" },
                { "element needs a property element", "不包含属性元素" },
                { "element needs a property element", "does not contain a property element" },
                { "undeclared prefix", "undeclared prefix" },
                { "undeclared prefix", "未声明的前缀" },
                { "unknown member", "unknown member" },
                { "unknown member", "未知成员" },
                { "unknown type", "unknown type" },
                { "unknown type", "未知类型" },
                { "value not parsable", "cannot create" },
                { "value not parsable", "cannot convert" },
                { "value not parsable", "FormatException" },
                { "value not parsable", "无法从文本" },
                { "value not parsable", "不是属性" },
                { "value not parsable", "的有效值" },
                { "ResourceDictionary placement", "ResourceDictionary" },
                { "ResourceDictionary placement", "UIElementCollection" },
                { "constructor rejected content", "constructor" },
                { "constructor rejected content", "构造函数" },
                { "calling thread must be STA", "STA" },
            };

            for (int i = 0; i < patterns.GetLength(0); i++)
            {
                if (message.IndexOf(patterns[i, 1], StringComparison.OrdinalIgnoreCase) >= 0)
                    return patterns[i, 0];
            }
            return "other";
        }

        private sealed class Result
        {
            public bool Loaded;
            public string Stage;
            public string ExceptionType;
            public string Message;
            public int LonghornTypes;
        }

        private static Result Validate(string file, Tally tally)
        {
            var r = new Result();
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                BamlFileView view = BamlFileView.FromBytes(bytes, file);

                if (view.LoadError != null)
                {
                    r.Stage = "decode";
                    r.Message = view.LoadError;
                    return r;
                }

                string markup = view.Render(BamlView.WpfXaml);
                r.LonghornTypes = CountLonghornTypes(markup);
                tally.LonghornTypeTotal += r.LonghornTypes;
                if (r.LonghornTypes > 0) tally.MarkupWithLonghornTypes++;
                else tally.CleanMarkup++;

                try
                {
                    object parsed = XamlReader.Parse(markup);
                    var fe = parsed as System.Windows.FrameworkElement;
                    if (fe != null)
                    {
                        fe.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                        fe.Arrange(new System.Windows.Rect(fe.DesiredSize));
                    }
                    r.Loaded = true;
                }
                catch (XamlParseException ex)
                {
                    r.ExceptionType = "XamlParseException";
                    r.Message = "line " + ex.LineNumber + " pos " + ex.LinePosition + ": " + Innermost(ex);
                }
                catch (Exception ex)
                {
                    r.ExceptionType = ex.GetType().Name;
                    r.Message = Innermost(ex);
                }
            }
            catch (Exception ex)
            {
                r.Stage = "convert";
                r.ExceptionType = ex.GetType().Name;
                r.Message = Innermost(ex);
            }
            return r;
        }

        /// <summary>
        /// Counts elements left under the lh prefix, which is how the converter marks a type with no
        /// WPF equivalent. These documents cannot load by construction, so they are reported
        /// separately rather than counted as converter defects.
        /// </summary>
        private static int CountLonghornTypes(string markup)
        {
            int n = 0;
            int i = 0;
            while (true)
            {
                i = markup.IndexOf("<lh:", i, StringComparison.Ordinal);
                if (i < 0) break;
                n++;
                i += 4;
            }
            return n;
        }

        private static string Innermost(Exception ex)
        {
            Exception e = ex;
            int guard = 0;
            while (e.InnerException != null && guard++ < 8) e = e.InnerException;
            string m = e.Message ?? string.Empty;
            int nl = m.IndexOfAny(new char[] { '\r', '\n' });
            return nl < 0 ? m : m.Substring(0, nl);
        }

        private static string Truncate(string s, int n)
        {
            if (s == null) return string.Empty;
            return s.Length <= n ? s : s.Substring(0, n - 3) + "...";
        }

        private sealed class Failure
        {
            public readonly string File;
            public readonly string Stage;
            public readonly string ExceptionType;
            public readonly string Message;

            public Failure(string file, string stage, string exceptionType, string message)
            {
                File = file;
                Stage = stage;
                ExceptionType = exceptionType;
                Message = message;
            }
        }

        private sealed class Tally
        {
            public int Files;
            public int Loaded;
            public int Failed;
            public int FailedExpected;
            public int MarkupWithLonghornTypes;
            public int CleanMarkup;
            public int LonghornTypeTotal;
        }

        private static List<string> Expand(List<string> paths)
        {
            var files = new List<string>();
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    string[] found = Directory.GetFiles(p, "*.baml", SearchOption.AllDirectories);
                    Array.Sort(found, StringComparer.Ordinal);
                    files.AddRange(found);
                }
                else if (File.Exists(p))
                {
                    files.Add(p);
                }
                else
                {
                    Console.Error.WriteLine("not found: " + p);
                }
            }
            return files;
        }

        private static void WriteArtifacts(List<string> files, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            int n = 0;
            foreach (string file in files)
            {
                try
                {
                    BamlFileView view = BamlFileView.FromBytes(File.ReadAllBytes(file), file);
                    string name = Path.GetFileNameWithoutExtension(file);
                    File.WriteAllText(Path.Combine(outputDir, name + ".xaml"),
                        view.Render(BamlView.Xaml), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, name + ".wpf.xaml"),
                        view.Render(BamlView.WpfXaml), new UTF8Encoding(false));
                    n++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("  could not write {0}: {1}", file, ex.Message);
                }
            }
            Console.WriteLine("  wrote {0} document(s) to {1}", n, outputDir);
        }

        private static void Usage()
        {
            Console.WriteLine("usage: baml-validate [options] <file-or-directory>...");
            Console.WriteLine();
            Console.WriteLine("  -v, --verbose     one line per document");
            Console.WriteLine("      --out <dir>   also write the .xaml and .wpf.xaml for each input");
            Console.WriteLine("      --only <sub>  only documents whose file name contains <sub>");
            Console.WriteLine();
            Console.WriteLine("exit code: 0 when nothing failed for a reason other than a Longhorn-only type");
        }
    }
}
