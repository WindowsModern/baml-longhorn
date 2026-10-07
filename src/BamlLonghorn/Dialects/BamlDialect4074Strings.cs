using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn.Dialects
{
    /// <summary>One string located by the 4074 tokenizer.</summary>
    public sealed class Baml4074String
    {
        /// <summary>Absolute offset of the string's first byte of text.</summary>
        public int Offset { get; internal set; }

        /// <summary>"utf16" or "ascii7".</summary>
        public string Encoding { get; internal set; }

        /// <summary>Decoded text.</summary>
        public string Text { get; internal set; }

        /// <summary>Total bytes the token occupies, including its length prefix.</summary>
        public int TotalBytes { get; internal set; }

        /// <summary>Bytes of the text itself, excluding the length prefix.</summary>
        public int TextBytes
        {
            get { return Encoding == "utf16" ? Text.Length * 2 : Text.Length; }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0}] {1} {2}",
                Offset, Encoding, Text);
        }
    }

    /// <summary>
    /// Greedy string scanner for the 4074 dialect.
    ///
    /// This is retained for the reconnaissance view (<c>baml recon</c>): it reports
    /// every string together with the raw bytes between them, which is how the
    /// record framing was originally established. The structured record layer is
    /// <see cref="BamlRecordWalker"/>.
    /// </summary>
    public static class BamlDialect4074Strings
    {
        /// <summary>Tokenizes every length-prefixed string in the buffer.</summary>
        public static List<Baml4074String> Tokenize(byte[] data)
        {
            List<Baml4074String> tokens = new List<Baml4074String>();
            int pos = 0;
            while (pos < data.Length)
            {
                Baml4074String utf16 = TryUtf16(data, pos);
                Baml4074String ascii = TryAscii7(data, pos);

                Baml4074String best = null;
                if (utf16 != null && ascii != null)
                {
                    best = utf16.TotalBytes >= ascii.TotalBytes ? utf16 : ascii;
                }
                else if (utf16 != null)
                {
                    best = utf16;
                }
                else if (ascii != null)
                {
                    best = ascii;
                }

                if (best == null)
                {
                    pos++;
                    continue;
                }
                tokens.Add(best);
                pos += best.TotalBytes;
            }
            return tokens;
        }

        /// <summary>Detects a 4-byte-length-prefixed UTF-16LE string.</summary>
        public static Baml4074String TryUtf16(byte[] data, int pos)
        {
            if (pos + 4 > data.Length)
            {
                return null;
            }
            int length = data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24);
            if (length <= 0 || length > 4096 || (length % 2) != 0 || pos + 4 + length > data.Length)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder(length / 2);
            int printable = 0;
            for (int i = 0; i < length; i += 2)
            {
                char c = (char)(data[pos + 4 + i] | (data[pos + 5 + i] << 8));
                sb.Append(c);
                if (c >= ' ' && c < 0x7F)
                {
                    printable++;
                }
            }
            if (printable < (length / 2) - 1)
            {
                return null;
            }
            return new Baml4074String
            {
                Offset = pos + 4,
                Encoding = "utf16",
                Text = sb.ToString().TrimEnd('\0'),
                TotalBytes = 4 + length
            };
        }

        /// <summary>
        /// Detects a 7-bit-length-prefixed ASCII string, the encoding
        /// <c>BinaryReader.ReadString</c> uses.
        /// </summary>
        public static Baml4074String TryAscii7(byte[] data, int pos)
        {
            int p = pos;
            int length = 0;
            int shift = 0;
            while (true)
            {
                if (p >= data.Length || shift > 28)
                {
                    return null;
                }
                byte b = data[p++];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }
                shift += 7;
            }
            if (length < 3 || length > 4096 || p + length > data.Length)
            {
                return null;
            }
            for (int i = 0; i < length; i++)
            {
                byte b = data[p + i];
                if (b < 0x20 || b >= 0x7F)
                {
                    return null;
                }
            }
            return new Baml4074String
            {
                Offset = p,
                Encoding = "ascii7",
                Text = Encoding.ASCII.GetString(data, p, length),
                TotalBytes = (p - pos) + length
            };
        }
    }
}
