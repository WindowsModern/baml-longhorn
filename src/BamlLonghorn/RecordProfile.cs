using System;
using System.Collections.Generic;

namespace BamlLonghorn
{
    /// <summary>
    /// How a generation frames its records.
    ///
    /// Two encodings exist in the Longhorn material, and they are mutually exclusive:
    ///
    /// <code>
    /// Short    [int16 type][int32 size, variable-sized records only][payload]
    ///          next = sizeFieldOffset + size = recordStart + 2 + size
    ///
    /// Long     [int64 size][int16 type][payload]
    ///          size counts the whole record, so next = recordStart + size
    /// </code>
    ///
    /// "Short" and "Long" name the encodings, not the eras: the Short form is the later
    /// one (4074 onward) despite its narrower size field, because that field is present
    /// only on the records that need it.
    /// </summary>
    public enum BamlFraming
    {
        /// <summary><c>[int16 type]</c>, then <c>[int32 size]</c> for variable-sized records.</summary>
        Short,

        /// <summary><c>[int64 size][int16 type]</c>, size counting the whole record.</summary>
        Long
    }

    /// <summary>
    /// A generation's record vocabulary: the ordered code list, the sizing
    /// classification, and the framing it uses.
    ///
    /// 4074 and 4093 share the SHORT framing and differ only in their enums. 3683 uses
    /// the LONG framing with a third enum. Swapping the profile is therefore enough to
    /// walk a different generation; the walker itself is shared.
    ///
    /// Every table is transcribed from the corresponding decompiled source; see
    /// docs/BAML4074-FORMAT.md and docs/BAML3683-FORMAT.md.
    /// </summary>
    public sealed class RecordProfile
    {
        private readonly string[] _names;
        private readonly HashSet<short> _implemented = new HashSet<short>();
        private readonly HashSet<short> _variable = new HashSet<short>();
        private readonly HashSet<short> _int16Payload = new HashSet<short>();
        private readonly HashSet<short> _nodeHeader = new HashSet<short>();

        /// <summary>
        /// True when this generation packs TypeInfoFlags into the high four bits of the
        /// same Int16 that carries the assembly id, so the id has to be masked out.
        ///
        /// 4093 does this; 4074 keeps the flag in a separate field that is never
        /// serialized.
        /// </summary>
        public bool PacksTypeInfoFlags { get; private set; }

        /// <summary>
        /// True when an AttributeInfo record carries a BamlAttributeUsage byte between
        /// OwnerTypeId and Name. 4093 does; 4074 does not.
        /// </summary>
        public bool AttributeInfoHasUsage { get; private set; }

        /// <summary>Human readable generation label.</summary>
        public string Name { get; private set; }

        /// <summary>The FormatVersion tuple this generation writes, or -1/-1 when it has none.</summary>
        public short VersionMajor { get; private set; }
        public short VersionMinor { get; private set; }

        /// <summary>True when this generation's StartDocument carries a FormatVersion.</summary>
        public bool HasFormatVersion { get; private set; }

        /// <summary>The record framing.</summary>
        public BamlFraming Framing { get; private set; }

        /// <summary>Highest valid code (the value of LastRecordType).</summary>
        public short LastRecordType { get { return (short)(_names.Length - 1); } }

        private RecordProfile(string name, string[] names, BamlFraming framing,
                              short major, short minor, bool hasFormatVersion)
        {
            Name = name;
            _names = names;
            Framing = framing;
            VersionMajor = major;
            VersionMinor = minor;
            HasFormatVersion = hasFormatVersion;
        }

        private RecordProfile Implemented(params short[] codes)
        {
            for (int i = 0; i < codes.Length; i++) _implemented.Add(codes[i]);
            return this;
        }

        private RecordProfile Variable(params short[] codes)
        {
            for (int i = 0; i < codes.Length; i++) _variable.Add(codes[i]);
            return this;
        }

        private RecordProfile Int16Payload(params short[] codes)
        {
            for (int i = 0; i < codes.Length; i++) _int16Payload.Add(codes[i]);
            return this;
        }

        private RecordProfile NodeHeader(params short[] codes)
        {
            for (int i = 0; i < codes.Length; i++) _nodeHeader.Add(codes[i]);
            return this;
        }

        /// <summary>Marks the generation as packing TypeInfoFlags with the assembly id.</summary>
        private RecordProfile WithTypeInfoFlags()
        {
            PacksTypeInfoFlags = true;
            return this;
        }

        /// <summary>Marks AttributeInfo as carrying a usage byte.</summary>
        private RecordProfile WithAttributeUsage()
        {
            AttributeInfoHasUsage = true;
            return this;
        }

        /// <summary>Record kind name for a code, or a marker when out of range.</summary>
        public string NameOf(short code)
        {
            if (code < 0 || code >= _names.Length) return "??(" + code + ")";
            return _names[code];
        }

        /// <summary>True when the code names a record that has an implementation class.</summary>
        public bool IsImplemented(short code) { return _implemented.Contains(code); }

        /// <summary>True when the record carries a size field of its own.</summary>
        public bool IsVariableSized(short code) { return _variable.Contains(code); }

        /// <summary>True for fixed-size records carrying a 2-byte payload.</summary>
        public bool HasInt16Payload(short code) { return _int16Payload.Contains(code); }

        /// <summary>
        /// True for the LONG lineage's tree-node records, which carry a 12-byte header
        /// of depth, parent offset, right-sibling offset and left-sibling count.
        /// </summary>
        public bool HasNodeHeader(short code) { return _nodeHeader.Contains(code); }

        /// <summary>Code of a named record kind, or -1.</summary>
        public short CodeOf(string name)
        {
            for (short i = 0; i < _names.Length; i++)
            {
                if (string.Equals(_names[i], name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // the generations
        // ------------------------------------------------------------------

        /// <summary>
        /// Build 4074 — 34 members. Source:
        /// <c>反编译\4074\PresentationFramework\System.Windows.Serialization\BamlRecordType.cs</c>,
        /// <c>enum BamlRecordType : short</c>.
        ///
        /// Codes 20, 21, 22, 24, 26 and 27 have no record class, so they are declared
        /// but never live. <c>BamlWriterVersion = (0, 0)</c>.
        /// </summary>
        public static readonly RecordProfile Build4074 = new RecordProfile(
            "4074",
            new string[]
            {
                "Unknown", "DocumentStart", "DocumentEnd", "ElementStart", "ElementEnd",
                "Property", "PropertyCustom", "PropertyComplexStart", "PropertyComplexEnd",
                "PropertyArrayStart", "PropertyArrayEnd", "PropertyIListStart",
                "PropertyIListEnd", "PropertyIDictionaryStart", "PropertyIDictionaryEnd",
                "LiteralContent", "Text", "RoutedEvent", "ClrEvent", "XmlnsProperty",
                "XmlAttribute", "ProcessingInstruction", "Comment", "IncludeTag",
                "DefTag", "DefAttribute", "EndAttributes", "EndStartElement",
                "PIMapping", "AssemblyInfo", "TypeInfo", "TypeSerializerInfo",
                "AttributeInfo", "LastRecordType"
            },
            BamlFraming.Short, 0, 0, true)
            .Implemented(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 23, 25, 28, 29, 30, 31, 32)
            .Variable(1, 5, 6, 15, 16, 17, 19, 23, 25, 28, 29, 30, 31, 32)
            .Int16Payload(3, 7, 9, 11, 13);

        /// <summary>
        /// Build 4093 — 37 members. Source:
        /// <c>反编译\4093\PresentationFramework\System.Windows.Serialization\BamlRecordType.cs</c>,
        /// <c>enum BamlRecordType : byte</c>.
        ///
        /// <c>DefArrayStart</c>/<c>DefArrayEnd</c> exist at 24/25 and ARE implemented,
        /// so every later code shifts by two; <c>EndStartElement</c> is gone;
        /// <c>ResourceInfo</c> and <c>PropertyResourceReference</c> are new at 34/35.
        /// <c>BamlWriterVersion = (0, 1)</c>.
        /// </summary>
        public static readonly RecordProfile Build4093 = new RecordProfile(
            "4093",
            new string[]
            {
                "Unknown", "DocumentStart", "DocumentEnd", "ElementStart", "ElementEnd",
                "Property", "PropertyCustom", "PropertyComplexStart", "PropertyComplexEnd",
                "PropertyArrayStart", "PropertyArrayEnd", "PropertyIListStart",
                "PropertyIListEnd", "PropertyIDictionaryStart", "PropertyIDictionaryEnd",
                "LiteralContent", "Text", "RoutedEvent", "ClrEvent", "XmlnsProperty",
                "XmlAttribute", "ProcessingInstruction", "Comment", "IncludeTag",
                "DefArrayStart", "DefArrayEnd", "DefTag", "DefAttribute",
                "EndAttributes", "PIMapping", "AssemblyInfo", "TypeInfo",
                "TypeSerializerInfo", "AttributeInfo", "ResourceInfo",
                "PropertyResourceReference", "LastRecordType"
            },
            BamlFraming.Short, 0, 1, true)
            .Implemented(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 23, 24, 25, 27, 29, 30, 31, 32, 33, 34, 35)
            .Variable(1, 5, 6, 15, 16, 17, 19, 23, 24, 25, 27, 29, 30, 31, 32, 33, 34, 35)
            .Int16Payload(3, 7, 9, 11, 13)
            // Two payload changes distinguish 4093 from 4074 on the wire. Both are
            // transcribed from the decompiled LoadRecordData bodies, and both were
            // found by watching a 4093 stream desynchronise at the first TypeInfo.
            .WithTypeInfoFlags()
            .WithAttributeUsage();

        /// <summary>
        /// Build 3683 — 23 members, the LONG framing. Source:
        /// <c>反编译\3683\Avalon.Core\MS.Internal\BamlRecordType.cs</c>,
        /// <c>enum BamlRecordType</c>.
        ///
        /// This is the lineage that produced <c>example.baml</c> and <c>481.baml</c>. It
        /// carries no FormatVersion, so there is no version tuple to discriminate on --
        /// the framing itself is the discriminator.
        ///
        /// The first member is spelled <c>Uknown</c> in the decompile; the spelling is
        /// preserved so the table matches its source. There is no IncludeTag and no
        /// DynamicPropertyCustom in this build.
        ///
        /// No code is marked variable-sized: in this framing the size field is part of
        /// every record, so the "variable" distinction does not apply.
        /// </summary>
        public static readonly RecordProfile Build3683 = new RecordProfile(
            "3683",
            new string[]
            {
                "Uknown", "StartDocument", "EndDocument", "Element", "EndElement",
                "ParseLiteralContent", "XmlnsProperty", "DynamicProperty",
                "DynamicEvent", "GenericAttribute", "Text", "AssemblyInfo",
                "TypeInfo", "AttributeInfo", "ComplexDynamicProperty",
                "EndComplexDynamicProperty", "ClrObject", "EndClrObject",
                "ClrProperty", "ClrArrayProperty", "EndClrArrayProperty",
                "ClrComplexProperty", "EndClrComplexProperty", "LastRecordType"
            },
            BamlFraming.Long, -1, -1, false)
            // every code except the sentinel has a class in this build
            .Implemented(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 20, 21, 22)
            // the tree-node records, which carry the 12-byte header
            .NodeHeader(3, 5, 10, 16);

        /// <summary>
        /// The code table shared by 4039 and 4042, whose enums are identical.
        ///
        /// A property rather than a field so both profiles get their own array and
        /// cannot alias; built from one definition so the two cannot drift apart.
        /// </summary>
        private static string[] LateLongNames
        {
            get
            {
                return new string[]
                {
                    "Unknown", "StartDocument", "EndDocument", "Element", "EndElement",
                    "ParseLiteralContent", "XmlnsProperty", "DependencyProperty",
                    "RoutedEvent", "GenericAttribute", "Text", "AssemblyInfo",
                    "TypeInfo", "AttributeInfo", "ComplexDependencyProperty",
                    "EndComplexDependencyProperty", "ClrObject", "EndClrObject",
                    "ClrProperty", "ClrArrayProperty", "EndClrArrayProperty",
                    "IListProperty", "EndIListProperty", "ClrComplexProperty",
                    "EndClrComplexProperty", "IncludeTag", "IDictionaryProperty",
                    "EndIDictionaryProperty", "DictionaryKeyTag",
                    "DependencyPropertyCustom", "PIMapping", "ClrPropertyCustom",
                    "LastRecordType"
                };
            }
        }

        /// <summary>
        /// Build 3718 — 25 members, the LONG framing. Source:
        /// <c>反编译\3718\Avalon.Core\MS.Internal\BamlRecordType.cs</c>.
        ///
        /// Identical to 3683 except that <c>IncludeTag</c> is appended at 23; payload
        /// layouts are unchanged, which is why one name-based dispatch serves both.
        ///
        /// This is the profile the collection's <c>481.baml</c> needs: that file reaches
        /// type code 24, which a 23-member enum cannot express.
        /// </summary>
        public static readonly RecordProfile Build3718 = new RecordProfile(
            "3718",
            new string[]
            {
                "Unknown", "StartDocument", "EndDocument", "Element", "EndElement",
                "ParseLiteralContent", "XmlnsProperty", "DynamicProperty",
                "DynamicEvent", "GenericAttribute", "Text", "AssemblyInfo",
                "TypeInfo", "AttributeInfo", "ComplexDynamicProperty",
                "EndComplexDynamicProperty", "ClrObject", "EndClrObject",
                "ClrProperty", "ClrArrayProperty", "EndClrArrayProperty",
                "ClrComplexProperty", "EndClrComplexProperty", "IncludeTag",
                "LastRecordType"
            },
            BamlFraming.Long, -1, -1, false)
            .Implemented(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 20, 21, 22, 23)
            .NodeHeader(3, 5, 10, 16);

        /// <summary>
        /// Build 4015 — 26 members, the LONG framing. Source:
        /// <c>反编译\4015\System.Windows\MS.Internal\BamlRecordType.cs</c>.
        ///
        /// Adds <c>DynamicPropertyCustom</c> at 24. The assembly moved from
        /// <c>Avalon.Core</c> to <c>System.Windows</c> at this point.
        /// </summary>
        public static readonly RecordProfile Build4015 = new RecordProfile(
            "4015",
            new string[]
            {
                "Unknown", "StartDocument", "EndDocument", "Element", "EndElement",
                "ParseLiteralContent", "XmlnsProperty", "DynamicProperty",
                "DynamicEvent", "GenericAttribute", "Text", "AssemblyInfo",
                "TypeInfo", "AttributeInfo", "ComplexDynamicProperty",
                "EndComplexDynamicProperty", "ClrObject", "EndClrObject",
                "ClrProperty", "ClrArrayProperty", "EndClrArrayProperty",
                "ClrComplexProperty", "EndClrComplexProperty", "IncludeTag",
                "DynamicPropertyCustom", "LastRecordType"
            },
            BamlFraming.Long, -1, -1, false)
            .Implemented(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 20, 21, 22, 23, 24)
            .NodeHeader(3, 5, 10, 16);

        /// <summary>
        /// Build 4033 — 28 members, the LONG framing. Source:
        /// <c>反编译\4033\PresentationFramework\System.Windows.Serialization\BamlRecordType.cs</c>.
        ///
        /// The Avalon-to-WPF rename lands here: the <c>Dynamic</c> family becomes
        /// <c>DependencyID</c>, <c>IListProperty</c> is added, and the namespace becomes
        /// <c>System.Windows.Serialization</c>. The rename is lexical, so the payload
        /// dispatch is unchanged.
        /// </summary>
        public static readonly RecordProfile Build4033 = new RecordProfile(
            "4033",
            new string[]
            {
                "Unknown", "StartDocument", "EndDocument", "Element", "EndElement",
                "ParseLiteralContent", "XmlnsProperty", "DependencyIDProperty",
                "RoutedEvent", "GenericAttribute", "Text", "AssemblyInfo",
                "TypeInfo", "AttributeInfo", "ComplexDependencyIDProperty",
                "EndComplexDependencyIDProperty", "ClrObject", "EndClrObject",
                "ClrProperty", "ClrArrayProperty", "EndClrArrayProperty",
                "IListProperty", "EndIListProperty", "ClrComplexProperty",
                "EndClrComplexProperty", "IncludeTag", "DependencyIDPropertyCustom",
                "LastRecordType"
            },
            BamlFraming.Long, -1, -1, false)
            .Implemented(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 20, 21, 22, 23, 24, 25, 26)
            .NodeHeader(3, 5, 10, 16);

        /// <summary>
        /// Build 4039 — 33 members, the LONG framing. Source:
        /// <c>反编译\4039\PresentationFramework\MSAvalon.Windows.Serialization\BamlRecordType.cs</c>.
        ///
        /// Adds the dictionary records and <c>DictionaryKeyTag</c>, renames
        /// <c>DependencyIDProperty</c> to <c>DependencyProperty</c>, and brings in
        /// <c>PIMapping</c> and <c>ClrPropertyCustom</c>.
        /// </summary>
        public static readonly RecordProfile Build4039 = new RecordProfile(
            "4039", LateLongNames, BamlFraming.Long, -1, -1, false)
            .Implemented(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31)
            .NodeHeader(3, 5, 10, 16);

        /// <summary>
        /// Build 4042 — 33 members, the LONG framing. Source:
        /// <c>反编译\4042\PresentationFramework\MSAvalon.Windows.Serialization\BamlRecordType.cs</c>.
        ///
        /// Byte-identical enum to 4039, so it shares the table.
        /// </summary>
        public static readonly RecordProfile Build4042 = new RecordProfile(
            "4042", LateLongNames, BamlFraming.Long, -1, -1, false)
            .Implemented(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17,
                         18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31)
            .NodeHeader(3, 5, 10, 16);

        /// <summary>
        /// Every LONG-framing profile, oldest first.
        ///
        /// Exposed so a detector can try each in turn without the list being hard-coded
        /// in more than one place.
        /// </summary>
        public static readonly RecordProfile[] LongLineage =
        {
            Build3683, Build3718, Build4015, Build4033, Build4039, Build4042
        };
    }
}
