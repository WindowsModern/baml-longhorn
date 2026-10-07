using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>A summary of what a conversion did.</summary>
    public sealed class LhConversionReport
    {
        /// <summary>Element name to occurrence count, for elements kept as lh:.</summary>
        public readonly List<KeyValuePair<string, int>> Unsupported = new List<KeyValuePair<string, int>>();

        /// <summary>Element name renames applied, with counts.</summary>
        public readonly List<KeyValuePair<string, int>> Renamed = new List<KeyValuePair<string, int>>();

        /// <summary>Role substitutions applied, with counts.</summary>
        public readonly List<KeyValuePair<string, int>> Substituted = new List<KeyValuePair<string, int>>();

        /// <summary>
        /// Element names shortened from a full CLR type name to the short form WPF expects, such as
        /// <c>System.Windows.Controls.Canvas</c> to <c>Canvas</c>. Reported apart from a rename
        /// because it is not one: the type is unchanged and only its qualification is dropped.
        /// </summary>
        public readonly List<KeyValuePair<string, int>> Shortened = new List<KeyValuePair<string, int>>();

        /// <summary>
        /// Percentage lengths that were translated or dropped. Reported because a partial
        /// percentage cannot be expressed in WPF at all, so those attributes are removed and the
        /// layout differs from the original rather than being translated.
        /// </summary>
        public readonly List<string> PercentLengths = new List<string>();

        /// <summary>Attribute renames applied, as "owner.attribute -> new".</summary>
        public readonly List<string> AttributeRenames = new List<string>();

        /// <summary>
        /// Namespace URIs that were normalised, as "before -> after". Recorded because the change is
        /// not cosmetic: a doubled URI cannot be matched by an XML reader, so the document would fail
        /// to load for a reason unrelated to the conversion.
        /// </summary>
        public readonly List<string> NamespaceFixes = new List<string>();

        public int ElementsSeen;
        public int AttributesSeen;

        public bool IsLossless
        {
            get { return Unsupported.Count == 0; }
        }

        internal static void Bump(List<KeyValuePair<string, int>> list, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Key == key)
                {
                    list[i] = new KeyValuePair<string, int>(key, list[i].Value + 1);
                    return;
                }
            }
            list.Add(new KeyValuePair<string, int>(key, 1));
        }
    }

    /// <summary>Options for <see cref="LhConverter"/>.</summary>
    public sealed class LhConversionOptions
    {
        /// <summary>
        /// The XML namespace to emit for elements that have no WPF equivalent. Defaults to
        /// <c>lh</c>, which keeps them distinguishable and easy to search for.
        /// </summary>
        public string UnsupportedPrefix = "lh";

        /// <summary>The URI bound to that prefix.</summary>
        public string UnsupportedNamespace = "urn:longhorn-baml";

        /// <summary>Emit a comment above each element that was renamed, substituted or kept.</summary>
        public bool EmitComments = true;

        /// <summary>Indent unit.</summary>
        public string Indent = "  ";
    }

    /// <summary>
    /// Converts decompiled Longhorn markup into XAML that a current WPF can read.
    ///
    /// The conversion is deliberately conservative and its output says what it did. Three
    /// operations happen, in order:
    ///
    ///   1. The namespace declaration is rewritten. Longhorn writes a single
    ///      <c>using:</c> directive listing CLR namespaces; WPF wants the presentation
    ///      namespace. A prefix for the un-convertible elements is added alongside it.
    ///   2. Element and attribute names are rewritten through <see cref="LhWpfMappings"/>.
    ///   3. Anything with no WPF equivalent keeps its own name, is moved into the
    ///      <c>lh</c> namespace, and is annotated.
    ///
    /// Step 3 is the important one. Marking is not a fallback for missing work; it is the honest
    /// answer for the large part of the Longhorn vocabulary that never existed in WPF. Roughly
    /// three quarters of the element types in the sample corpora are shell and explorer types
    /// from <c>MS.Internal.Desktop.*</c> and <c>System.Windows.WCPExplorer.*</c>, and inventing
    /// mappings for them would produce markup that compiles and does something else.
    /// </summary>
    public static class LhConverter
    {
        public const string WpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        /// <summary>Converts decompiled markup, returning the new markup.</summary>
        public static string Convert(string xaml, LhConversionOptions options, out LhConversionReport report)
        {
            if (options == null) options = new LhConversionOptions();
            report = new LhConversionReport();
            if (string.IsNullOrEmpty(xaml)) return xaml;

            StringBuilder sb = new StringBuilder(xaml.Length * 2);
            int i = 0;
            bool anyUnsupported = false;

            // The enclosing element, so that a bare positioning attribute can be resolved to the
            // attached property of whichever panel owns it. Left on a child of a Canvas is
            // Canvas.Left; Dock on a child of a DockPanel is DockPanel.Dock. There is no way to
            // decide that from the attribute name alone, and getting it wrong is what produced
            // "cannot set unknown member TextBlock.Dock".
            Stack<string> parentStack = new Stack<string>();

            while (i < xaml.Length)
            {
                int lt = xaml.IndexOf('<', i);
                if (lt < 0)
                {
                    sb.Append(xaml, i, xaml.Length - i);
                    break;
                }
                // text between tags
                sb.Append(xaml, i, lt - i);

                // comment or declaration: copy through untouched
                if (lt + 1 < xaml.Length && (xaml[lt + 1] == '!' || xaml[lt + 1] == '?'))
                {
                    int close = xaml.IndexOf('>', lt);
                    if (close < 0) { sb.Append(xaml, lt, xaml.Length - lt); break; }
                    sb.Append(xaml, lt, close - lt + 1);
                    i = close + 1;
                    continue;
                }

                int gt = FindTagEnd(xaml, lt);
                if (gt < 0) { sb.Append(xaml, lt, xaml.Length - lt); break; }

                string tag = xaml.Substring(lt, gt - lt + 1);
                string parent = parentStack.Count > 0 ? parentStack.Peek() : null;
                string converted = ConvertTag(tag, options, report, parent, ref anyUnsupported,
                    parentStack);
                sb.Append(converted);
                i = gt + 1;
            }

            string result = sb.ToString();

            // Rewrite namespace declarations: collapse the doubled slashes the 2005 generation
            // writes, and replace Longhorn's presentation namespace with the one WPF resolves
            // against. Applied to the finished text rather than inside the tag pass so it covers
            // every xmlns however it was introduced, and so the rule lives in one place.
            if (result.IndexOf("xmlns", StringComparison.Ordinal) >= 0)
            {
                result = FixNamespaces(result, report);
            }

            // A document with no namespace declaration at all is equally unresolvable, and some
            // samples are in exactly that state: HelloWorld declares nothing, so a WPF reader
            // reports "cannot create unknown type Canvas". Adding the presentation namespace to the
            // root element is the smallest fix, and it is recorded so the addition is visible.
            if (result.IndexOf(WpfNamespace, StringComparison.Ordinal) < 0)
            {
                result = InjectDefaultNamespace(result, report);
            }

            // Names such as x:Name come from the mappings, and an undeclared prefix is itself a
            // parse failure: "'x' is an undeclared prefix". The declaration is required whenever the
            // output uses an x: name, whether or not the source declared one.
            if (result.IndexOf("x:", StringComparison.Ordinal) >= 0
                && result.IndexOf("xmlns:x=", StringComparison.Ordinal) < 0)
            {
                result = InjectDeclaration(result, "xmlns:x", XamlNamespace, report,
                    "used by a mapped attribute such as ID to x:Name");
            }

            // add the lh prefix declaration beside the presentation namespace
            if (anyUnsupported)
            {
                string decl = "xmlns:" + options.UnsupportedPrefix + "=\"" + options.UnsupportedNamespace + "\"";
                int idx = result.IndexOf("xmlns=", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int space = result.IndexOf(' ', idx);
                    if (space < 0) space = result.IndexOf('>', idx);
                    if (space > 0) result = result.Substring(0, space) + " " + decl + result.Substring(space);
                }
            }

            return result;
        }

        /// <summary>
        /// Rewrites namespace declarations so a WPF reader can resolve them.
        ///
        /// Two distinct problems are fixed here, and the second is the one that decides whether the
        /// output can be loaded at all.
        ///
        /// The 2005 generation writes the presentation namespace with every slash doubled, and the
        /// plain form of that URI appears in the same corpus, which identifies the plain form as
        /// intended.
        ///
        /// More importantly, Longhorn's presentation namespace is not WPF's. A Longhorn document
        /// declares <c>http://schemas.microsoft.com/2005/xaml/</c> (or the 2003 variant, or a
        /// <c>using:</c> directive), and no current WPF reader resolves the unprefixed element names
        /// against any of those -- it fails immediately with "cannot create unknown type Canvas".
        /// The default namespace therefore has to become the presentation namespace that WPF expects,
        /// and a prefix it may already use has to be removed so the two do not collide.
        ///
        /// PIMapping-derived prefixes such as <c>fullexp</c> are left alone: they map to CLR
        /// namespaces of application types, not to WPF, and the elements that use them are marked
        /// un-convertible anyway.
        /// </summary>
        private static string FixNamespaces(string text, LhConversionReport report)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                int at = text.IndexOf("xmlns", i, StringComparison.Ordinal);
                if (at < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, at - i);

                int p = at;
                while (p < text.Length && text[p] != '=' && text[p] != '>') p++;
                string name = text.Substring(at, p - at);
                sb.Append(name);
                if (p >= text.Length || text[p] != '=')
                {
                    i = p;
                    continue;
                }
                sb.Append('=');
                p++;
                while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
                {
                    sb.Append(text[p]);
                    p++;
                }
                if (p >= text.Length || (text[p] != '"' && text[p] != '\''))
                {
                    i = p;
                    continue;
                }
                char quote = text[p];
                int close = text.IndexOf(quote, p + 1);
                if (close < 0)
                {
                    sb.Append(text, p, text.Length - p);
                    break;
                }

                string value = text.Substring(p + 1, close - p - 1);
                string fixedValue = LhWpfMappings.UndoubleSlashes(value);

                bool isDefault = string.Equals(name, "xmlns", StringComparison.Ordinal);
                bool isLonghornPresentation =
                    fixedValue.IndexOf("schemas.microsoft.com/2005/xaml", StringComparison.Ordinal) >= 0
                    || fixedValue.IndexOf("schemas.microsoft.com/2003/xaml", StringComparison.Ordinal) >= 0
                    || fixedValue.StartsWith("using:", StringComparison.Ordinal);

                if (isDefault && isLonghornPresentation)
                {
                    report.NamespaceFixes.Add(fixedValue + " -> " + WpfNamespace
                                              + "  (default namespace: Longhorn presentation to WPF)");
                    fixedValue = WpfNamespace;
                }
                else if (!isDefault && isLonghornPresentation
                         && string.Equals(name, "xmlns:x", StringComparison.Ordinal))
                {
                    // xmlns:x must remain the XAML language namespace; a Longhorn document that
                    // reuses that prefix for a presentation namespace would shadow it
                    report.NamespaceFixes.Add(fixedValue + " -> dropped from xmlns:x");
                    fixedValue = XamlNamespace;
                }
                else if (!string.Equals(fixedValue, value, StringComparison.Ordinal))
                {
                    report.NamespaceFixes.Add(value + " -> " + fixedValue);
                }

                sb.Append(quote).Append(fixedValue).Append(quote);
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Adds a namespace declaration to the first element.
        ///
        /// Used both for the presentation namespace when a document declares none and for the x:
        /// prefix when a mapped attribute introduced one, since both are cases where the markup is
        /// otherwise correct and only a declaration is missing.
        /// </summary>
        private static string InjectDeclaration(string text, string name, string value,
            LhConversionReport report, string why)
        {
            // find the end of the first opening tag's name, skipping comments and declarations
            int lt = text.IndexOf('<');
            while (lt >= 0 && lt + 1 < text.Length
                   && (text[lt + 1] == '!' || text[lt + 1] == '?'))
            {
                int skip = text.IndexOf('>', lt);
                if (skip < 0) return text;
                lt = text.IndexOf('<', skip);
            }
            if (lt < 0) return text;

            int nameEnd = lt + 1;
            while (nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd])
                   && text[nameEnd] != '>' && text[nameEnd] != '/')
            {
                nameEnd++;
            }

            report.NamespaceFixes.Add(name + "=\"" + value + "\" added (" + why + ")");
            return text.Substring(0, nameEnd)
                   + " " + name + "=\"" + value + "\""
                   + text.Substring(nameEnd);
        }

        /// <summary>
        /// Adds the presentation namespace to the first element when the document declares none.
        /// </summary>
        private static string InjectDefaultNamespace(string text, LhConversionReport report)
        {
            return InjectDeclaration(text, "xmlns", WpfNamespace, report,
                "no namespace was declared; element names would be unresolvable");
        }

        /// <summary>
        /// Translates a percentage length into something WPF accepts, or returns null when the value
        /// is not a percentage.
        ///
        /// Longhorn's Length carried a unit and <c>Percent</c> was one of them. WPF's LengthConverter
        /// has no percentage unit at all -- it parses absolute lengths and <c>Auto</c> -- so
        /// <c>Width="100%"</c> fails with "cannot create Width from the text 100%", and renaming
        /// cannot help.
        ///
        /// The measured distribution decides the treatment. Of the pairs carrying a percentage, the
        /// common ones by a wide margin are Width=100% with Height=100%, and Width=100% alone; the
        /// rest are 10, 50, 70, 75 and 80 percent. A 100% dimension means "take the space the parent
        /// offers", which WPF expresses as the corresponding alignment, so those become
        /// Stretch and the attribute is dropped. That is the same layout rather than an approximation
        /// of it.
        ///
        /// A partial percentage is where the layouts genuinely differ: WPF has no way to say "70% of
        /// the parent". Those are also dropped, and reported, because the alternative is emitting a
        /// value that cannot parse, and inventing a Grid or a converter would change the document's
        /// structure rather than translate it. The caller records all of these so the loss is visible.
        /// </summary>
        private static string PercentReplacement(string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value[value.Length - 1] != '%') return null;

            bool isWidth = string.Equals(name, "Width", StringComparison.Ordinal);
            bool isHeight = string.Equals(name, "Height", StringComparison.Ordinal);

            if (isWidth || isHeight)
            {
                string number = value.Substring(0, value.Length - 1).Trim();
                double pct;
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out pct))
                {
                    return null;
                }

                if (pct >= 100.0)
                {
                    return isWidth
                        ? "HorizontalAlignment=\"Stretch\""
                        : "VerticalAlignment=\"Stretch\"";
                }

                // A partial percentage has no WPF equivalent, so the attribute goes. Nothing is
                // emitted in its place: XML does not allow a comment inside a tag, so
                // <X <!-- Width dropped --> > is not merely ugly, it does not parse. An empty string
                // removes the attribute, and the caller records it so the loss is still visible --
                // through the report rather than through broken markup.
                return string.Empty;
            }

            // Canvas.Left and the rest cannot be a percentage in WPF either; the element is positioned
            // by absolute coordinates or not at all, so the attribute is dropped
            if (name.IndexOf("Canvas.", StringComparison.Ordinal) >= 0
                || string.Equals(name, "Left", StringComparison.Ordinal)
                || string.Equals(name, "Top", StringComparison.Ordinal)
                || string.Equals(name, "Right", StringComparison.Ordinal)
                || string.Equals(name, "Bottom", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return null;
        }

        /// <summary>
        /// Makes a string safe to place inside an XML comment.
        ///
        /// XML forbids a double hyphen anywhere inside a comment and a trailing hyphen at its end, and
        /// a reader rejects the whole document when it finds one -- "a comment cannot contain '--' and
        /// cannot end with '-'". The converter annotates renaming decisions with text that can contain
        /// both, because a type name is not under its control, so the text is sanitised rather than
        /// assumed safe. This accounted for 18 of 103 documents failing to load.
        /// </summary>
        private static string SafeComment(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            // a single hyphen is fine; a run of two or more is not
            StringBuilder sb = new StringBuilder(text.Length + 8);
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] != '-') { sb.Append(text[i]); i++; continue; }
                int j = i;
                while (j < text.Length && text[j] == '-') j++;
                for (int k = i; k < j; k++)
                {
                    sb.Append('-');
                    if (k + 1 < j) sb.Append(' ');   // break the run
                }
                i = j;
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == '-') sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>Finds the '&gt;' that closes a tag, skipping quoted attribute values.</summary>
        private static int FindTagEnd(string s, int start)        {
            char quote = '\0';
            for (int i = start + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return i;
            }
            return -1;
        }

        /// <summary>
        /// Rewrites one complete tag: its name, its attributes, and the parent stack.
        ///
        /// The stack is maintained here because a closing tag is the only place a parent goes out of
        /// scope, and an owner is needed while the opening tag is being rewritten -- which is where a
        /// bare positioning attribute has to be resolved to the attached property of the panel that
        /// will actually read it.
        /// </summary>
        private static string ConvertTag(string tag, LhConversionOptions o,
            LhConversionReport report, string parent, ref bool anyUnsupported,
            Stack<string> parentStack)
        {
            bool closing = tag.StartsWith("</", StringComparison.Ordinal);
            bool selfClosing = tag.EndsWith("/>", StringComparison.Ordinal);

            int nameStart = closing ? 2 : 1;
            int nameEnd = nameStart;
            while (nameEnd < tag.Length && !char.IsWhiteSpace(tag[nameEnd])
                   && tag[nameEnd] != '>' && tag[nameEnd] != '/')
                nameEnd++;

            string rawName = tag.Substring(nameStart, nameEnd - nameStart);

            if (closing)
            {
                // the enclosing element ends with this tag
                if (parentStack.Count > 0) parentStack.Pop();
            }

            LhElementMapping map = LhWpfMappings.FindElement(rawName);
            report.ElementsSeen++;

            string newName;
            string comment = null;

            // Longhorn markup names elements by their full CLR type, so a document contains
            // <System.Windows.Controls.Canvas> where WPF expects <Canvas>. This is the single change
            // that most improves whether converted output loads at all: without it every dotted name
            // is unresolvable and a WPF reader reports "cannot create unknown type" or "unknown
            // member" for the file.
            //
            // The shortening is safe because it is the same type: only the namespace qualification
            // is dropped, and it is applied only when the WPF type index confirms a type of that
            // name, so an application type that happens to be dotted is not mangled.
            if (map == null && rawName.IndexOf('.') >= 0)
            {
                string shortName = rawName.Substring(rawName.LastIndexOf('.') + 1);
                if (WpfTypeIndex.Exists(rawName) || WpfTypeIndex.Exists(shortName))
                {
                    LhConversionReport.Bump(report.Shortened, rawName + " -> " + shortName);

                    // The attributes must still be rewritten. Returning early here with the body
                    // copied verbatim was a real defect: it skipped every attribute rule for any
                    // element whose name is a full CLR type name, which is most of them, so a
                    // percentage length, a Dock or an ID on those elements was emitted untouched and
                    // the document could not load. The short name is used for the owner lookup
                    // because that is the type the reader will resolve.
                    StringBuilder sbn = new StringBuilder();
                    if (!closing && comment != null && o.EmitComments)
                    {
                        sbn.Append("<!-- ").Append(comment).Append(" -->");
                    }
                    sbn.Append(closing ? "</" : "<").Append(shortName);
                    string shortBody = tag.Substring(nameEnd, tag.Length - nameEnd);
                    if (!closing && shortBody.IndexOf('=') >= 0)
                    {
                        shortBody = RewriteAttributes(shortName, shortBody, report, o, parent);
                    }
                    sbn.Append(shortBody);
                    if (!closing && !selfClosing)
                    {
                        parentStack.Push(shortName);
                    }
                    return sbn.ToString();
                }
            }

            if (map == null)
            {
                // not listed: decide by whether WPF has the type at all. The mapping table only
                // carries the entries that were verified either way, so an unlisted name is left
                // alone when it is a WPF type and marked when it is not.
                if (WpfTypeIndex.Exists(rawName))
                {
                    newName = rawName;
                }
                else
                {
                    newName = o.UnsupportedPrefix + ":" + rawName;
                    LhConversionReport.Bump(report.Unsupported, rawName);
                    anyUnsupported = true;
                    comment = rawName + " has no WPF equivalent; kept as " + newName;
                }
            }
            else
            {
                switch (map.Kind)
                {
                    case LhMapKind.Identical:
                        newName = rawName;
                        break;
                    case LhMapKind.Renamed:
                        newName = map.WpfName;
                        LhConversionReport.Bump(report.Renamed, rawName + " -> " + map.WpfName);
                        comment = rawName + " -> " + map.WpfName + " (" + map.Note + ")";
                        break;
                    case LhMapKind.Substituted:
                        newName = map.WpfName;
                        LhConversionReport.Bump(report.Substituted, rawName + " -> " + map.WpfName);
                        comment = rawName + " has no direct equivalent; " + map.WpfName +
                                  " substituted (" + map.Note + ")";
                        break;
                    default:
                        newName = o.UnsupportedPrefix + ":" + rawName;
                        LhConversionReport.Bump(report.Unsupported, rawName);
                        anyUnsupported = true;
                        comment = rawName + " has no WPF equivalent (" + map.Note + ")";
                        break;
                }
            }

            // attribute rewriting only on an opening tag; the parent decides how a bare positioning
            // attribute is qualified
            string body = tag.Substring(nameEnd, tag.Length - nameEnd);
            if (!closing && body.IndexOf('=') >= 0)
            {
                body = RewriteAttributes(rawName, body, report, o, parent);
            }

            StringBuilder sb = new StringBuilder();
            if (comment != null && o.EmitComments && !closing)
            {
                sb.Append("<!-- ").Append(SafeComment(comment)).Append(" -->");
            }
            sb.Append(closing ? "</" : "<").Append(newName).Append(body);

            // an opening tag that is not self-closing becomes the parent of what follows
            if (!closing && !selfClosing)
            {
                parentStack.Push(newName);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Renames attributes that the mapping table covers, leaving the rest alone.
        ///
        /// The parent element is passed through because a bare positioning name has no fixed owner:
        /// Dock belongs to DockPanel and Left to Canvas, and which applies is decided by the panel the
        /// element sits in, not by the element or the attribute.
        /// </summary>
        private static string RewriteAttributes(string owner, string body,
            LhConversionReport report, LhConversionOptions o, string parent)
        {
            StringBuilder sb = new StringBuilder(body.Length);
            int i = 0;
            while (i < body.Length)
            {
                int eq = body.IndexOf('=', i);
                if (eq < 0) { sb.Append(body, i, body.Length - i); break; }

                // walk back over the attribute name
                int nameEnd = eq;
                int nameStart = nameEnd;
                while (nameStart > i && !char.IsWhiteSpace(body[nameStart - 1])) nameStart--;
                string name = body.Substring(nameStart, nameEnd - nameStart);

                // read the value before writing anything, because a percentage length may suppress the
                // attribute entirely and replace it with alignments
                int q = nameEnd + 1;
                while (q < body.Length && (body[q] == ' ' || body[q] == '\t')) q++;
                if (q >= body.Length || (body[q] != '"' && body[q] != '\''))
                {
                    // no value; copy the name through and move on
                    sb.Append(body, i, nameEnd - i);
                    i = nameEnd;
                    continue;
                }
                char quote = body[q];
                int closeQuote = body.IndexOf(quote, q + 1);
                if (closeQuote < 0)
                {
                    sb.Append(body, i, body.Length - i);
                    break;
                }
                string value = body.Substring(q + 1, closeQuote - q - 1);

                sb.Append(body, i, nameStart - i);

                // Resolve the name first, then decide what to do with the value. The order matters:
                // a percentage length has to be judged against the property it will actually occupy,
                // and Longhorn's name for it is not always Width. RectangleWidth, for instance, maps
                // to Width, so checking before the rename missed it and emitted
                // RectangleWidth="100%" -- a member WPF has never heard of.
                string effectiveName = name;
                string attached = LhWpfMappings.ResolveAttached(parent, name);
                if (attached != null)
                {
                    sb.Append(attached);
                    report.AttributeRenames.Add(owner + "." + name + " -> " + attached
                                                + "  (attached property of " + parent + ")");
                    effectiveName = attached;
                }
                else
                {
                    LhAttributeMapping am = LhWpfMappings.FindAttribute(owner, name);
                    if (am != null && am.WpfName == null)
                    {
                        // the mapping says WPF has no such member, so the attribute is removed rather
                        // than emitted; a name no WPF type declares cannot be set and fails the parse
                        report.AttributeRenames.Add(owner + "." + name + " dropped (" + am.Note + ")");
                        sb.Length -= name.Length;
                        i = closeQuote + 1;
                        report.AttributesSeen++;
                        continue;
                    }
                    if (am != null && !string.Equals(am.WpfName, name, StringComparison.Ordinal))
                    {
                        sb.Append(am.WpfName);
                        if (am.Note != null)
                            report.AttributeRenames.Add(owner + "." + name + " -> " + am.WpfName);
                        effectiveName = am.WpfName;
                    }
                    else
                    {
                        sb.Append(name);
                    }
                }

                // A percentage length is then handled against the resolved name, because WPF's
                // LengthConverter rejects it outright -- "cannot create Width from the text 100%" --
                // and no renaming changes that. Longhorn's Length carried a unit; WPF's has no
                // percentage unit at all.
                string percentReplacement = PercentReplacement(effectiveName, value);
                if (percentReplacement != null)
                {
                    // the name has already been written, so undo it and emit the replacement instead
                    sb.Length -= effectiveName.Length;
                    sb.Append(percentReplacement);
                    report.PercentLengths.Add(name + "=\"" + value + "\"" + (percentReplacement.Length == 0
                        ? " dropped (WPF cannot express a percentage length)"
                        : " -> " + percentReplacement));
                    report.AttributesSeen++;
                    i = closeQuote + 1;
                    continue;
                }

                // the value's position was located above, before the name was written, so it is
                // simply copied through here
                sb.Append(body, nameEnd, closeQuote - nameEnd + 1);
                report.AttributesSeen++;
                i = closeQuote + 1;
            }
            return sb.ToString();
        }
    }
}
