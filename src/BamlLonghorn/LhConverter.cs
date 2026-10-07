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

        /// <summary>Attribute renames applied, as "owner.attribute -> new".</summary>
        public readonly List<string> AttributeRenames = new List<string>();

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

        /// <summary>Finds the '&gt;' that closes a tag, skipping quoted attribute values.</summary>
        private static int FindTagEnd(string s, int start)
        {
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
