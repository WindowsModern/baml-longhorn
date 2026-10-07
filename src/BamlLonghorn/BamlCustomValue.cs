using System;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// Value decoders for <c>PropertyCustom</c> records.
    ///
    /// <c>BamlPropertyCustomRecord.LoadRecordData</c> reads only the
    /// <c>AttributeId</c>; the value that follows is consumed later by
    /// <c>SetValueObject</c>, whose branch depends on the property's type. These
    /// decoders are transcribed from each type's <c>DeserializeFrom</c> in the
    /// build-4074 PresentationFramework decompile.
    ///
    /// The encodings are variable-width and tag-driven, not fixed-size, so a
    /// decoder must be chosen by type rather than guessed from the byte count.
    /// </summary>
    public static class BamlCustomValue
    {
        /// <summary>
        /// The unit kinds encoded in the low five bits of the packed scalar tag.
        ///
        /// Transcribed from the build-4074 <c>System.Windows.UnitType</c>:
        /// <code>
        /// public enum UnitType { Auto, Percent, Pixel }
        /// </code>
        /// so Auto = 0, Percent = 1, Pixel = 2. A value whose tag has the high bit
        /// clear is implicitly Pixel.
        /// </summary>
        public enum Unit
        {
            Auto = 0,
            Percent = 1,
            Pixel = 2
        }

        /// <summary>Result of decoding one custom value.</summary>
        public sealed class Decoded
        {
            /// <summary>Rendered text, or null when the type is unsupported.</summary>
            public string Text { get; internal set; }

            /// <summary>Bytes consumed, or 0 when decoding was not attempted.</summary>
            public int Bytes { get; internal set; }

            /// <summary>Why decoding failed, or null.</summary>
            public string Error { get; internal set; }
        }

        /// <summary>
        /// Decodes the packed scalar encoding shared by <c>Length</c>,
        /// <c>GridLength</c>, <c>Spacing</c> and <c>FontSize</c>.
        ///
        /// <code>
        /// byte b = ReadByte();
        /// if ((b &amp; 0x80) == 0) { type = Pixel; num = (int)b; }        // 1 byte
        /// else {
        ///     type = (UnitType)(b &amp; 0x1F);
        ///     switch (b &amp; 0xE0) {
        ///         case 128: num = ReadByte();    break;              // +1
        ///         case 192: num = ReadInt16();   break;              // +2
        ///         case 160: num = ReadInt32();   break;              // +4
        ///         default:  num = (float)ReadDouble(); break;        // +8
        ///     }
        /// }
        /// </code>
        /// </summary>
        public static Decoded ReadPackedLength(byte[] data, int offset, string unitSuffix)
        {
            Decoded result = new Decoded();
            if (offset >= data.Length)
            {
                result.Error = "no bytes";
                return result;
            }

            byte b = data[offset];
            int pos = offset + 1;
            int unit;
            double value;

            if ((b & 0x80) == 0)
            {
                unit = (int)Unit.Pixel;
                value = b;
            }
            else
            {
                unit = b & 0x1F;
                switch (b & 0xE0)
                {
                    case 0x80:
                        if (pos + 1 > data.Length) { result.Error = "truncated u8"; return result; }
                        value = data[pos];
                        pos += 1;
                        break;
                    case 0xC0:
                        if (pos + 2 > data.Length) { result.Error = "truncated i16"; return result; }
                        value = (short)(data[pos] | (data[pos + 1] << 8));
                        pos += 2;
                        break;
                    case 0xA0:
                        if (pos + 4 > data.Length) { result.Error = "truncated i32"; return result; }
                        value = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
                        pos += 4;
                        break;
                    default:
                        if (pos + 8 > data.Length) { result.Error = "truncated f64"; return result; }
                        value = BitConverter.ToDouble(data, pos);
                        pos += 8;
                        break;
                }
            }

            result.Bytes = pos - offset;
            result.Text = Format(value, (Unit)unit, unitSuffix);
            return result;
        }

        /// <summary>
        /// Decodes a <c>Thickness</c>. The first byte selects how many components
        /// follow: 1 = all four equal, 2 = symmetric, 4 = explicit.
        /// </summary>
        public static Decoded ReadThickness(byte[] data, int offset)
        {
            Decoded result = new Decoded();
            if (offset >= data.Length)
            {
                result.Error = "no bytes";
                return result;
            }

            int count = data[offset];
            int pos = offset + 1;
            decimal[] parts = new decimal[4];

            if (count == 1)
            {
                Decoded one = ReadPackedLength(data, pos, string.Empty);
                if (one.Error != null) { result.Error = one.Error; return result; }
                parts[0] = parts[1] = parts[2] = parts[3] = Parse(one.Text);
                pos += one.Bytes;
            }
            else if (count == 2)
            {
                Decoded a = ReadPackedLength(data, pos, string.Empty);
                if (a.Error != null) { result.Error = a.Error; return result; }
                pos += a.Bytes;
                Decoded b = ReadPackedLength(data, pos, string.Empty);
                if (b.Error != null) { result.Error = b.Error; return result; }
                pos += b.Bytes;
                parts[0] = parts[2] = Parse(a.Text);
                parts[1] = parts[3] = Parse(b.Text);
            }
            else if (count == 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    Decoded p = ReadPackedLength(data, pos, string.Empty);
                    if (p.Error != null) { result.Error = p.Error; return result; }
                    pos += p.Bytes;
                    parts[i] = Parse(p.Text);
                }
            }
            else
            {
                result.Error = "unknown thickness component count " + count;
                return result;
            }

            result.Bytes = pos - offset;
            result.Text = string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3}", parts[0], parts[1], parts[2], parts[3]);
            return result;
        }

        /// <summary>
        /// Decodes a serialized value of the form <c>[tag][string]</c>.
        ///
        /// This is the <c>SerializationBrushType.Other</c> branch of
        /// <c>Brush.SerializeOn</c>:
        ///
        /// <code>
        /// public void SerializeOn(BinaryWriter writer, string stringValue)
        /// {
        ///     writer.Write((byte)0);          // SerializationBrushType.Other
        ///     writer.Write(stringValue);      // BinaryWriter.Write(string)
        /// }
        /// </code>
        ///
        /// The string is a normal <c>BinaryWriter.Write(string)</c> value, i.e. a
        /// 7-bit-encoded byte length followed by UTF-8, so it is read that way
        /// rather than assuming a single length byte. Observed payloads include
        /// <c>#RRGGBB</c> colours and gradient mode names such as "Vertical".
        /// </summary>
        public static Decoded ReadTaggedString(byte[] data, int offset)
        {
            Decoded result = new Decoded();
            if (offset + 2 > data.Length)
            {
                result.Error = "too short";
                return result;
            }
            if (data[offset] != 0x00)
            {
                result.Error = "not an Other-tagged value";
                return result;
            }

            // 7-bit encoded length, as BinaryReader.ReadString decodes it
            int pos = offset + 1;
            int length = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= data.Length || shift > 35)
                {
                    result.Error = "bad length prefix";
                    return result;
                }
                byte b = data[pos++];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }
                shift += 7;
            }
            if (length <= 0 || pos + length > data.Length)
            {
                result.Error = "bad tagged-string length";
                return result;
            }

            for (int i = 0; i < length; i++)
            {
                byte c = data[pos + i];
                if (c < 0x20 || c >= 0x7F)
                {
                    result.Error = "tagged string is not printable";
                    return result;
                }
            }
            result.Bytes = (pos - offset) + length;
            result.Text = Encoding.ASCII.GetString(data, pos, length);
            return result;
        }

        /// <summary>
        /// Decodes a <c>SerializationBrushType.SolidColor</c> value:
        /// <c>[tag = 1][uint]</c> where the uint is already the packed ARGB colour.
        ///
        /// <c>KnownColor</c> is declared <c>: uint</c> and its members ARE the
        /// packed values (<c>Black = 4278190080u</c> == 0xFF000000), so the uint can
        /// be rendered directly as <c>#AARRGGBB</c>. Byte count: 5.
        /// </summary>
        public static Decoded ReadColor(byte[] data, int offset)
        {
            Decoded result = new Decoded();
            if (offset + 5 > data.Length)
            {
                result.Error = "too short";
                return result;
            }
            if (data[offset] != 0x01)
            {
                result.Error = "not a SolidColor tag";
                return result;
            }
            uint argb = (uint)(data[offset + 1]
                               | (data[offset + 2] << 8)
                               | (data[offset + 3] << 16)
                               | (data[offset + 4] << 24));
            result.Bytes = 5;
            result.Text = "#" + argb.ToString("X8", CultureInfo.InvariantCulture);
            return result;
        }

        /// <summary>Decodes an enum value, which is a plain <c>uint</c>.</summary>
        public static Decoded ReadUInt32(byte[] data, int offset)
        {
            Decoded result = new Decoded();
            if (offset + 4 > data.Length)
            {
                result.Error = "truncated uint";
                return result;
            }
            uint value = (uint)(data[offset] | (data[offset + 1] << 8)
                                | (data[offset + 2] << 16) | (data[offset + 3] << 24));
            result.Bytes = 4;
            result.Text = value.ToString(CultureInfo.InvariantCulture);
            return result;
        }

        private static decimal Parse(string text)
        {
            decimal value;
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return 0m;
        }

        private static string Format(double value, Unit unit, string unitSuffix)
        {
            string number = value == Math.Floor(value) && !double.IsInfinity(value)
                ? ((long)value).ToString(CultureInfo.InvariantCulture)
                : value.ToString("0.###", CultureInfo.InvariantCulture);

            switch (unit)
            {
                case Unit.Auto:
                    return "Auto";
                case Unit.Percent:
                    return number + "%";
                case Unit.Pixel:
                    return number + (unitSuffix == null ? "px" : unitSuffix);
                default:
                    return number + "u" + ((int)unit).ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
