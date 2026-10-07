using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// Decompiles decoded SHORT-framing records back to XAML text.
    ///
    /// Name resolution uses the interning tables that the record stream itself
    /// carries:
    ///   * <c>ElementStart.TypeId</c> is negative  -> <c>BamlMapTable._knownTypes[-TypeId]</c>
    ///   * <c>ElementStart.TypeId</c> is non-negative -> the <c>TypeInfo</c> record
    ///     with that <c>typeId</c>, whose <c>typeFullName</c> may be qualified by
    ///     its <c>assemblyId</c> through <c>AssemblyInfo</c>
    ///   * <c>Property.AttributeId</c> -> the <c>AttributeInfo</c> record with that
    ///     <c>attributeId</c>, whose <c>name</c> is the XAML attribute name
    /// </summary>
    public static class BamlXamlWriter
    {
        /// <summary>
        /// Count of <c>DefAttribute</c> records dropped because their name was empty.
        ///
        /// Exposed rather than silently discarded: a bare <c>def:</c> prefix makes the document
        /// unparseable, so the record cannot be emitted, but the loss should be visible to whoever
        /// reads the output.
        /// </summary>
        public static int SkippedDefAttributes;

        /// <summary>
        /// Makes a string safe to place inside an XML comment.
        ///
        /// XML forbids a double hyphen anywhere inside a comment and a trailing hyphen at its end, and
        /// a reader rejects the entire document when it finds one -- "a comment cannot contain '--' and
        /// cannot end with '-'". Everything this project puts in a comment is generated from decoded
        /// data or from its own report headings, neither of which is under a formatter's control, so
        /// the text is sanitised rather than assumed safe. This single omission accounted for 18 of
        /// 103 corpus documents failing to load, and it is shared here so every comment site uses it.
        /// </summary>
        public static string SafeComment(string text)
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

        /// <summary>The pre-release XAML namespace, as serialized by build 4074.</summary>
        public const string XamlNamespace = "http://schemas.microsoft.com/2005/xaml/";

        /// <summary>The Longhorn definition namespace, whose prefix is always "def".</summary>
        public const string DefinitionNamespace = "Definition";

        /// <summary>Options controlling the emitted XAML.</summary>
        public sealed class Options
        {
            /// <summary>Emit a leading XML declaration.</summary>
            public bool XmlDeclaration { get; set; }

            /// <summary>Indent with this string.</summary>
            public string Indent { get; set; }

            /// <summary>
            /// Resolve <c>TypeInfo.typeFullName</c> through its assembly and emit the
            /// assembly-qualified form.
            /// </summary>
            public bool QualifyTypesWithAssembly { get; set; }

            /// <summary>Append a comment naming the record offset of each element.</summary>
            public bool AnnotateOffsets { get; set; }

            /// <summary>
            /// Expand compound brush shorthands (HorizontalGradient and friends) into
            /// structured LinearGradientBrush / RadialGradientBrush markup instead of
            /// printing the raw shorthand string.
            ///
            /// Off by default: the string IS what the compiler wrote, so the faithful
            /// reading is the string. Expansion is an interpretation, and it is offered
            /// so a reader can see the brush the application would have built.
            /// </summary>
            public bool ExpandCompoundBrushes { get; set; }

            public Options()
            {
                XmlDeclaration = false;
                Indent = "  ";
                QualifyTypesWithAssembly = false;
                AnnotateOffsets = false;
            }
        }

        /// <summary>Decompiles a document to XAML text using default options.</summary>
        public static string Write(BamlDocument document)
        {
            return Write(document, new Options());
        }

        /// <summary>Decompiles a document to XAML text.</summary>
        public static string Write(BamlDocument document, Options options)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }
            if (options == null)
            {
                options = new Options();
            }
            if (options.Indent == null)
            {
                options.Indent = "  ";
            }

            Tables tables = Tables.Build(document, options);

            StringBuilder sb = new StringBuilder();
            if (options.XmlDeclaration)
            {
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            }

            Element root = Element.Build(document, tables, options);
            if (root == null)
            {
                // Say WHY there is no tree rather than leaving the reader to guess. The
                // LONG lineage is a real distinction here: its records decode fine and
                // are visible through `records`, but the element tree is expressed
                // through the node-header offsets and CLR-backed records rather than the
                // SHORT lineage's ElementStart/ElementEnd pair, so the SHORT tree walk
                // finds nothing to build from.
                if (document.Dialect == BamlDialect.Build3683)
                {
                    sb.AppendLine("<!-- no element tree: this is the LONG-framing Avalon "
                                  + "lineage (3683..4042). Its records are decoded and "
                                  + "visible via 'records', but XAML output for this "
                                  + "lineage is not implemented yet. -->");
                }
                else
                {
                    sb.AppendLine("<!-- no element tree in this document -->");
                }
                if (!string.IsNullOrEmpty(document.Note))
                {
                    sb.Append("<!-- ");
                    sb.Append(SafeComment(document.Note));
                    sb.AppendLine(" -->");
                }
                return sb.ToString();
            }

            // The LONG lineage reconstructs its tree from a different record vocabulary
            // (Element/EndElement pairs plus CLR-backed records, with names resolved
            // through the interning tables rather than a known-types table), so it has its
            // own builder. Everything downstream -- attribute rendering, brush expansion,
            // escaping -- is shared.

            WriteElement(sb, root, 0, options);

            if (!document.IsCompleteParse)
            {
                sb.AppendLine();
                sb.Append("<!-- INCOMPLETE: ");
                sb.Append(SafeComment(document.Note));
                sb.AppendLine(" -->");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // interning tables
        // ------------------------------------------------------------------

        private sealed class Tables
        {
            public readonly Dictionary<int, string> Assemblies = new Dictionary<int, string>();
            public readonly Dictionary<int, string> Types = new Dictionary<int, string>();
            public readonly Dictionary<int, int> TypeAssembly = new Dictionary<int, int>();
            public readonly Dictionary<int, string> Attributes = new Dictionary<int, string>();
            public readonly Dictionary<int, int> AttributeOwner = new Dictionary<int, int>();

            /// <summary>Namespaces declared by PIMapping records, keyed by CLR namespace.</summary>
            public readonly Dictionary<string, string> PiMappings = new Dictionary<string, string>(StringComparer.Ordinal);

            public static Tables Build(BamlDocument document, Options options)
            {
                Tables t = new Tables();

                for (int i = 0; i < document.Entries.Count; i++)
                {
                    BamlRecordEntry e = document.Entries[i];
                    switch (e.Name)
                    {
                        case "AssemblyInfo":
                            t.Assemblies[e.GetInt32("assemblyId", -1)] = e.GetString("fullName", string.Empty);
                            break;

                        case "TypeInfo":
                        case "TypeSerializerInfo":
                            t.Types[e.GetInt32("typeId", -1)] = e.GetString("typeFullName", string.Empty);
                            t.TypeAssembly[e.GetInt32("typeId", -1)] = e.GetInt32("assemblyId", -1);
                            break;

                        case "AttributeInfo":
                            t.Attributes[e.GetInt32("attributeId", -1)] = e.GetString("name", string.Empty);
                            t.AttributeOwner[e.GetInt32("attributeId", -1)] = e.GetInt32("ownerTypeId", -1);
                            break;

                        case "PIMapping":
                            t.PiMappings[e.GetString("clrns", string.Empty)] = e.GetString("xmlns", string.Empty);
                            break;

                        default:
                            break;
                    }
                }
                return t;
            }

            /// <summary>Element name for a TypeId, or a stable placeholder.</summary>
            public string ElementName(int typeId)
            {
                string known = BamlShortTreeBuilder.ResolveTypeName(typeId, null);
                if (known != null)
                {
                    return known;
                }
                string name;
                if (Types.TryGetValue(typeId, out name) && name.Length > 0)
                {
                    return name;
                }
                return "type" + typeId.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>Attribute name for an attributeId, or a stable placeholder.</summary>
            public string AttributeName(int attributeId)
            {
                string name;
                if (Attributes.TryGetValue(attributeId, out name) && name.Length > 0)
                {
                    return name;
                }
                return "attr" + attributeId.ToString(CultureInfo.InvariantCulture);
            }
        }

        // ------------------------------------------------------------------
        // tree
        // ------------------------------------------------------------------

        private sealed class Element
        {
            public string Name;
            public int TypeId;
            public int Offset;

            /// <summary>
            /// True when this node is a complex property rather than an element, i.e. it was
            /// opened by ComplexDynamicProperty / ClrComplexProperty. Markup renders both the
            /// same way -- property elements are ordinary elements -- but the flag lets a
            /// caller tell them apart.
            /// </summary>
            public bool IsPropertyElement;

            /// <summary>xmlns declarations and ordinary attributes, in stream order.</summary>
            public readonly List<KeyValuePair<string, string>> Attributes =
                new List<KeyValuePair<string, string>>();

            public readonly List<Element> Children = new List<Element>();

            /// <summary>Text content, when the element holds only text.</summary>
            public string Text;

            public static Element Build(BamlDocument document, Tables tables, Options options)
            {
                // The LONG lineage reconstructs from a different vocabulary -- Element /
                // EndElement pairs, dynamic properties resolved through the interning
                // tables, CLR-backed records -- so it has its own builder. Everything
                // downstream (WriteElement, escaping, brush expansion) is shared, which is
                // why this lives here rather than in a separate type that would have to
                // reach into these private classes.
                if (document.Dialect == BamlDialect.Build3683)
                {
                    return BuildLong(document, options);
                }

                Element root = null;
                Stack<Element> open = new Stack<Element>();

                for (int i = 0; i < document.Entries.Count; i++)
                {
                    BamlRecordEntry e = document.Entries[i];
                    Element current = open.Count > 0 ? open.Peek() : null;

                    switch (e.Name)
                    {
                        case "ElementStart":
                        {
                            int typeId = e.GetInt32("int16", -1);
                            Element node = new Element();
                            node.TypeId = typeId;
                            node.Name = tables.ElementName(typeId);
                            node.Offset = e.Offset;

                            if (current == null)
                            {
                                root = node;
                            }
                            else
                            {
                                current.Children.Add(node);
                            }
                            open.Push(node);
                            break;
                        }

                        case "ElementEnd":
                            if (open.Count > 0)
                            {
                                open.Pop();
                            }
                            break;

                        case "XmlnsProperty":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string prefix = e.GetString("prefix", string.Empty);
                            string value = e.GetString("value", string.Empty);
                            string key = prefix.Length > 0 ? "xmlns:" + prefix : "xmlns";
                            current.Attributes.Add(new KeyValuePair<string, string>(key, value));
                            break;
                        }

                        case "Property":
                        case "RoutedEvent":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string name = tables.AttributeName(e.GetInt32("attributeId", -1));
                            string value = e.GetString("value", string.Empty);
                            current.Attributes.Add(new KeyValuePair<string, string>(name, value));
                            break;
                        }

                        case "PropertyCustom":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string name = tables.AttributeName(e.GetInt32("attributeId", -1));
                            string value = DecodeCustomValue(e.GetString("rawValue", string.Empty), options);
                            current.Attributes.Add(new KeyValuePair<string, string>(name, value));
                            break;
                        }

                        case "DefAttribute":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            // DefAttribute carries Value then Name; Name is the def: key.
                            //
                            // A record whose name is empty would render as a bare "def:", which is an
                            // unbound namespace prefix: no XML reader can parse the document, and this
                            // is the most common reason converted output fails to load. The record
                            // carries no usable name, so it is dropped and counted rather than emitted
                            // broken.
                            string defName = e.GetString("name", string.Empty);
                            if (defName.Length == 0)
                            {
                                SkippedDefAttributes++;
                                break;
                            }
                            current.Attributes.Add(
                                new KeyValuePair<string, string>(
                                    "def:" + defName, e.GetString("value", string.Empty)));
                            break;
                        }

                        case "Text":
                        case "LiteralContent":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string text = e.GetString("value", string.Empty);
                            current.Text = current.Text == null ? text : current.Text + text;
                            break;
                        }

                        default:
                            break;
                    }
                }
                return root;
            }

            /// <summary>
            /// Builds the tree for the LONG lineage (Avalon, 3683..4042).
            ///
            /// Nesting comes from the record stream: <c>Element</c> opens a node,
            /// <c>EndElement</c> closes it, and <c>ClrObject</c>/<c>EndClrObject</c> do the
            /// same for CLR-backed children. Every tree-node record also carries a 12-byte
            /// header of depth and offsets, but those are not needed here and are not
            /// followed -- the traversal records already delimit the structure.
            ///
            /// NAMES resolve differently from the SHORT lineage. There, ElementStart's
            /// TypeId may be negative, meaning an entry in <c>BamlMapTable._knownTypes</c>.
            /// Here an <c>Element</c> carries a plain <c>Id</c> looked up directly against
            /// <c>TypeInfo.TypeId</c>; there is no negative encoding.
            /// </summary>
            private static Element BuildLong(BamlDocument document, Options options)
            {
                Dictionary<int, string> types = new Dictionary<int, string>();
                Dictionary<int, string> attributes = new Dictionary<int, string>();

                for (int i = 0; i < document.Entries.Count; i++)
                {
                    BamlRecordEntry e = document.Entries[i];
                    if (e.Name == "TypeInfo" || e.Name == "TypeSerializerInfo")
                    {
                        types[e.GetInt32("typeId", -1)] = e.GetString("typeFullName", "?");
                    }
                    else if (e.Name == "AttributeInfo")
                    {
                        attributes[e.GetInt32("attributeId", -1)] = e.GetString("name", "?");
                    }
                }

                Element root = null;
                Stack<Element> open = new Stack<Element>();

                for (int i = 0; i < document.Entries.Count; i++)
                {
                    BamlRecordEntry e = document.Entries[i];
                    Element current = open.Count > 0 ? open.Peek() : null;

                    switch (e.Name)
                    {
                        case "Element":
                        {
                            Element node = new Element();
                            int id = e.GetInt32("id", -1);
                            string name;
                            if (types.TryGetValue(id, out name) && name.Length > 0)
                            {
                                node.Name = name;
                            }
                            else
                            {
                                node.Name = "id" + id.ToString(CultureInfo.InvariantCulture);
                            }
                            node.TypeId = id;
                            node.Offset = e.Offset;

                            if (current == null)
                            {
                                root = node;
                            }
                            else
                            {
                                current.Children.Add(node);
                            }
                            open.Push(node);
                            break;
                        }

                        case "ClrObject":
                        {
                            // A CLR object shares the TypeInfo id space with Element, so its
                            // type resolves the same way. Confirmed against 481.baml:
                            // ClrObject id=5 is System.Windows.Media.LinearGradient, and
                            // every GradientStop is ClrObject id=6.
                            Element node = new Element();
                            int id = e.GetInt32("id", -1);
                            string clrName;
                            if (types.TryGetValue(id, out clrName) && clrName.Length > 0)
                            {
                                node.Name = clrName;
                            }
                            else
                            {
                                node.Name = "clr#" + id.ToString(CultureInfo.InvariantCulture);
                            }
                            node.TypeId = id;
                            node.Offset = e.Offset;
                            if (current == null)
                            {
                                root = node;
                            }
                            else
                            {
                                current.Children.Add(node);
                            }
                            open.Push(node);
                            break;
                        }

                        case "EndElement":
                        case "EndClrObject":
                            if (open.Count > 0)
                            {
                                open.Pop();
                            }
                            break;

                        case "DynamicProperty":
                        case "DependencyIDProperty":
                        case "DependencyProperty":
                        case "DynamicEvent":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            int id = e.GetInt32("attributeId", -1);
                            string name;
                            if (!attributes.TryGetValue(id, out name) || name.Length == 0)
                            {
                                name = "attr" + id.ToString(CultureInfo.InvariantCulture);
                            }
                            current.Attributes.Add(new KeyValuePair<string, string>(
                                name, e.GetString("value", string.Empty)));
                            break;
                        }

                        // -- complex properties: nested scopes, not attributes -------
                        // A complex property opens a scope whose contents are the property
                        // value, and EndComplex* closes it. Rendering it as an attribute
                        // would lose the whole subtree, and worse, would leave the scope's
                        // children attached to the enclosing element with no closing tag.
                        case "ComplexDynamicProperty":
                        case "ComplexDependencyIDProperty":
                        case "ComplexDependencyProperty":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            int id = e.GetInt32("attributeId", -1);
                            string name;
                            if (!attributes.TryGetValue(id, out name) || name.Length == 0)
                            {
                                name = "attr" + id.ToString(CultureInfo.InvariantCulture);
                            }
                            Element scope = new Element();
                            scope.Name = name;
                            scope.IsPropertyElement = true;
                            scope.Offset = e.Offset;
                            current.Children.Add(scope);
                            open.Push(scope);
                            break;
                        }

                        case "ClrComplexProperty":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            Element scope = new Element();
                            scope.Name = e.GetString("name", "clrProperty");
                            scope.IsPropertyElement = true;
                            scope.Offset = e.Offset;
                            current.Children.Add(scope);
                            open.Push(scope);
                            break;
                        }

                        // Collection properties open a scope in exactly the same way, and
                        // each carries its own name. Leaving these unhandled let their
                        // children attach to the enclosing element with no closing tag --
                        // the same defect class as the complex-property bug, so they are
                        // handled together rather than one at a time.
                        case "ClrArrayProperty":
                        case "IListProperty":
                        case "IDictionaryProperty":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            Element scope = new Element();
                            scope.Name = e.GetString("name", "items");
                            scope.IsPropertyElement = true;
                            scope.Offset = e.Offset;
                            current.Children.Add(scope);
                            open.Push(scope);
                            break;
                        }

                        case "EndClrArrayProperty":
                        case "EndIListProperty":
                        case "EndIDictionaryProperty":
                        case "EndComplexDynamicProperty":
                        case "EndComplexDependencyIDProperty":
                        case "EndComplexDependencyProperty":
                        case "EndClrComplexProperty":
                            if (open.Count > 0)
                            {
                                open.Pop();
                            }
                            break;

                        case "GenericAttribute":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string uri = e.GetString("namespaceUri", string.Empty);
                            string local = e.GetString("localName", string.Empty);
                            string name = uri.Length > 0 ? "{" + uri + "}" + local : local;
                            current.Attributes.Add(new KeyValuePair<string, string>(
                                name, e.GetString("value", string.Empty)));
                            break;
                        }

                        case "XmlnsProperty":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string prefix = e.GetString("prefix", string.Empty);
                            string key = prefix.Length > 0 ? "xmlns:" + prefix : "xmlns";
                            current.Attributes.Add(new KeyValuePair<string, string>(
                                key, e.GetString("value", string.Empty)));
                            break;
                        }

                        case "DefAttribute":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            current.Attributes.Add(new KeyValuePair<string, string>(
                                "def:" + e.GetString("name", string.Empty),
                                e.GetString("value", string.Empty)));
                            break;
                        }

                        case "ClrProperty":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            current.Attributes.Add(new KeyValuePair<string, string>(
                                e.GetString("name", string.Empty),
                                e.GetString("value", string.Empty)));
                            break;
                        }

                        case "Text":
                        case "ParseLiteralContent":
                        {
                            if (current == null)
                            {
                                break;
                            }
                            string text = e.GetString("value", string.Empty);
                            current.Text = current.Text == null ? text : current.Text + text;
                            break;
                        }

                        default:
                            break;
                    }
                }

                return root;
            }
        }

        // ------------------------------------------------------------------
        // rendering
        // ------------------------------------------------------------------

        private static void WriteElement(StringBuilder sb, Element element, int depth, Options options)
        {
            string pad = Repeat(options.Indent, depth);

            sb.Append(pad);
            sb.Append('<');
            sb.Append(element.Name);

            for (int i = 0; i < element.Attributes.Count; i++)
            {
                KeyValuePair<string, string> a = element.Attributes[i];

                // The default XAML namespace may be omitted: it is implied. Any
                // other default xmlns must be preserved.
                if (string.Equals(a.Key, "xmlns", StringComparison.Ordinal)
                    && string.Equals(a.Value, XamlNamespace, StringComparison.Ordinal))
                {
                    continue;
                }
                sb.Append(' ');
                sb.Append(a.Key);
                sb.Append("=\"");
                sb.Append(EscapeAttribute(a.Value));
                sb.Append('"');
            }

            bool hasContent = element.Children.Count > 0
                              || (element.Text != null && element.Text.Length > 0);

            if (!hasContent)
            {
                sb.AppendLine(" />");
                return;
            }
            sb.Append('>');

            if (element.Children.Count == 0 && element.Text != null && element.Text.Length > 0)
            {
                // text-only element: keep it on one line
                sb.Append(EscapeText(element.Text));
                sb.Append("</");
                sb.Append(element.Name);
                sb.AppendLine(">");
                return;
            }

            sb.AppendLine();
            if (element.Text != null && element.Text.Length > 0)
            {
                sb.Append(pad);
                sb.Append(options.Indent);
                sb.AppendLine(EscapeText(element.Text));
            }
            for (int i = 0; i < element.Children.Count; i++)
            {
                WriteElement(sb, element.Children[i], depth + 1, options);
            }
            sb.Append(pad);
            sb.Append("</");
            sb.Append(element.Name);
            sb.AppendLine(">");
        }

        /// <summary>
        /// Renders a <c>PropertyCustom</c> value from its raw bytes.
        ///
        /// WHY THIS IS BYTE-DRIVEN AND NOT TYPE-DRIVEN
        ///
        /// The record's layout is chosen by the property's CLR type, and that type
        /// is NOT present in the stream. The original reader obtains it by
        /// reflection:
        ///
        ///     BamlAttributeInfoRecord.GetPropertyType() -> PropInfo.PropertyType
        ///     BamlRecordReader: SetValueObject(
        ///         ((DependencyProperty)dp).PropertyType, reader)
        ///
        /// so `PropertyCustom` is genuinely ambiguous without runtime type
        /// metadata. What saves us is that the value encodings are largely
        /// self-describing:
        ///
        ///   * packed scalar (Length, GridLength, Spacing, FontSize) -- the first
        ///     byte carries both the unit and the width of the number that follows,
        ///     so the decoder can verify it consumed exactly the available bytes
        ///   * enum -- a bare <c>uint</c>, 4 bytes, no self-description
        ///   * Thickness -- leads with a component count of 1, 2 or 4
        ///   * Brush -- a composite that is not implemented here yet
        ///
        /// So dispatch is: try the interpretations that can be validated against the
        /// exact byte count, and fall back to hex rather than emit a wrong value.
        /// The one unavoidable ambiguity is a 4-byte value, which could be either an
        /// enum or a packed scalar; a leading byte below 0x80 whose high bits do not
        /// encode a width is treated as an enum.
        /// </summary>
        private static string DecodeCustomValue(string rawHex, Options options)
        {
            if (string.IsNullOrEmpty(rawHex))
            {
                return string.Empty;
            }

            string[] parts = rawHex.Split(' ');
            byte[] bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                byte b;
                if (!byte.TryParse(parts[i], NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out b))
                {
                    return rawHex;
                }
                bytes[i] = b;
            }

            // 1. Tagged string: [00][len][ascii]. Self-describing, so it is checked
            //    before anything width-based. Covers gradient modes and "#RRGGBB"
            //    colours, which is most of the remaining volume.
            if (bytes[0] == 0x00 && bytes.Length >= 3)
            {
                BamlCustomValue.Decoded tagged = BamlCustomValue.ReadTaggedString(bytes, 0);
                if (tagged.Error == null && tagged.Bytes == bytes.Length)
                {
                    // A handful of these strings are compound brush shorthands. The
                    // string is what the compiler wrote, so it is returned verbatim by
                    // default; expansion is offered separately because it shows what
                    // the brush would have become at load time.
                    if (options != null && options.ExpandCompoundBrushes
                        && BamlBrushExpander.IsCompoundBrush(tagged.Text))
                    {
                        BamlBrushExpander.Expansion expansion =
                            BamlBrushExpander.Expand(tagged.Text, string.Empty);
                        if (expansion.Expanded)
                        {
                            return "\n" + expansion.Markup;
                        }
                    }
                    return tagged.Text;
                }
            }

            // 2. Serialized colour: [01][A][R][G][B].
            if (bytes[0] == 0x01 && bytes.Length == 5)
            {
                BamlCustomValue.Decoded color = BamlCustomValue.ReadColor(bytes, 0);
                if (color.Error == null && color.Bytes == bytes.Length)
                {
                    return color.Text;
                }
            }

            // 3. A single byte below 0x80 is a pixel measure with no unit suffix.
            //    The packed decoder returns "0px" style text for these, which the
            //    original XAML would not have written, so strip the suffix.
            if (bytes.Length == 1 && bytes[0] < 0x80)
            {
                return bytes[0].ToString(CultureInfo.InvariantCulture);
            }

            // 4. A 4-byte value whose first byte is a plain small integer and whose
            //    total width is exactly 4 cannot be a self-describing packed scalar
            //    of width 4 (that needs the 0xA0 tag), so it is an enum uint.
            if (bytes.Length == 4 && bytes[0] < 0x80)
            {
                BamlCustomValue.Decoded small = BamlCustomValue.ReadUInt32(bytes, 0);
                if (small.Error == null && small.Bytes == bytes.Length)
                {
                    return small.Text;
                }
            }

            // 5. Packed scalars: Length / GridLength / Spacing / FontSize. The
            //    width check makes this self-validating.
            if ((bytes[0] & 0x80) != 0 || bytes.Length == 1)
            {
                BamlCustomValue.Decoded packed =
                    BamlCustomValue.ReadPackedLength(bytes, 0, string.Empty);
                if (packed.Error == null && packed.Bytes == bytes.Length)
                {
                    return packed.Text;
                }
            }

            // 6. Thickness leads with a component count of 1, 2 or 4, and its width
            //    check must also hold.
            if (bytes[0] == 1 || bytes[0] == 2 || bytes[0] == 4)
            {
                BamlCustomValue.Decoded thickness = BamlCustomValue.ReadThickness(bytes, 0);
                if (thickness.Error == null && thickness.Bytes == bytes.Length)
                {
                    return thickness.Text;
                }
            }

            // 7. A 4-byte enum whose high bit happens to be set.
            if (bytes.Length == 4)
            {
                BamlCustomValue.Decoded asEnum = BamlCustomValue.ReadUInt32(bytes, 0);
                if (asEnum.Error == null && asEnum.Bytes == bytes.Length)
                {
                    return asEnum.Text;
                }
            }

            // 8. Give up rather than guess: keep the bytes so nothing is lost.
            return rawHex;
        }

        private static string Repeat(string unit, int count)
        {
            if (count <= 0 || string.IsNullOrEmpty(unit))
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(unit.Length * count);
            for (int i = 0; i < count; i++)
            {
                sb.Append(unit);
            }
            return sb.ToString();
        }

        private static string EscapeAttribute(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");
        }

        private static string EscapeText(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
