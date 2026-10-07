using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>One node of a markup tree rebuilt from SHORT-framing records.</summary>
    public sealed class ShortTreeNode
    {
        /// <summary>Human readable label: the element's type name when known.</summary>
        public string Label { get; internal set; }

        /// <summary>Offset of the ElementStart record that opened this node.</summary>
        public int Offset { get; internal set; }

        /// <summary>Properties, events and xmlns declarations on this node.</summary>
        public List<BamlRecordEntry> Attributes { get; private set; }

        /// <summary>Child nodes, in document order.</summary>
        public List<ShortTreeNode> Children { get; private set; }

        /// <summary>Parent node, or null for the root.</summary>
        public ShortTreeNode Parent { get; internal set; }

        public ShortTreeNode()
        {
            Label = "?";
            Offset = -1;
            Attributes = new List<BamlRecordEntry>();
            Children = new List<ShortTreeNode>();
        }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Rebuilds a markup tree from decoded SHORT-framing records.
    ///
    /// The record stream is already a well-nested sequence:
    /// <c>ElementStart</c> opens a node, <c>ElementEnd</c> closes it, and
    /// <c>Property</c> / <c>XmlnsProperty</c> / <c>Text</c> records belong to the
    /// node currently open. So a single stack is enough 鈥?no offset chasing.
    ///
    /// Type names come from the <c>TypeInfo</c> table that the stream populates
    /// through its <c>typeId</c> values.
    /// </summary>
    public static class BamlShortTreeBuilder
    {
        /// <summary>
        /// The build-4074 "known types" table, transcribed from
        /// <c>BamlMapTable.InitStaticData</c>. An <c>ElementStart</c> record whose
        /// <c>TypeId</c> is NEGATIVE refers to this table as
        /// <c>index = -typeId</c>; a non-negative TypeId instead indexes the
        /// <c>TypeInfo</c> records that the stream itself carries.
        ///
        /// Index 0 and the gaps (7, 13) are intentionally absent in the original,
        /// so they stay null here.
        /// </summary>
        private static readonly string[] KnownTypes = BuildKnownTypes();

        private static string[] BuildKnownTypes()
        {
            string[] t = new string[37];
            t[1] = "Border";
            t[2] = "Button";
            t[3] = "Canvas";
            t[4] = "CheckBox";
            t[5] = "ComboBox";
            t[6] = "ComboBoxItem";
            t[8] = "DockPanel";
            t[9] = "FlowPanel";
            t[10] = "Frame";
            t[11] = "HyperLink";
            t[12] = "Image";
            t[14] = "ListBox";
            t[15] = "Menu";
            t[16] = "MenuItem";
            t[17] = "RadioButton";
            t[18] = "Text";
            t[19] = "TextBox";
            t[20] = "TextPanel";
            t[21] = "Block";
            t[22] = "Body";
            t[23] = "Cell";
            t[24] = "FixedPage";
            t[25] = "Heading";
            t[26] = "Inline";
            t[27] = "Italic";
            t[28] = "LineBreak";
            t[29] = "Paragraph";
            t[30] = "Row";
            t[31] = "Table";
            t[32] = "Glyphs";
            t[33] = "Path";
            t[34] = "Rectangle";
            t[35] = "Style";
            t[36] = "XamlStyleSerializer";
            return t;
        }

        /// <summary>
        /// Resolves an ElementStart TypeId to a type name, or null when it cannot
        /// be resolved.
        /// </summary>
        public static string ResolveTypeName(int typeId, Dictionary<int, string> typeTable)
        {
            if (typeId < 0)
            {
                int index = -typeId;
                if (index > 0 && index < KnownTypes.Length && KnownTypes[index] != null)
                {
                    return KnownTypes[index];
                }
                return null;
            }
            string name;
            if (typeTable != null && typeTable.TryGetValue(typeId, out name))
            {
                return name;
            }
            return null;
        }

        /// <summary>Builds the tree, or null when the stream holds no elements.</summary>
        public static ShortTreeNode Build(BamlDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            Dictionary<int, string> types = new Dictionary<int, string>();
            Dictionary<int, string> attributes = new Dictionary<int, string>();

            // first pass: interning tables
            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                if (e.Name == "TypeInfo"
                    || e.Name == "TypeSerializerInfo")
                {
                    types[e.GetInt32("typeId", -1)] = e.GetString("typeFullName", "?");
                }
                else if (e.Name == "AttributeInfo")
                {
                    attributes[e.GetInt32("attributeId", -1)] = e.GetString("name", "?");
                }
            }

            ShortTreeNode root = null;
            ShortTreeNode current = null;

            for (int i = 0; i < document.Entries.Count; i++)
            {
                BamlRecordEntry e = document.Entries[i];
                switch (e.Name)
                {
                    case "ElementStart":
                    {
                        ShortTreeNode node = new ShortTreeNode();
                        int typeId = e.GetInt32("int16", -1);
                        string name = ResolveTypeName(typeId, types);
                        node.Label = name != null
                            ? name
                            : "typeId=" + typeId.ToString(CultureInfo.InvariantCulture);
                        node.Offset = e.Offset;

                        if (current == null)
                        {
                            root = node;
                        }
                        else
                        {
                            node.Parent = current;
                            current.Children.Add(node);
                        }
                        current = node;
                        break;
                    }

                    case "ElementEnd":
                        if (current != null)
                        {
                            current = current.Parent;
                        }
                        break;

                    case "Text":
                    case "LiteralContent":
                    case "DefAttribute":
                    case "XmlnsProperty":
                    case "Property":
                    case "PropertyCustom":
                    case "RoutedEvent":
                        if (current != null)
                        {
                            current.Attributes.Add(e);
                        }
                        break;

                    default:
                        break;
                }
            }

            return root;
        }

        /// <summary>Renders the tree as indented pseudo-XAML.</summary>
        public static string Render(BamlDocument document)
        {
            ShortTreeNode root = Build(document);
            if (root == null)
            {
                return "(no element tree in this document)" + Environment.NewLine;
            }
            StringBuilder sb = new StringBuilder();
            Write(sb, root, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, ShortTreeNode node, int indent)
        {
            string pad = new string(' ', indent * 2);
            sb.Append(pad);
            sb.Append('<');
            sb.Append(node.Label);
            sb.AppendLine(">");

            for (int i = 0; i < node.Attributes.Count; i++)
            {
                BamlRecordEntry a = node.Attributes[i];
                sb.Append(pad);
                sb.Append("  ");
                switch (a.Name)
                {
                    case "Text":
                        sb.Append("(text) ");
                        sb.AppendLine(a.GetString("value", string.Empty));
                        break;
                    case "XmlnsProperty":
                        sb.Append("xmlns");
                        string prefix = a.GetString("prefix", string.Empty);
                        if (prefix.Length > 0)
                        {
                            sb.Append(':');
                            sb.Append(prefix);
                        }
                        sb.Append("=\"");
                        sb.Append(a.GetString("value", string.Empty));
                        sb.AppendLine("\"");
                        break;
                    case "Property":
                    case "RoutedEvent":
                        sb.Append(a.Name);
                        sb.Append(" attributeId=");
                        sb.Append(a.GetInt32("attributeId", -1).ToString(CultureInfo.InvariantCulture));
                        sb.Append(" value=\"");
                        sb.Append(a.GetString("value", string.Empty));
                        sb.AppendLine("\"");
                        break;
                    default:
                        sb.Append(a.Name);
                        sb.Append(' ');
                        sb.AppendLine(a.ToString());
                        break;
                }
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Write(sb, node.Children[i], indent + 1);
            }

            sb.Append(pad);
            sb.Append("</");
            sb.Append(node.Label);
            sb.AppendLine(">");
        }
    }
}
