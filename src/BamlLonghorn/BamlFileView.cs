using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using BamlLonghorn.Dialects;

namespace BamlLonghorn
{
    /// <summary>
    /// What a caller can ask a loaded document to show.
    ///
    /// Both front ends go through here — the CLI's subcommands and the GUI's tabs —
    /// so the two can never drift apart in what they render or how they fail.
    /// </summary>
    public enum BamlView
    {
        /// <summary>Detected dialect, confidence, size and parse completeness.</summary>
        Summary,

        /// <summary>XAML text, i.e. the decompiler output.</summary>
        Xaml,

        /// <summary>Every decoded record with offsets, sizes and payload fields.</summary>
        Records,

        /// <summary>The reconstructed markup tree.</summary>
        Tree,

        /// <summary>Assembly / type / attribute interning tables.</summary>
        Tables,

        /// <summary>Reconnaissance: string tokens and the bytes between them.</summary>
        Recon,

        /// <summary>
        /// The decompiled markup converted toward WPF, with a report of what would not convert.
        ///
        /// Separate from <see cref="Xaml"/> on purpose: that view is the faithful decompile, and
        /// this one is a transformation of it. Keeping them apart means the conversion can be
        /// judged against the document it came from, and a converter bug cannot hide by
        /// overwriting the evidence.
        /// </summary>
        WpfXaml
    }

    /// <summary>
    /// One file as a loadable, renderable unit.
    ///
    /// Holds the raw bytes, the detection result and the decoded document, and
    /// renders any <see cref="BamlView"/> on demand. Rendering is deliberately
    /// lazy: a 50 KB stream decodes to thousands of records and a tree walk is
    /// cheap, but there is no reason to pay for views nobody opens.
    /// </summary>
    public sealed class BamlFileView
    {
        private BamlDocument _document;
        private string _xaml;
        private string _xamlExpanded;
        private string _records;
        private string _tree;
        private string _tables;
        private string _recon;
        private string _summary;
        private string _wpfXaml;

        /// <summary>Full path, or a display name for in-memory data.</summary>
        public string Path { get; private set; }

        /// <summary>Raw BAML bytes.</summary>
        public byte[] Data { get; private set; }

        /// <summary>Byte length.</summary>
        public int Length { get { return Data == null ? 0 : Data.Length; } }

        /// <summary>Short name for lists.</summary>
        public string Name { get { return System.IO.Path.GetFileName(Path); } }

        /// <summary>Detected reader, or null when unrecognised.</summary>
        public IBamlDialectReader Reader { get; private set; }

        /// <summary>Detection confidence, 0..100.</summary>
        public int Confidence { get; private set; }

        /// <summary>Dialect label, or "(unrecognised)".</summary>
        public string DialectName
        {
            get { return Reader == null ? "(unrecognised)" : Reader.Name; }
        }

        /// <summary>True when a reader claimed the stream.</summary>
        public bool IsRecognised { get { return Reader != null; } }

        /// <summary>Error from loading, or null.</summary>
        public string LoadError { get; private set; }

        private BamlFileView() { }

        /// <summary>Loads and detects a file.</summary>
        public static BamlFileView FromFile(string path)
        {
            BamlFileView view = new BamlFileView();
            view.Path = path;
            try
            {
                view.Data = File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                view.Data = new byte[0];
                view.LoadError = ex.Message;
                return view;
            }
            view.Detect();
            return view;
        }

        /// <summary>Loads and detects an in-memory buffer.</summary>
        public static BamlFileView FromBytes(byte[] data, string displayName)
        {
            BamlFileView view = new BamlFileView();
            view.Path = displayName ?? "(memory)";
            view.Data = data ?? new byte[0];
            view.Detect();
            return view;
        }

        private void Detect()
        {
            int confidence = 0;
            try
            {
                Reader = BamlDetector.Detect(Data, out confidence);
            }
            catch (Exception ex)
            {
                Reader = null;
                LoadError = ex.Message;
            }
            Confidence = confidence;
        }

        /// <summary>The decoded document, or null when unrecognised or failed.</summary>
        public BamlDocument Document
        {
            get
            {
                if (_document == null && Reader != null && LoadError == null)
                {
                    try
                    {
                        _document = Reader.Read(Data);
                    }
                    catch (Exception ex)
                    {
                        LoadError = ex.Message;
                    }
                }
                return _document;
            }
        }

        /// <summary>True when the record walk consumed the stream exactly.</summary>
        public bool IsCompleteParse
        {
            get
            {
                BamlDocument d = Document;
                return d != null && d.IsCompleteParse;
            }
        }

        /// <summary>Note describing an incomplete parse, or empty.</summary>
        public string Note
        {
            get
            {
                BamlDocument d = Document;
                return d == null || string.IsNullOrEmpty(d.Note) ? string.Empty : d.Note;
            }
        }

        /// <summary>
        /// When set, the XAML view expands compound brush shorthands into structured
        /// markup instead of echoing the shorthand string.
        /// </summary>
        public bool ExpandCompoundBrushes { get; set; }

        /// <summary>Drops cached renders, so a changed option takes effect.</summary>
        public void InvalidateViews()
        {
            _xaml = null;
            _xamlExpanded = null;
            _records = null;
            _tree = null;
            _tables = null;
            _recon = null;
            _summary = null;
        }

        /// <summary>Renders the requested view, caching the result.</summary>
        public string Render(BamlView view)
        {
            switch (view)
            {
                case BamlView.Summary:
                    if (_summary == null) _summary = BuildSummary();
                    return _summary;

                case BamlView.Xaml:
                    if (ExpandCompoundBrushes)
                    {
                        if (_xamlExpanded == null) _xamlExpanded = BuildXaml();
                        return _xamlExpanded;
                    }
                    if (_xaml == null) _xaml = BuildXaml();
                    return _xaml;

                case BamlView.Records:
                    if (_records == null) _records = BuildRecords();
                    return _records;

                case BamlView.Tree:
                    if (_tree == null) _tree = BuildTree();
                    return _tree;

                case BamlView.Tables:
                    if (_tables == null) _tables = BuildTables();
                    return _tables;

                case BamlView.Recon:
                    if (_recon == null) _recon = BuildRecon();
                    return _recon;

                case BamlView.WpfXaml:
                    if (_wpfXaml == null) _wpfXaml = BuildWpfXaml();
                    return _wpfXaml;

                default:
                    return string.Empty;
            }
        }

        private string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("file       : ");
            sb.AppendLine(Path);
            sb.Append("size       : ");
            sb.Append(Length.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(" bytes");
            sb.Append("dialect    : ");
            sb.Append(DialectName);
            if (IsRecognised)
            {
                sb.Append("  (");
                sb.Append(Confidence.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("% confidence)");
            }
            else
            {
                sb.AppendLine();
            }

            if (LoadError != null)
            {
                sb.Append("error      : ");
                sb.AppendLine(LoadError);
                return sb.ToString();
            }

            BamlDocument d = Document;
            if (d == null)
            {
                return sb.ToString();
            }

            sb.Append("records    : ");
            sb.Append(d.Entries.Count > 0
                ? d.Entries.Count.ToString(CultureInfo.InvariantCulture)
                : d.Records.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.Append("complete   : ");
            sb.AppendLine(d.IsCompleteParse ? "yes" : "no");
            if (!string.IsNullOrEmpty(d.Note))
            {
                sb.Append("note       : ");
                sb.AppendLine(d.Note);
            }

            // record histogram, which is the quickest way to see the shape of a file
            if (d.Entries.Count > 0)
            {
                Dictionary<string, int> histogram = new Dictionary<string, int>();
                for (int i = 0; i < d.Entries.Count; i++)
                {
                    string k = d.Entries[i].Name;
                    int n;
                    histogram.TryGetValue(k, out n);
                    histogram[k] = n + 1;
                }
                sb.AppendLine();
                sb.AppendLine("record histogram:");
                List<KeyValuePair<string, int>> pairs =
                    new List<KeyValuePair<string, int>>(histogram);
                pairs.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                {
                    int c = b.Value.CompareTo(a.Value);
                    return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
                });
                for (int i = 0; i < pairs.Count; i++)
                {
                    sb.Append("  ");
                    sb.Append(pairs[i].Value.ToString(CultureInfo.InvariantCulture).PadLeft(6));
                    sb.Append("  ");
                    sb.AppendLine(pairs[i].Key);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts the decompiled markup toward WPF and appends a report.
        ///
        /// The report is emitted inside the view rather than kept beside it because a converter
        /// that quietly renames or drops what it cannot handle produces output that looks
        /// complete. Printing the counts and the names of the un-convertible elements in the same
        /// pane means the reader cannot mistake the transformation for lossless.
        /// </summary>
        private string BuildWpfXaml()
        {
            if (LoadError != null)
            {
                return "cannot convert: " + LoadError + Environment.NewLine;
            }
            BamlDocument d = Document;
            if (d == null)
            {
                return "stream is not recognised as any supported BAML generation."
                       + Environment.NewLine;
            }
            if (d.Entries.Count == 0)
            {
                return "conversion requires a record-level decode, which this dialect does not have."
                       + Environment.NewLine;
            }

            BamlXamlWriter.Options wopts = new BamlXamlWriter.Options();
            wopts.ExpandCompoundBrushes = true;
            string lh = BamlXamlWriter.Write(d, wopts);

            LhConversionReport report;
            string wpf = LhConverter.Convert(lh, new LhConversionOptions(), out report);

            StringBuilder sb = new StringBuilder(wpf.Length + 2048);
            sb.Append(wpf);

            sb.AppendLine();
            sb.AppendLine();
            // The report body is built separately and sanitised before being wrapped in a
            // comment. Its group headings use a run of hyphens as a separator, and XML forbids
            // a double hyphen anywhere inside a comment, so emitting it verbatim made the whole
            // document unloadable -- "a comment cannot contain '--'".
            StringBuilder reportText = new StringBuilder(1024);
            reportText.AppendLine("  conversion report");
            if (!report.IsLossless)
            {
                reportText.AppendLine("  NOTE: some elements have no WPF equivalent and keep their own");
                reportText.AppendLine("  name under the prefix below. Markup that uses them will not load");
                reportText.AppendLine("  in WPF until they are replaced by hand.");
            }
            reportText.AppendLine("    elements seen        : " + report.ElementsSeen.ToString(CultureInfo.InvariantCulture));
            reportText.AppendLine("    attributes seen      : " + report.AttributesSeen.ToString(CultureInfo.InvariantCulture));
            reportText.AppendLine("    renamed elements     : " + report.Renamed.Count.ToString(CultureInfo.InvariantCulture));
            reportText.AppendLine("    substituted elements : " + report.Substituted.Count.ToString(CultureInfo.InvariantCulture));
            reportText.AppendLine("    unconvertible        : " + report.Unsupported.Count.ToString(CultureInfo.InvariantCulture));
            reportText.AppendLine("    WPF types available  : " + WpfTypeIndex.TypeCount.ToString(CultureInfo.InvariantCulture)
                          + " (" + WpfTypeIndex.Source + ")");

            AppendGroup(reportText, "renamed (same role, different name)", report.Renamed);
            AppendGroup(reportText, "substituted (no exact equivalent)", report.Substituted);
            AppendGroup(reportText, "unconvertible (kept under the lh prefix)", report.Unsupported);

            if (report.AttributeRenames.Count > 0)
            {
                reportText.AppendLine("    --- attributes renamed ---");
                for (int i = 0; i < report.AttributeRenames.Count; i++)
                {
                    reportText.AppendLine("      " + report.AttributeRenames[i]);
                }
            }
            sb.AppendLine("<!--");
            sb.Append(BamlXamlWriter.SafeComment(reportText.ToString()));
            sb.AppendLine("-->");
            return sb.ToString();
        }

        /// <summary>Appends one counted group to the conversion report, largest first.</summary>
        private static void AppendGroup(StringBuilder sb, string title,
            List<KeyValuePair<string, int>> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine("    --- " + title + " ---");
            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(items);
            sorted.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            for (int i = 0; i < sorted.Count; i++)
            {
                sb.AppendLine("      " + sorted[i].Value.ToString(CultureInfo.InvariantCulture).PadLeft(6)
                              + "  " + sorted[i].Key);
            }
        }

        private string BuildXaml()
        {            if (LoadError != null)
            {
                return "cannot decompile: " + LoadError + Environment.NewLine;
            }
            BamlDocument d = Document;
            if (d == null)
            {
                return "stream is not recognised as any supported BAML generation."
                       + Environment.NewLine;
            }
            if (d.Entries.Count == 0 && d.Records.Count == 0)
            {
                return "this dialect's record layer is not decoded, so XAML output is "
                       + "not available (see Records or Recon)." + Environment.NewLine;
            }
            if (d.Entries.Count == 0)
            {
                return "XAML output requires a record-level decode, which this dialect "
                       + "does not provide yet." + Environment.NewLine;
            }
            return BamlXamlWriter.Write(d);
        }

        private string BuildRecords()
        {
            BamlDocument d = Document;
            if (d == null)
            {
                return LoadError == null ? "(no document)" + Environment.NewLine : LoadError;
            }
            return d.Entries.Count > 0
                ? BamlPrinter.DumpEntries(d)
                : BamlPrinter.DumpRecords(d);
        }

        private string BuildTree()
        {
            BamlDocument d = Document;
            if (d == null)
            {
                return LoadError == null ? "(no document)" + Environment.NewLine : LoadError;
            }
            return d.Entries.Count > 0
                ? BamlShortTreeBuilder.Render(d)
                : BamlPrinter.DumpTree(d);
        }

        private string BuildTables()
        {
            BamlDocument d = Document;
            if (d == null)
            {
                return LoadError == null ? "(no document)" + Environment.NewLine : LoadError;
            }
            if (d.Entries.Count > 0)
            {
                return BamlPrinter.DumpEntriesTables(d);
            }
            return BamlPrinter.DumpInternTables(d);
        }

        private string BuildRecon()
        {
            BamlDocument d = Document;
            if (d == null || d.Strings == null)
            {
                return "(no reconnaissance data for this dialect)" + Environment.NewLine;
            }
            return BamlPrinter.Dump4074Recon(Data, d, 24);
        }
    }
}
