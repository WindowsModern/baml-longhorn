using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// Reads the record layer of every known generation.
    ///
    /// Two framings exist and the profile selects between them:
    ///
    /// <code>
    /// SHORT (4074, 4093)
    ///     [int16 type] [int32 size, variable-sized records only] [payload]
    ///     next = sizeFieldOffset + size = recordStart + 2 + size
    ///
    /// LONG (3683, and the 4074-era System.Windows lineage)
    ///     [int64 size] [int16 type] [payload]
    ///     next = recordStart + size
    /// </code>
    ///
    /// The SHORT size rule comes from <c>BamlVariableSizedRecord.Write</c>, which takes
    /// <c>num</c> after writing the type field and sets <c>RecordSize = end - num</c>,
    /// so that size field counts itself plus the payload. The LONG rule comes from
    /// <c>BamlRecord.Write</c>, which seeks back to the record start and writes
    /// <c>RecordSize = end - start</c>, counting the size field, the type field and the
    /// payload together.
    ///
    /// Payload dispatch is by record NAME rather than by code, because the same name
    /// occupies different codes in each generation's enum -- and in the 3683 case one
    /// name (<c>IncludeTag</c>) does not exist at all.
    /// </summary>
    public static class BamlRecordWalker
    {
        /// <summary>Outcome of a walk over one stream.</summary>
        public sealed class Result
        {
            /// <summary>Decoded records, in stream order.</summary>
            public List<BamlRecordEntry> Records { get; internal set; }

            /// <summary>Offset at which the walk stopped.</summary>
            public int EndOffset { get; internal set; }

            /// <summary>Total stream length.</summary>
            public int Length { get; internal set; }

            /// <summary>Error text, or null when the walk reached the end.</summary>
            public string Error { get; internal set; }

            /// <summary>True when the walk consumed the stream exactly.</summary>
            public bool IsClean
            {
                get { return Error == null && EndOffset == Length; }
            }

            public Result()
            {
                Records = new List<BamlRecordEntry>();
            }
        }

        /// <summary>Walks the stream using the profile's framing.</summary>
        public static Result Read(byte[] data, RecordProfile profile)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }

            return profile.Framing == BamlFraming.Long
                ? ReadLong(data, profile)
                : ReadShort(data, profile);
        }

        // ------------------------------------------------------------------
        // SHORT framing
        // ------------------------------------------------------------------

        private static Result ReadShort(byte[] data, RecordProfile profile)
        {
            Result result = new Result();
            result.Length = data.Length;
            BamlBinaryReader reader = new BamlBinaryReader(data);

            while (reader.Position + 2 <= reader.Length)
            {
                int start = reader.Position;
                short code = reader.ReadInt16();

                string name;
                string failure = Classify(profile, code, start, out name);
                if (failure != null)
                {
                    result.EndOffset = start;
                    result.Error = failure;
                    return result;
                }

                BamlRecordEntry entry = NewEntry(start, code, name);

                if (profile.IsVariableSized(code))
                {
                    if (reader.Position + 4 > reader.Length)
                    {
                        return Fail(result, start, "no room for size field");
                    }
                    int sizeFieldOffset = reader.Position;      // recordStart + 2
                    int size = reader.ReadInt32();
                    int end = sizeFieldOffset + size;           // THE SHORT RULE
                    if (size < 0 || end > reader.Length || end < reader.Position)
                    {
                        return Fail(result, start, "size " + size + " does not fit");
                    }
                    entry.Size = size;
                    entry.EndOffset = end;

                    try
                    {
                        reader.PayloadEnd = end;
                        ReadPayload(reader, entry, name, start, profile);
                    }
                    catch (BamlParseException ex)
                    {
                        return Fail(result, start, "payload: " + ex.Message);
                    }

                    result.Records.Add(entry);
                    reader.Position = end;
                }
                else
                {
                    if (profile.HasInt16Payload(code))
                    {
                        if (reader.Position + 2 > reader.Length)
                        {
                            return Fail(result, start, "no room for Int16 payload");
                        }
                        entry.Add("int16", reader.ReadInt16());
                    }
                    entry.Size = reader.Position - start;
                    entry.EndOffset = reader.Position;
                    result.Records.Add(entry);
                }
            }

            result.EndOffset = reader.Position;
            return result;
        }

        // ------------------------------------------------------------------
        // LONG framing
        // ------------------------------------------------------------------

        private static Result ReadLong(byte[] data, RecordProfile profile)
        {
            Result result = new Result();
            result.Length = data.Length;
            BamlBinaryReader reader = new BamlBinaryReader(data);

            while (reader.Position + 10 <= reader.Length)
            {
                int start = reader.Position;
                long size = reader.ReadInt64();
                if (size <= 0 || start + size > reader.Length)
                {
                    return Fail(result, start, "size " + size + " does not fit");
                }
                int end = (int)(start + size);

                short code = reader.ReadInt16();
                string name;
                string failure = Classify(profile, code, start, out name);
                if (failure != null)
                {
                    result.EndOffset = start;
                    result.Error = failure;
                    return result;
                }

                BamlRecordEntry entry = NewEntry(start, code, name);
                entry.Size = (int)size;
                entry.EndOffset = end;

                try
                {
                    reader.PayloadEnd = end;
                    ReadPayload(reader, entry, name, start, profile);
                }
                catch (BamlParseException ex)
                {
                    return Fail(result, start, "payload: " + ex.Message);
                }

                result.Records.Add(entry);
                reader.Position = end;
            }

            result.EndOffset = reader.Position;
            return result;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static BamlRecordEntry NewEntry(int start, short code, string name)
        {
            BamlRecordEntry entry = new BamlRecordEntry();
            entry.Offset = start;
            entry.Code = code;
            entry.Name = name;
            return entry;
        }

        private static Result Fail(Result result, int offset, string message)
        {
            result.EndOffset = offset;
            result.Error = message;
            return result;
        }

        private static string Classify(RecordProfile profile, short code, int offset,
            out string name)
        {
            name = null;
            if (code < 0 || code > profile.LastRecordType)
            {
                return "type " + code + " not in the " + profile.Name + " enum";
            }
            if (!profile.IsImplemented(code))
            {
                return "type " + code + " (" + profile.NameOf(code)
                       + ") has no record class in " + profile.Name;
            }
            name = profile.NameOf(code);
            return null;
        }

        /// <summary>
        /// Reads a record payload, dispatching on the record NAME so one implementation
        /// serves every generation.
        ///
        /// All bodies are transcribed from the decompiled <c>LoadRecordData</c> methods.
        /// The LONG lineage's node records are read with offset-relative tree links
        /// (<c>FilePos</c>-based), which are kept as fields rather than resolved, since
        /// the record stream already carries the nesting.
        /// </summary>
        private static void ReadPayload(BamlBinaryReader reader, BamlRecordEntry entry,
            string name, int recordStart, RecordProfile profile)
        {
            switch (name)
            {
                // -- document ------------------------------------------------
                case "StartDocument":
                    // LONG: RootElement (offset-relative) + MaxAsyncRecords
                    entry.Add("rootElement", reader.ReadInt32() + recordStart);
                    entry.Add("maxAsyncRecords", reader.ReadInt32());
                    break;

                case "DocumentStart":
                    // SHORT: FormatVersion, LoadAsync, MaxAsyncRecords
                    {
                        string feature = ReadFormatVersion(reader, entry);
                        entry.Add("loadAsync", reader.ReadBoolean());
                        entry.Add("maxAsyncRecords", reader.ReadInt32());
                        entry.Fields.Insert(0,
                            new KeyValuePair<string, object>("featureId", feature));
                    }
                    break;

                case "EndDocument":
                case "DocumentEnd":
                    break;

                // -- tree nodes ----------------------------------------------
                case "Element":
                    ReadNodeHeader(reader, entry, recordStart);
                    entry.Add("id", reader.ReadInt16());
                    entry.Add("childNodes", reader.ReadInt16());
                    entry.Add("elementNodes", reader.ReadInt16());
                    entry.Add("firstChildOffset", reader.ReadInt32() + recordStart);
                    break;

                case "ClrObject":
                    ReadNodeHeader(reader, entry, recordStart);
                    entry.Add("id", reader.ReadInt16());
                    break;

                case "Text":
                    // SHORT Text is a string record; LONG Text is a node record.
                    // Both are handled here because the name is shared.
                    if (profile.Framing == BamlFraming.Long)
                    {
                        ReadNodeHeader(reader, entry, recordStart);
                    }
                    entry.Add("value", reader.ReadString());
                    break;

                case "ParseLiteralContent":
                case "LiteralContent":
                    if (profile.Framing == BamlFraming.Long)
                    {
                        ReadNodeHeader(reader, entry, recordStart);
                    }
                    entry.Add("value", reader.ReadString());
                    entry.Add("lineNumber", reader.ReadInt32());
                    entry.Add("linePosition", reader.ReadInt32());
                    break;

                case "EndElement":
                case "ElementEnd":
                case "EndClrObject":
                    break;

                // -- properties and attributes -------------------------------
                case "Property":
                case "RoutedEvent":
                    entry.Add("attributeId", reader.ReadInt16());
                    entry.Add("value", reader.ReadString());
                    break;

                case "PropertyCustom":
                    // BamlPropertyCustomRecord overrides LoadRecordData and reads only
                    // AttributeId -- there is no string. The value is a fixed-width
                    // serialized object whose layout depends on the property type, which
                    // is not in the stream, so the raw bytes are kept instead.
                    entry.Add("attributeId", reader.ReadInt16());
                    entry.Add("rawValue", reader.ReadRemainingHex());
                    break;

                case "DynamicProperty":
                case "DynamicEvent":
                case "DependencyIDProperty":
                case "DependencyProperty":
                    entry.Add("attributeId", reader.ReadInt16());
                    entry.Add("value", reader.ReadString());
                    break;

                case "DynamicPropertyCustom":
                case "DependencyIDPropertyCustom":
                case "DependencyPropertyCustom":
                case "ClrPropertyCustom":
                    // The "…Custom" records read only the attribute/reference id; the
                    // value is a type-dependent blob the stream does not describe, so
                    // the remaining bytes are kept rather than guessed at.
                    entry.Add("attributeId", reader.ReadInt16());
                    entry.Add("rawValue", reader.ReadRemainingHex());
                    break;

                case "ComplexDynamicProperty":
                case "ComplexDependencyIDProperty":
                case "ComplexDependencyProperty":
                    entry.Add("attributeId", reader.ReadInt16());
                    break;

                case "EndComplexDynamicProperty":
                case "EndComplexDependencyIDProperty":
                case "EndComplexDependencyProperty":
                    break;

                case "DictionaryKeyTag":
                    entry.Add("value", reader.ReadString());
                    break;

                case "GenericAttribute":
                    entry.Add("namespaceUri", reader.ReadString());
                    entry.Add("localName", reader.ReadString());
                    entry.Add("value", reader.ReadString());
                    break;

                case "XmlnsProperty":
                    entry.Add("prefix", reader.ReadString());
                    entry.Add("value", reader.ReadString());
                    break;

                case "IncludeTag":
                    entry.Add("value", reader.ReadString());
                    break;

                // -- CLR members ---------------------------------------------
                case "ClrProperty":
                    entry.Add("name", reader.ReadString());
                    entry.Add("value", reader.ReadString());
                    entry.Add("fieldTypeId", reader.ReadInt16());
                    break;

                case "ClrArrayProperty":
                case "ClrComplexProperty":
                case "IListProperty":
                case "IDictionaryProperty":
                    entry.Add("name", reader.ReadString());
                    break;

                case "EndClrArrayProperty":
                case "EndClrComplexProperty":
                case "EndIListProperty":
                case "EndIDictionaryProperty":
                    break;

                // -- interning tables ----------------------------------------
                case "AssemblyInfo":
                    entry.Add("assemblyId", reader.ReadInt16());
                    entry.Add("fullName", reader.ReadString());
                    break;

                case "TypeInfo":
                case "TypeSerializerInfo":
                    entry.Add("typeId", reader.ReadInt16());
                    {
                        short raw = reader.ReadInt16();
                        if (profile.PacksTypeInfoFlags)
                        {
                            // 4093 packs TypeInfoFlags into the high four bits of the
                            // same Int16 that carries the assembly id, which is why a
                            // naive read shows assemblyId=4096 rather than 0.
                            //   _flags = (TypeInfoFlags)(AssemblyId >> 12);
                            //   _assemblyId &= 4095;
                            entry.Add("assemblyId", raw & 0x0FFF);
                            entry.Add("typeFlags", (raw >> 12) & 0x0F);
                        }
                        else
                        {
                            entry.Add("assemblyId", raw);
                        }
                    }
                    entry.Add("typeFullName", reader.ReadString());
                    if (name == "TypeSerializerInfo")
                    {
                        entry.Add("serializerTypeId", reader.ReadInt16());
                    }
                    break;

                case "AttributeInfo":
                    entry.Add("attributeId", reader.ReadInt16());
                    entry.Add("ownerTypeId", reader.ReadInt16());
                    if (profile.AttributeInfoHasUsage)
                    {
                        // 4093 inserts a BamlAttributeUsage byte before the name:
                        //   AttributeId; OwnerTypeId; AttributeUsage = (byte); Name
                        entry.Add("attributeUsage", reader.ReadByte());
                    }
                    entry.Add("name", reader.ReadString());
                    break;

                case "PIMapping":
                    entry.Add("xmlns", reader.ReadString());
                    entry.Add("clrns", reader.ReadString());
                    entry.Add("assemblyId", reader.ReadInt16());
                    break;

                case "ResourceInfo":
                    entry.Add("resourceId", reader.ReadInt16());
                    entry.Add("value", reader.ReadString());
                    break;

                case "PropertyResourceReference":
                    entry.Add("attributeId", reader.ReadInt16());
                    entry.Add("resourceId", reader.ReadInt16());
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// The LONG lineage's 12-byte tree-node prefix, from
        /// <c>BamlNodeRecord.LoadRecordData</c>.
        ///
        /// The two offsets are stored relative to the record's own start and are
        /// re-based to absolute here, which is what makes them useful in a dump.
        /// </summary>
        private static void ReadNodeHeader(BamlBinaryReader reader,
            BamlRecordEntry entry, int recordStart)
        {
            entry.Add("depth", reader.ReadInt16());
            entry.Add("parentOffset", reader.ReadInt32() + recordStart);
            entry.Add("rightSiblingOffset", reader.ReadInt32() + recordStart);
            entry.Add("leftElementSiblingsCount", reader.ReadInt16());
        }

        /// <summary>
        /// Reads a SHORT-lineage <c>FormatVersion</c> and records its three version
        /// tuples.
        ///
        /// <c>FormatVersion.Read</c> builds a BinaryReader with Encoding.Unicode and
        /// calls ContainerUtilities.ReadByteLengthPrefixedDWordPaddedUnicodeString,
        /// i.e. an Int32 byte length, length/2 UTF-16 chars, then DWord padding --
        /// followed by reader/updater/writer as three (Int16, Int16) pairs.
        /// </summary>
        private static string ReadFormatVersion(BamlBinaryReader reader, BamlRecordEntry entry)
        {
            int byteLength = reader.ReadInt32();
            if (byteLength < 0 || (byteLength % 2) != 0 || reader.Position + byteLength > reader.Length)
            {
                throw new BamlParseException("bad feature-string length " + byteLength, reader.Position);
            }
            string text = Encoding.Unicode.GetString(reader.ReadBytes(byteLength));
            int pad = (4 - (byteLength % 4)) % 4;
            if (pad > 0)
            {
                reader.ReadBytes(pad);
            }

            entry.Add("readerVersion", reader.ReadInt16() + "." + reader.ReadInt16());
            entry.Add("updaterVersion", reader.ReadInt16() + "." + reader.ReadInt16());
            entry.Add("writerVersion", reader.ReadInt16() + "." + reader.ReadInt16());
            return text;
        }
    }
}
