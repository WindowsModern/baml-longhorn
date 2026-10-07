using System;
using System.Collections.Generic;
using System.Globalization;

namespace BamlLonghorn
{
    /// <summary>One node of a reconstructed markup tree.</summary>
    public sealed class BamlTreeNode
    {
        /// <summary>Element id (index into the TypeInfo naming space).</summary>
        public int Id { get; internal set; }

        /// <summary>Nesting depth as recorded in the node header.</summary>
        public int Depth { get; internal set; }

        /// <summary>Offset of the record that produced this node.</summary>
        public long RecordOffset { get; internal set; }

        /// <summary>True for a text node rather than an element.</summary>
        public bool IsText { get; internal set; }

        /// <summary>Text content, for text nodes.</summary>
        public string Text { get; internal set; }

        /// <summary>Properties and events attached to this node.</summary>
        public List<BamlRecord> Attributes { get; private set; }

        /// <summary>Child nodes, in document order.</summary>
        public List<BamlTreeNode> Children { get; private set; }

        /// <summary>Parent node, or null for the root.</summary>
        public BamlTreeNode Parent { get; internal set; }

        public BamlTreeNode()
        {
            Id = -1;
            Depth = -1;
            Attributes = new List<BamlRecord>();
            Children = new List<BamlTreeNode>();
        }

        public override string ToString()
        {
            return IsText
                ? "Text(" + Text + ")"
                : "Element(id=" + Id.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// Rebuilds a markup tree from a decoded record list.
    ///
    /// The 4074 dialect is a linear record stream, so the tree is rebuilt from
    /// the record sequence itself: Start/End pairing plus the node header's
    /// depth is sufficient, and is more robust than chasing the
    /// position-relative sibling offsets (those are a compiler-side navigation
    /// aid, not a requirement for reading).  The offsets remain on each node so
    /// callers can still follow them explicitly.
    /// </summary>
    public static class BamlTreeBuilder
    {
        /// <summary>Builds the tree, or returns null when the stream has no nodes.</summary>
        public static BamlTreeNode Build(BamlDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            BamlTreeNode root = null;
            BamlTreeNode current = null;

            for (int i = 0; i < document.Records.Count; i++)
            {
                BamlRecord record = document.Records[i];
                switch (record.RecordType)
                {
                    case BamlRecordType.Element:
                    {
                        BamlTreeNode node = new BamlTreeNode();
                        node.Id = record.GetInt32("id", -1);
                        node.Depth = record.Node != null ? record.Node.Depth : -1;
                        node.RecordOffset = record.Offset;

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

                    case BamlRecordType.EndElement:
                        if (current != null)
                        {
                            current = current.Parent;
                        }
                        break;

                    case BamlRecordType.Text:
                    case BamlRecordType.ParseLiteralContent:
                    case BamlRecordType.ClrObject:
                    {
                        BamlTreeNode node = new BamlTreeNode();
                        node.IsText = record.RecordType == BamlRecordType.Text;
                        node.Text = record.GetString("value", string.Empty);
                        node.Depth = record.Node != null ? record.Node.Depth : -1;
                        node.RecordOffset = record.Offset;

                        if (current == null)
                        {
                            root = node;
                        }
                        else
                        {
                            node.Parent = current;
                            current.Children.Add(node);
                        }
                        break;
                    }

                    case BamlRecordType.DynamicProperty:
                    case BamlRecordType.DynamicEvent:
                    case BamlRecordType.GenericAttribute:
                    case BamlRecordType.ComplexDynamicProperty:
                    case BamlRecordType.ClrProperty:
                    case BamlRecordType.ClrArrayProperty:
                    case BamlRecordType.ClrComplexProperty:
                        if (current != null)
                        {
                            current.Attributes.Add(record);
                        }
                        break;

                    default:
                        break;
                }
            }

            return root;
        }
    }
}
