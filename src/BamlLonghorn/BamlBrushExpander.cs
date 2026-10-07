using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// Expands a compound brush string into structured markup.
    ///
    /// WHY THIS EXISTS
    ///
    /// A brush reaches the decoder by one of two routes. Most arrive as an
    /// ElementStart naming the brush type, with the brush's own properties as child
    /// records, and those are already structured by the ordinary element walk. But a
    /// few arrive as a PropertyCustom whose value is a tagged string, and that string
    /// uses the shorthand grammar of <c>MS.Internal.Parsers.ParseBrush</c>. On its own
    /// the string is opaque: nothing in the stream says what the brush would have
    /// become.
    ///
    /// The grammar is transcribed from the build-4074 PresentationCore decompile
    /// (`MS.Internal\Parsers.cs`, ParseBrush):
    ///
    /// <code>
    /// HorizontalGradient  c1 c2            LinearGradientBrush(c1, c2, (0,0), (1,0))
    /// VerticalGradient    c1 c2            LinearGradientBrush(c1, c2, (0,0), (0,1))
    /// RadialGradient      c1 c2            RadialGradientBrush(c1, c2)
    /// LinearGradient      x1 y1 x2 y2 c1 c2   explicit points, stops at 0.0 and 1.0
    /// Image               uri              ImageBrush
    /// NineGrid            ...              NineGridBrush
    /// </code>
    ///
    /// Everything else that ParseBrush accepts is either a plain colour (already
    /// rendered correctly, since a SolidColorBrush from a colour string needs no
    /// expansion) or a form this project deliberately does not expand.
    /// </summary>
    public static class BamlBrushExpander
    {
        /// <summary>An expansion result: either markup or "not a compound brush".</summary>
        public sealed class Expansion
        {
            /// <summary>True when the value was recognised and expanded.</summary>
            public bool Expanded { get; internal set; }

            /// <summary>Structured markup, indented but not prefixed.</summary>
            public string Markup { get; internal set; }

            /// <summary>One-line summary for a tooltip or a log.</summary>
            public string Summary { get; internal set; }

            /// <summary>Why expansion was refused, when it was.</summary>
            public string Reason { get; internal set; }
        }

        private static readonly Expansion NotCompound = new Expansion
        {
            Expanded = false,
            Reason = "not a compound brush"
        };

        /// <summary>
        /// Expands <paramref name="value"/> if it is a compound brush string.
        ///
        /// <paramref name="indent"/> is the leading whitespace to place before the
        /// first line, so the caller can nest the result at the right depth.
        /// </summary>
        public static Expansion Expand(string value, string indent)
        {
            if (string.IsNullOrEmpty(value))
            {
                return NotCompound;
            }
            if (indent == null)
            {
                indent = string.Empty;
            }

            string[] tokens = Tokenize(value);
            if (tokens.Length == 0)
            {
                return NotCompound;
            }

            switch (tokens[0])
            {
                case "HorizontalGradient":
                    if (tokens.Length != 3) return Arity(tokens, 3);
                    return LinearGradient(tokens[1], tokens[2], "0,0", "1,0", indent);

                case "VerticalGradient":
                    if (tokens.Length != 3) return Arity(tokens, 3);
                    return LinearGradient(tokens[1], tokens[2], "0,0", "0,1", indent);

                case "RadialGradient":
                    if (tokens.Length != 3) return Arity(tokens, 3);
                    return RadialGradient(tokens[1], tokens[2], indent);

                case "LinearGradient":
                    if (tokens.Length != 7) return Arity(tokens, 7);
                    return LinearGradient(tokens[5], tokens[6],
                        tokens[1] + "," + tokens[2], tokens[3] + "," + tokens[4], indent);

                case "Image":
                    if (tokens.Length != 2) return Arity(tokens, 2);
                    return Simple("System.Windows.Media.ImageBrush",
                        "ImageSource=\"" + tokens[1] + "\"", indent);

                default:
                    return NotCompound;
            }
        }

        private static Expansion Arity(string[] tokens, int expected)
        {
            return new Expansion
            {
                Expanded = false,
                Reason = tokens[0] + " expects " + expected + " token(s), found "
                         + tokens.Length
            };
        }

        private static Expansion LinearGradient(string c1, string c2,
            string startPoint, string endPoint, string indent)
        {
            string inner = indent + "  ";
            StringBuilder sb = new StringBuilder();
            sb.Append(indent);
            sb.Append("<System.Windows.Media.LinearGradientBrush StartPoint=\"");
            sb.Append(startPoint);
            sb.Append("\" EndPoint=\"");
            sb.Append(endPoint);
            sb.AppendLine("\">");
            sb.Append(inner);
            sb.AppendLine("<System.Windows.Media.GradientStopCollection>");
            AppendStop(sb, inner + "  ", c1, "0");
            AppendStop(sb, inner + "  ", c2, "1");
            sb.Append(inner);
            sb.AppendLine("</System.Windows.Media.GradientStopCollection>");
            sb.Append(indent);
            sb.Append("</System.Windows.Media.LinearGradientBrush>");

            return new Expansion
            {
                Expanded = true,
                Markup = sb.ToString(),
                Summary = "LinearGradientBrush " + c1 + " -> " + c2
            };
        }

        private static Expansion RadialGradient(string c1, string c2, string indent)
        {
            string inner = indent + "  ";
            StringBuilder sb = new StringBuilder();
            sb.Append(indent);
            sb.AppendLine("<System.Windows.Media.RadialGradientBrush>");
            sb.Append(inner);
            sb.AppendLine("<System.Windows.Media.GradientStopCollection>");
            AppendStop(sb, inner + "  ", c1, "0");
            AppendStop(sb, inner + "  ", c2, "1");
            sb.Append(inner);
            sb.AppendLine("</System.Windows.Media.GradientStopCollection>");
            sb.Append(indent);
            sb.Append("</System.Windows.Media.RadialGradientBrush>");

            return new Expansion
            {
                Expanded = true,
                Markup = sb.ToString(),
                Summary = "RadialGradientBrush " + c1 + " -> " + c2
            };
        }

        private static Expansion Simple(string typeName, string attributes, string indent)
        {
            return new Expansion
            {
                Expanded = true,
                Markup = indent + "<" + typeName + " " + attributes + " />",
                Summary = typeName
            };
        }

        private static void AppendStop(StringBuilder sb, string indent,
            string color, string offset)
        {
            sb.Append(indent);
            sb.Append("<System.Windows.Media.GradientStop Color=\"");
            sb.Append(color);
            sb.Append("\" Offset=\"");
            sb.Append(offset);
            sb.AppendLine("\" />");
        }

        /// <summary>
        /// Splits on whitespace and commas, which is what TokenizerHelper does for
        /// these grammars. The brush type name is case-sensitive in ParseBrush's
        /// switch, so the caller's token is matched exactly.
        /// </summary>
        private static string[] Tokenize(string value)
        {
            List<string> tokens = new List<string>();
            StringBuilder current = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }
            return tokens.ToArray();
        }

        /// <summary>True when the value looks like a compound brush at all.</summary>
        public static bool IsCompoundBrush(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string trimmed = value.TrimStart();
            return trimmed.StartsWith("HorizontalGradient", StringComparison.Ordinal)
                || trimmed.StartsWith("VerticalGradient", StringComparison.Ordinal)
                || trimmed.StartsWith("RadialGradient", StringComparison.Ordinal)
                || trimmed.StartsWith("LinearGradient", StringComparison.Ordinal)
                || trimmed.StartsWith("Image", StringComparison.Ordinal)
                || trimmed.StartsWith("NineGrid", StringComparison.Ordinal);
        }
    }
}
