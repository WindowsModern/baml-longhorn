using System;
using System.Collections.Generic;

namespace BamlLonghorn.Dialects
{
    /// <summary>
    /// Reader for the Core-Avalon flat record stream: the compiled-XAML format
    /// of the core pre-release Avalon assembly (System.Windows.dll 6.0.3708.0,
    /// SHA256 B286F1DC...B9C5), recovered by decompiling
    /// MS.Internal\Baml*Record.cs.
    ///
    /// Framing (established empirically; see docs/CORE-AVALON-BAML-SPEC.md):
    ///
    ///     int64 recordSize   TOTAL record length, INCLUDING this 8-byte field
    ///     int16 recordType
    ///     payload            recordSize - 10 bytes, plus a pad byte if needed
    ///
    /// The stream is flat: no magic number, no version header, no interning
    /// table.  Records are read until the size field is &lt;= 0 or fewer than
    /// 8 bytes remain.
    ///
    /// NOTE: no file in either known sample corpus is written in this variant.
    /// It is validated by round-trip self-consistency (the reference Python
    /// implementation's selftest) and by this reader rejecting every real
    /// 4074 sample, which is what makes it usable as a dialect discriminator.
    /// </summary>
    public sealed class BamlDialectCoreAvalon : BamlDialectReaderBase
    {
        public override BamlDialect Dialect { get { return BamlDialect.CoreAvalon; } }

        public override string Name { get { return "Core Avalon (System.Windows 6.0.3708.0, flat record stream)"; } }

        /// <summary>
        /// Heuristic: the first record must be StartDocument (type 1) with a
        /// plausible size, and the following records must walk cleanly.  A wrong
        /// dialect almost always fails this immediately, which is what makes the
        /// detector usable as a discriminator against the 4074 format.
        /// </summary>
        public override int Detect(byte[] data)
        {
            if (data == null || data.Length < 20)
            {
                return 0;
            }

            int score = 0;

            long size = ReadInt64At(data, 0);
            short type = ReadInt16At(data, 8);

            if (size <= 0 || size > data.Length || (size % 2) != 0)
            {
                return 0;
            }
            if (type != (short)BamlRecordType.StartDocument)
            {
                return 0;
            }
            score += 40;

            int pos = 0;
            int walked = 0;
            while (pos + 10 <= data.Length && walked < 8)
            {
                long recordSize = ReadInt64At(data, pos);
                if (recordSize <= 0 || pos + recordSize > data.Length || (recordSize % 2) != 0)
                {
                    break;
                }
                short recordType = ReadInt16At(data, pos + 8);
                if (!IsPlausibleRecordType(recordType))
                {
                    // A single bad boundary invalidates the walk entirely.
                    return 0;
                }
                pos += (int)recordSize;
                walked++;
                score += 5;
            }

            if (walked >= 3 && pos >= data.Length - 16)
            {
                score += 20;
            }
            return Math.Min(score, 100);
        }

        public override BamlDocument Read(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            BamlDocument document = new BamlDocument();
            document.Dialect = BamlDialect.CoreAvalon;
            document.SourceLength = data.Length;

            BamlBinaryReader reader = new BamlBinaryReader(data);
            List<BamlRecord> records = document.Records;

            while (reader.Position + 10 <= reader.Length)
            {
                long recordStart = reader.Position;

                if (reader.Remaining < 8)
                {
                    break;
                }

                long recordSize = reader.ReadInt64();
                if (recordSize <= 0)
                {
                    // The original reader rewinds and reports "no more records".
                    break;
                }
                if (recordStart + recordSize > reader.Length)
                {
                    throw new BamlParseException(
                        "record claims size " + recordSize + " but only "
                        + (reader.Length - recordStart) + " bytes remain", recordStart);
                }

                long typeFieldOffset = recordStart + 8;
                short rawType = reader.ReadInt16();
                if (!IsPlausibleRecordType(rawType))
                {
                    throw new BamlParseException("unknown record type " + rawType, typeFieldOffset);
                }

                BamlRecord record = new BamlRecord((BamlRecordType)rawType);
                record.Offset = recordStart;
                record.Size = recordSize;
                record.TypeFieldOffset = typeFieldOffset;

                if (BamlRecordTypes.IsNodeRecord(record.RecordType))
                {
                    record.Node = ReadNodeHeader(reader, typeFieldOffset);
                }

                ReadPayload(reader, record);
                records.Add(record);

                // recordSize is the TOTAL length, which is the invariant the
                // original reader depends on.
                long next = recordStart + recordSize;
                if (next <= recordStart)
                {
                    throw new BamlParseException("record did not advance the stream", recordStart);
                }
                reader.Position = (int)next;
            }

            document.RecordCount = records.Count;
            return document;
        }

        /// <summary>
        /// Reads the 12-byte node header, rebasing the two relative offsets
        /// against <paramref name="typeFieldOffset"/>.
        /// </summary>
        private static BamlNodeHeader ReadNodeHeader(BamlBinaryReader reader, long typeFieldOffset)
        {
            BamlNodeHeader header = new BamlNodeHeader();
            header.Depth = reader.ReadInt16();

            int rawParent = reader.ReadInt32();
            int rawRight = reader.ReadInt32();

            // Positive values are relative to the record position; non-positive
            // values are the "none" sentinel and must be preserved verbatim.
            header.ParentOffset = rawParent > 0 ? (int)(rawParent + typeFieldOffset) : rawParent;
            header.RightSiblingOffset = rawRight > 0 ? (int)(rawRight + typeFieldOffset) : rawRight;
            header.LeftElementSiblingsCount = reader.ReadInt16();
            return header;
        }

        /// <summary>Reads the per-record payload fields.</summary>
        private static void ReadPayload(BamlBinaryReader reader, BamlRecord record)
        {
            switch (record.RecordType)
            {
                case BamlRecordType.StartDocument:
                {
                    int rawRoot = reader.ReadInt32();
                    record.AddField("rootElementOffset",
                        rawRoot > 0 ? (object)(int)(rawRoot + record.TypeFieldOffset) : rawRoot);
                    record.AddField("loadAsync", reader.ReadBoolean());
                    record.AddField("maxAsyncRecords", reader.ReadInt32());
                    break;
                }

                case BamlRecordType.Element:
                    record.AddField("id", reader.ReadInt16());
                    record.AddField("childNodes", reader.ReadInt16());
                    record.AddField("elementNodes", reader.ReadInt16());
                    {
                        int rawFirst = reader.ReadInt32();
                        record.AddField("firstChildOffset",
                            rawFirst > 0 ? (object)(int)(rawFirst + record.TypeFieldOffset) : rawFirst);
                    }
                    break;

                case BamlRecordType.ParseLiteralContent:
                    record.AddField("value", reader.ReadString());
                    record.AddField("lineNumber", reader.ReadInt32());
                    record.AddField("linePosition", reader.ReadInt32());
                    break;

                case BamlRecordType.XmlnsProperty:
                    record.AddField("prefix", reader.ReadString());
                    record.AddField("value", reader.ReadString());
                    break;

                case BamlRecordType.DynamicProperty:
                    record.AddField("attributeId", reader.ReadInt16());
                    record.AddField("value", reader.ReadString());
                    record.AddField("complex", reader.ReadBoolean());
                    break;

                case BamlRecordType.DynamicEvent:
                    record.AddField("attributeId", reader.ReadInt16());
                    record.AddField("value", reader.ReadString());
                    break;

                case BamlRecordType.GenericAttribute:
                    record.AddField("namespaceUri", reader.ReadString());
                    record.AddField("localName", reader.ReadString());
                    record.AddField("value", reader.ReadString());
                    break;

                case BamlRecordType.Text:
                    record.AddField("value", reader.ReadString());
                    break;

                case BamlRecordType.AssemblyInfo:
                    record.AddField("assemblyId", reader.ReadInt16());
                    record.AddField("assemblyFullName", reader.ReadString());
                    break;

                case BamlRecordType.TypeInfo:
                    record.AddField("typeId", reader.ReadInt16());
                    record.AddField("assemblyId", reader.ReadInt16());
                    record.AddField("typeFullName", reader.ReadString());
                    break;

                case BamlRecordType.AttributeInfo:
                    record.AddField("attributeId", reader.ReadInt16());
                    record.AddField("ownerTypeId", reader.ReadInt16());
                    record.AddField("name", reader.ReadString());
                    break;

                case BamlRecordType.ComplexDynamicProperty:
                    record.AddField("attributeId", reader.ReadInt16());
                    break;

                case BamlRecordType.ClrObject:
                    record.AddField("id", reader.ReadInt16());
                    break;

                case BamlRecordType.ClrProperty:
                    record.AddField("name", reader.ReadString());
                    record.AddField("value", reader.ReadString());
                    record.AddField("fieldTypeId", reader.ReadInt16());
                    break;

                case BamlRecordType.ClrArrayProperty:
                case BamlRecordType.ClrComplexProperty:
                    record.AddField("name", reader.ReadString());
                    break;

                case BamlRecordType.IncludeTag:
                    record.AddField("value", reader.ReadString());
                    break;

                default:
                    // End* records carry no payload; anything else is unknown
                    // and is left as an opaque record rather than throwing, so
                    // that a partially supported stream still yields a tree.
                    break;
            }
        }

        private static long ReadInt64At(byte[] data, int offset)
        {
            long value = 0;
            for (int i = 7; i >= 0; i--)
            {
                value = (value << 8) | data[offset + i];
            }
            return value;
        }

        private static short ReadInt16At(byte[] data, int offset)
        {
            return (short)(data[offset] | (data[offset + 1] << 8));
        }
    }
}
