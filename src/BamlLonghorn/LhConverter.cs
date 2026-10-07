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
                string converted = ConvertTag(tag, options, report, ref anyUnsupported);
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
        /// Adds the presentation namespace to the first element when the document declares none.
        ///
        /// Several samples carry no namespace declaration, so their element names have no namespace
        /// and no WPF reader can resolve them. Injecting the declaration at the root is the minimal
        /// change that makes the names resolvable, and it is reported rather than done quietly.
        /// </summary>
        private static string InjectDefaultNamespace(string text, LhConversionReport report)
        {
            // find the end of the first opening tag's name
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

            report.NamespaceFixes.Add("no namespace declared -> added xmlns=\"" + WpfNamespace + "\"");
            return text.Substring(0, nameEnd)
                   + " xmlns=\"" + WpfNamespace + "\""
                   + text.Substring(nameEnd);
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

        /// <summary>Rewrites one complete tag: name, attributes, and so on.</summary>
        private static string ConvertTag(string tag, LhConversionOptions o,
            LhConversionReport report, ref bool anyUnsupported)
        {
            bool closing = tag.StartsWith("</", StringComparison.Ordinal);
            bool selfClosing = tag.EndsWith("/>", StringComparison.Ordinal);

            int nameStart = closing ? 2 : 1;
            int nameEnd = nameStart;
            while (nameEnd < tag.Length && !char.IsWhiteSpace(tag[nameEnd])
                   && tag[nameEnd] != '>' && tag[nameEnd] != '/')
                nameEnd++;

            string rawName = tag.Substring(nameStart, nameEnd - nameStart);
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
                    string shortened = shortName;
                    LhConversionReport.Bump(report.Shortened, rawName + " -> " + shortened);
                    // write it out and move to the next tag
                    StringBuilder sbn = new StringBuilder();
                    sbn.Append(closing ? "</" : "<").Append(shortened)
                       .Append(tag, nameEnd, tag.Length - nameEnd);
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

            // attribute rewriting only on an opening tag
            string body = tag.Substring(nameEnd, tag.Length - nameEnd);
            if (!closing && body.IndexOf('=') >= 0)
            {
                body = RewriteAttributes(rawName, body, report, o);
            }

            StringBuilder sb = new StringBuilder();
            if (comment != null && o.EmitComments && !closing)
            {
                sb.Append("<!-- ").Append(comment).Append(" -->");
            }
            sb.Append(closing ? "</" : "<").Append(newName).Append(body);
            return sb.ToString();
        }

        /// <summary>Renames attributes that the mapping table covers, leaving the rest alone.</summary>
        private static string RewriteAttributes(string owner, string body,
            LhConversionReport report, LhConversionOptions o)
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

                sb.Append(body, i, nameStart - i);

                LhAttributeMapping m = LhWpfMappings.FindAttribute(owner, name);
                if (m != null && !string.Equals(m.WpfName, name, StringComparison.Ordinal))
                {
                    sb.Append(m.WpfName);
                    if (m.Note != null) report.AttributeRenames.Add(owner + "." + name + " -> " + m.WpfName);
                }
                else
                {
                    sb.Append(name);
                }

                // copy the '=' and any spaces, then the quoted value
                sb.Append('=');
                int q = nameEnd + 1;
                while (q < body.Length && (body[q] == ' ' || body[q] == '\t')) { sb.Append(body[q]); q++; }
                if (q >= body.Length || (body[q] != '"' && body[q] != '\''))
                {
                    i = nameEnd + 1;
                    continue;
                }
                char quote = body[q];
                int closeQuote = body.IndexOf(quote, q + 1);
                if (closeQuote < 0) { sb.Append(body, q, body.Length - q); break; }
                sb.Append(body, q, closeQuote - q + 1);
                report.AttributesSeen++;
                i = closeQuote + 1;
            }
            return sb.ToString();
        }
    }
}
