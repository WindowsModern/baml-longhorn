using System;

namespace BamlLonghorn
{
    /// <summary>
    /// BAML record kinds for the 4074 dialect.
    ///
    /// The numeric values are significant: they are the on-disk int16 record
    /// type codes.  Derived from the decompiled
    /// MS.Internal\BamlRecordType.cs of System.Windows.dll 6.0.3708.0, whose
    /// member order fixes the values.
    /// </summary>
    public enum BamlRecordType : short
    {
        Unknown = 0,
        StartDocument = 1,
        EndDocument = 2,
        Element = 3,
        EndElement = 4,
        ParseLiteralContent = 5,
        XmlnsProperty = 6,
        DynamicProperty = 7,
        DynamicEvent = 8,
        GenericAttribute = 9,
        Text = 10,
        AssemblyInfo = 11,
        TypeInfo = 12,
        AttributeInfo = 13,
        ComplexDynamicProperty = 14,
        EndComplexDynamicProperty = 15,
        ClrObject = 16,
        EndClrObject = 17,
        ClrProperty = 18,
        ClrArrayProperty = 19,
        EndClrArrayProperty = 20,
        ClrComplexProperty = 21,
        EndClrComplexProperty = 22,
        IncludeTag = 23,
        DynamicPropertyCustom = 24
    }

    /// <summary>Helpers over <see cref="BamlRecordType"/>.</summary>
    public static class BamlRecordTypes
    {
        /// <summary>
        /// Records whose payload begins with the 12-byte node header
        /// (the BamlNodeRecord subclasses in the original assembly).
        /// </summary>
        public static bool IsNodeRecord(BamlRecordType type)
        {
            switch (type)
            {
                case BamlRecordType.Element:
                case BamlRecordType.ParseLiteralContent:
                case BamlRecordType.Text:
                case BamlRecordType.ClrObject:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Records whose payload is empty.</summary>
        public static bool IsEmptyRecord(BamlRecordType type)
        {
            switch (type)
            {
                case BamlRecordType.EndDocument:
                case BamlRecordType.EndElement:
                case BamlRecordType.EndComplexDynamicProperty:
                case BamlRecordType.EndClrObject:
                case BamlRecordType.EndClrArrayProperty:
                case BamlRecordType.EndClrComplexProperty:
                case BamlRecordType.Unknown:
                    return true;
                default:
                    return false;
            }
        }
    }
}
