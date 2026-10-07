using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using BamlLonghorn.Dialects;

namespace BamlLonghorn
{
    /// <summary>
    /// Text rendering of decoded BAML: record dumps, reconstructed trees,
    /// interning tables and the 4093 recon view.
    /// </summary>
    public static class BamlPrinter
    {
        /// <summary>One record per line, with offsets and node headers.</summary>
        public static string DumpRecords(BamlDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < document.Records.Count; i++)
            {
                BamlRecord record = document.Records[i];
                sb.Append('[');
                sb.Append(record.Offset.ToString(CultureInfo.InvariantCulture).PadLeft(6));
                sb.Append("] ");
                sb.Append(record.RecordType.ToString().PadRight(26));
                sb.Append(" size=");
                sb.Append(record.Size.ToString(CultureInfo.InvariantCulture).PadRight(6));

                if (record.Node != null)
                {
                    sb.Append("  node(");
                    sb.Append(record.Node.ToString());
                    sb.Append(')');
                }
                sb.AppendLine();

                for (int f = 0; f < record.Fields.Count; f++)
                {
                    KeyValuePair<string, object> field = record.Fields[f];
                    sb.Append("         ");
                    sb.Append(field.Key.PadRight(22));
                    sb.Append(" = ");
                    string text = field.Value as string;
                    if (text != null)
                    {
                        sb.Append('"');
                        sb.Append(text);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(Convert.ToString(field.Value, CultureInfo.InvariantCulture));
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>Reconstructed markup tree as indented pseudo-XAML.</summary>
        public static string DumpTree(BamlDocument document)
        {
            BamlTreeNode root = BamlTreeBuilder.Build(document);
            if (root == null)
            {
                return "(no element tree in this document)" + Environment.NewLine;
            }
            StringBuilder sb = new StringBuilder();
            WriteTreeNode(sb, root, 0);
            return sb.ToString();
        }

        private static void WriteTreeNode(StringBuilder sb, BamlTreeNode node, int indent)
        {
            string pad = new string(' ', indent * 2);
            if (node.IsText)
            {
                sb.Append(pad);
                sb.Append("<Text>");
                sb.Append(node.Text);
                sb.AppendLine("</Text>");
                return;
            }

            sb.Append(pad);
            sb.Append("<Element id=");
            sb.Append(node.Id.ToString(CultureInfo.InvariantCulture));
            if (node.Depth >= 0)
            {
                sb.Append(" depth=");
                sb.Append(node.Depth.ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine(">");

            for (int i = 0; i < node.Attributes.Count; i++)
            {
                BamlRecord attribute = node.Attributes[i];
                sb.Append(pad);
                sb.Append("  @");
                sb.Append(attribute.RecordType.ToString());
                sb.Append(' ');
                object value = attribute.GetField("value");
                if (value == null)
                {
                    value = attribute.GetField("name");
                }
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                WriteTreeNode(sb, node.Children[i], indent + 1);
            }

            sb.Append(pad);
            sb.AppendLine("</Element>");
        }

        /// <summary>
        /// One line per decoded SHORT-framing record, with offset, kind, size and
        /// every payload field in declaration order.
        /// </summary>
        public static string DumpEntries(BamlDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                sb.Append('@');
                sb.Append(e.Offset.ToString(CultureInfo.InvariantCulture).PadLeft(6));
                sb.Append("  ");
                sb.Append(e.Name.PadRight(24));
                sb.Append(" size=");
                sb.Append(e.Size.ToString(CultureInfo.InvariantCulture).PadRight(6));
                for (int f = 0; f < e.Fields.Count; f++)
                {
                    KeyValuePair<string, object> field = e.Fields[f];
                    sb.Append(field.Key);
                    sb.Append('=');
                    string text = field.Value as string;
                    if (text != null)
                    {
                        sb.Append('"');
                        sb.Append(text);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(Convert.ToString(field.Value, CultureInfo.InvariantCulture));
                    }
                    sb.Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// The interning tables that the 4074 dialect stores as records:
        /// assemblies, types and attributes.
        /// </summary>
        public static string DumpInternTables(BamlDocument document)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("assemblies:");
            for (int i = 0; i < document.Records.Count; i++)
            {
                BamlRecord record = document.Records[i];
                if (record.RecordType == BamlRecordType.AssemblyInfo)
                {
                    sb.Append("  [");
                    sb.Append(record.GetInt32("assemblyId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append("] ");
                    sb.AppendLine(record.GetString("assemblyFullName", string.Empty));
                }
            }

            sb.AppendLine("types:");
            for (int i = 0; i < document.Records.Count; i++)
            {
                BamlRecord record = document.Records[i];
                if (record.RecordType == BamlRecordType.TypeInfo)
                {
                    sb.Append("  [");
                    sb.Append(record.GetInt32("typeId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append("] asm=");
                    sb.Append(record.GetInt32("assemblyId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.AppendLine(record.GetString("typeFullName", string.Empty));
                }
            }

            sb.AppendLine("attributes:");
            for (int i = 0; i < document.Records.Count; i++)
            {
                BamlRecord record = document.Records[i];
                if (record.RecordType == BamlRecordType.AttributeInfo)
                {
                    sb.Append("  [");
                    sb.Append(record.GetInt32("attributeId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append("] owner=");
                    sb.Append(record.GetInt32("ownerTypeId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.AppendLine(record.GetString("name", string.Empty));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The 4074 recon view: version, token stream, and the raw bytes between
        /// tokens (where the record tags and length fields live).
        /// </summary>
        public static string Dump4074Recon(byte[] data, BamlDocument document, int maxGapBytes)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("4074 record layer: not fully decoded (reconnaissance output).");
            for (int i = 0; i < document.Headers.Count; i++)
            {
                sb.Append("  header  ");
                sb.Append(document.Headers[i].Key);
                sb.Append(" = ");
                sb.AppendLine(Convert.ToString(document.Headers[i].Value, CultureInfo.InvariantCulture));
            }
            sb.AppendLine();

            List<Baml4074String> tokens = document.Strings;
            if (tokens == null)
            {
                return sb.ToString();
            }

            int previousEnd = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                Baml4074String token = tokens[i];
                int gapLength = token.Offset - previousEnd;

                sb.Append("  [");
                sb.Append(token.Offset.ToString(CultureInfo.InvariantCulture).PadLeft(6));
                sb.Append("] ");
                sb.Append(token.Encoding.PadRight(7));
                sb.Append("len=");
                sb.Append(token.TextBytes.ToString(CultureInfo.InvariantCulture).PadRight(5));
                sb.Append(' ');
                sb.AppendLine(Truncate(token.Text, 88));

                if (gapLength > 0)
                {
                    sb.Append("           gap(");
                    sb.Append(gapLength.ToString(CultureInfo.InvariantCulture));
                    sb.Append("): ");
                    sb.AppendLine(Hex(data, previousEnd, Math.Min(gapLength, maxGapBytes)));
                }
                previousEnd = token.Offset + token.TextBytes;
            }

            if (previousEnd < data.Length)
            {
                sb.Append("  tail(");
                sb.Append((data.Length - previousEnd).ToString(CultureInfo.InvariantCulture));
                sb.Append("): ");
                sb.AppendLine(Hex(data, previousEnd, Math.Min(data.Length - previousEnd, maxGapBytes)));
            }
            return sb.ToString();
        }

        /// <summary>
        /// The interning tables of a SHORT-framing document: assemblies, types and
        /// attributes, read from the decoded records.
        /// </summary>
        public static string DumpEntriesTables(BamlDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("assemblies:");
            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                if (e.Name == "AssemblyInfo")
                {
                    sb.Append("  [");
                    sb.Append(e.GetInt32("assemblyId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append("] ");
                    sb.AppendLine(e.GetString("fullName", string.Empty));
                }
            }

            sb.AppendLine("types:");
            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                if (e.Name == "TypeInfo" || e.Name == "TypeSerializerInfo")
                {
                    sb.Append("  [");
                    sb.Append(e.GetInt32("typeId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append("] asm=");
                    sb.Append(e.GetInt32("assemblyId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(e.GetString("typeFullName", string.Empty));
                    if (e.Name == "TypeSerializerInfo")
                    {
                        sb.Append("  serializer=");
                        sb.Append(e.GetInt32("serializerTypeId", -1).ToString(CultureInfo.InvariantCulture));
                    }
                    sb.AppendLine();
                }
            }

            sb.AppendLine("attributes:");
            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                if (e.Name == "AttributeInfo")
                {
                    sb.Append("  [");
                    sb.Append(e.GetInt32("attributeId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append("] owner=");
                    sb.Append(e.GetInt32("ownerTypeId", -1).ToString(CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.AppendLine(e.GetString("name", string.Empty));
                }
            }

            sb.AppendLine("namespace mappings (PIMapping):");
            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                if (e.Name == "PIMapping")
                {
                    sb.Append("  ");
                    sb.Append(e.GetString("clrns", string.Empty));
                    sb.Append("  ->  ");
                    sb.AppendLine(e.GetString("xmlns", string.Empty));
                }
            }
            return sb.ToString();
        }

        private static string Hex(byte[] data, int offset, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(count * 3);
            for (int i = 0; i < count && offset + i < data.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(data[offset + i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string Truncate(string value, int max)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value.Length <= max)
            {
                return value;
            }
            return value.Substring(0, max) + "...";
        }
    }
}
