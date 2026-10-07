using System;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// Little-endian primitive reader for BAML streams, mirroring the exact
    /// encodings the original assembly uses.
    ///
    /// The original code reads through <c>System.IO.BinaryReader</c>, so all
    /// strings carry a 7-bit-encoded (LEB128-style) byte-length prefix.  This
    /// class makes those encodings explicit and bounds-checked, because a
    /// malformed prefix is the most likely way a fuzzed or foreign-dialect file
    /// would be misread.
    /// </summary>
    public sealed class BamlBinaryReader
    {
        private readonly byte[] _data;
        private int _position;

        public BamlBinaryReader(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            _data = data;
            _position = 0;
            PayloadEnd = -1;
        }

        /// <summary>Current absolute offset.</summary>
        public int Position
        {
            get { return _position; }
            set
            {
                if (value < 0 || value > _data.Length)
                {
                    throw new BamlParseException("seek out of range: " + value, _position);
                }
                _position = value;
            }
        }

        /// <summary>Total stream length.</summary>
        public int Length { get { return _data.Length; } }

        /// <summary>
        /// Absolute offset at which the record currently being read ends.
        ///
        /// Set by the record walker before a payload is read. Records whose
        /// payload width is not self-describing (<c>PropertyCustom</c>) use it to
        /// consume exactly the bytes the size field allocated.
        /// </summary>
        public int PayloadEnd { get; set; }

        /// <summary>
        /// Consumes the rest of the current record and returns it as hex.
        /// Returns an empty string when <see cref="PayloadEnd"/> is not set or is
        /// not ahead of the current position.
        /// </summary>
        public string ReadRemainingHex()
        {
            int count = PayloadEnd - _position;
            if (count <= 0)
            {
                return string.Empty;
            }
            StringBuilder sb = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(_data[_position + i].ToString("x2",
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            _position += count;
            return sb.ToString();
        }

        /// <summary>Bytes remaining.</summary>
        public int Remaining { get { return _data.Length - _position; } }

        /// <summary>Underlying buffer (read-only usage expected).</summary>
        public byte[] Buffer { get { return _data; } }

        private void Require(int count)
        {
            if (count < 0 || _position + count > _data.Length)
            {
                throw new BamlParseException(
                    "unexpected end of stream: need " + count + " byte(s), have " + Remaining,
                    _position);
            }
        }

        public byte ReadByte()
        {
            Require(1);
            return _data[_position++];
        }

        public short ReadInt16()
        {
            Require(2);
            short value = (short)(_data[_position] | (_data[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(_data[_position] | (_data[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public int ReadInt32()
        {
            Require(4);
            int value = _data[_position]
                        | (_data[_position + 1] << 8)
                        | (_data[_position + 2] << 16)
                        | (_data[_position + 3] << 24);
            _position += 4;
            return value;
        }

        public uint ReadUInt32()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64()
        {
            Require(8);
            long low = (uint)(_data[_position]
                        | (_data[_position + 1] << 8)
                        | (_data[_position + 2] << 16)
                        | (_data[_position + 3] << 24));
            long high = (uint)(_data[_position + 4]
                        | (_data[_position + 5] << 8)
                        | (_data[_position + 6] << 16)
                        | (_data[_position + 7] << 24));
            _position += 8;
            return low | (high << 32);
        }

        public bool ReadBoolean()
        {
            return ReadByte() != 0;
        }

        /// <summary>
        /// 7-bit-encoded unsigned integer, exactly as
        /// <c>BinaryReader.Read7BitEncodedInt</c> writes it: little-endian
        /// groups of 7 bits, high bit set on every byte but the last.
        /// </summary>
        public int Read7BitEncodedInt()
        {
            int result = 0;
            int shift = 0;
            while (true)
            {
                if (shift > 35)
                {
                    throw new BamlParseException("malformed 7-bit encoded integer", _position);
                }
                byte b = ReadByte();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }
                shift += 7;
            }
            return result;
        }

        /// <summary>
        /// Length-prefixed UTF-8 string, as <c>BinaryReader.ReadString</c>
        /// produces it.
        /// </summary>
        public string ReadString()
        {
            int length = Read7BitEncodedInt();
            if (length < 0)
            {
                throw new BamlParseException("negative string length " + length, _position);
            }
            Require(length);
            string value = Encoding.UTF8.GetString(_data, _position, length);
            _position += length;
            return value;
        }

        /// <summary>
        /// Length-prefixed UTF-16LE string.  The 4093 dialect uses this form,
        /// with a 4-byte length prefix.
        /// </summary>
        public string ReadUtf16String(int lengthPrefixBytes)
        {
            int length;
            if (lengthPrefixBytes == 4)
            {
                length = ReadInt32();
            }
            else if (lengthPrefixBytes == 2)
            {
                length = ReadUInt16();
            }
            else
            {
                throw new ArgumentOutOfRangeException("lengthPrefixBytes");
            }
            if (length < 0 || (length % 2) != 0)
            {
                throw new BamlParseException("implausible UTF-16 byte length " + length, _position);
            }
            Require(length);
            string value = Encoding.Unicode.GetString(_data, _position, length);
            _position += length;
            return value.TrimEnd('\0');
        }

        /// <summary>Peek a byte without consuming it.</summary>
        public byte PeekByte()
        {
            Require(1);
            return _data[_position];
        }

        /// <summary>Copy <paramref name="count"/> raw bytes.</summary>
        public byte[] ReadBytes(int count)
        {
            Require(count);
            byte[] result = new byte[count];
            Array.Copy(_data, _position, result, 0, count);
            _position += count;
            return result;
        }
    }
}
