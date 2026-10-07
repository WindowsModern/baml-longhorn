using System;
using System.Collections.Generic;
using System.Text;

using BamlLonghorn;

namespace BamlLonghorn.Dialects
{
    /// <summary>
    /// Reader for the SHORT-framing family: the compiled-XAML format used from build
    /// 4074 onward, whose records are
    ///
    /// <code>
    /// [int16 type] [int32 size, variable-sized records only] [payload]
    /// next = sizeFieldOffset + size = recordStart + 2 + size
    /// </code>
    ///
    /// and whose DocumentStart carries a <c>FormatVersion</c>: the feature identifier
    /// <c>"PreAlpha"</c> plus the tuple (reader, updater, writer).
    ///
    /// THREE GENERATIONS ARE READ HERE, and the tuple is what separates them:
    ///
    /// <code>
    /// 4074  34-member enum  tuple (0, 0)
    /// 4083  34-member enum  tuple (0, 0)   -- same wire format as 4074
    /// 4093  37-member enum  tuple (0, 1)
    /// </code>
    ///
    /// 4074 and 4083 are not distinguished on the wire: their runtime enums are
    /// identical member-for-member, and every record's <c>LoadRecordData</c> that was
    /// checked is byte-identical, with 4083's additions
    /// (<c>_serializerType</c>, <c>_parserContext</c>) sitting outside the read path. So
    /// one profile serves both and the difference is invisible to a decompiler, which is
    /// a property of the data rather than a gap in this reader.
    ///
    /// 4093 has its own enum and its own tuple, which is why it needs its own profile.
    /// The tuple check is what keeps a 4093 stream from being mis-parsed by the 4074
    /// code table: before it existed, such a stream survived several records and then
    /// failed on a code that is legal in one enum and not the other -- for example code
    /// 33, which is <c>AttributeInfo</c> in 4074 but <c>ResourceInfo</c> in 4093.
    /// </summary>
    public sealed class BamlDialectShort : BamlDialectReaderBase
    {
        /// <summary>The FormatVersion feature identifier written by every SHORT document.</summary>
        public const string Signature = "PreAlpha";

        private readonly RecordProfile _profile;
        private readonly short _expectedMajor;
        private readonly short _expectedMinor;

        /// <summary>Reader for build 4074 (and, identically, 4083).</summary>
        public static BamlDialectShort Create4074()
        {
            return new BamlDialectShort(RecordProfile.Build4074, "4074",
                RecordProfile.Build4074.VersionMajor, RecordProfile.Build4074.VersionMinor);
        }

        /// <summary>Reader for build 4093.</summary>
        public static BamlDialectShort Create4093()
        {
            return new BamlDialectShort(RecordProfile.Build4093, "4093",
                RecordProfile.Build4093.VersionMajor, RecordProfile.Build4093.VersionMinor);
        }

        private BamlDialectShort(RecordProfile profile, string label,
            short major, short minor)
        {
            _profile = profile;
            _expectedMajor = major;
            _expectedMinor = minor;
        }

        /// <summary>The profile this reader uses.</summary>
        public RecordProfile Profile { get { return _profile; } }

        public override BamlDialect Dialect
        {
            get
            {
                return _profile.Name == "4093" ? BamlDialect.Build4093 : BamlDialect.Build4074;
            }
        }

        public override string Name
        {
            get
            {
                return "Longhorn " + _profile.Name
                       + " (SHORT framing, System.Windows.Serialization)";
            }
        }

        /// <summary>
        /// Structural test: the stream must open with a DocumentStart record whose
        /// FormatVersion feature identifier is the signature AND whose version tuple
        /// matches this profile's.
        ///
        /// The signature alone is worthless -- every SHORT generation writes "PreAlpha"
        /// -- so the tuple carries the whole distinction. A mismatched tuple scores 50,
        /// which is below <see cref="BamlDetector.MinConfidence"/> and is therefore a
        /// refusal rather than a confident misparse.
        /// </summary>
        public override int Detect(byte[] data)
        {
            if (data == null || data.Length < 32)
            {
                return 0;
            }

            // offset 0 must be DocumentStart
            short documentStart = _profile.CodeOf("DocumentStart");
            short code = (short)(data[0] | (data[1] << 8));
            if (code != documentStart)
            {
                return 0;
            }
            int score = 20;

            // a 4-byte size field follows, then the FormatVersion feature string
            int size = data[2] | (data[3] << 8) | (data[4] << 16) | (data[5] << 24);
            if (size <= 0 || size > data.Length)
            {
                return 0;
            }
            score += 10;

            // feature string: Int32 byte length, then UTF-16LE
            int byteLength = data[6] | (data[7] << 8) | (data[8] << 16) | (data[9] << 24);
            if (byteLength <= 0 || (byteLength % 2) != 0 || 10 + byteLength > data.Length)
            {
                return 0;
            }
            string feature = Encoding.Unicode.GetString(data, 10, byteLength);
            if (!string.Equals(feature, Signature, StringComparison.Ordinal))
            {
                return score;
            }
            score += 20;

            // the three (Int16, Int16) version tuples follow, DWord-padded
            int pad = (4 - (byteLength % 4)) % 4;
            int versionOffset = 10 + byteLength + pad;
            if (versionOffset + 12 > data.Length)
            {
                return score;
            }
            short readerMajor = (short)(data[versionOffset] | (data[versionOffset + 1] << 8));
            short readerMinor = (short)(data[versionOffset + 2] | (data[versionOffset + 3] << 8));
            if (readerMajor == _expectedMajor && readerMinor == _expectedMinor)
            {
                score += 50;
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
            document.Dialect = Dialect;
            document.SourceLength = data.Length;

            BamlRecordWalker.Result result = BamlRecordWalker.Read(data, _profile);
            document.Entries = result.Records;
            document.RecordCount = result.Records.Count;
            document.IsCompleteParse = result.IsClean;
            if (!result.IsClean)
            {
                document.Note = "walk stopped at offset "
                                + result.EndOffset + " of " + result.Length
                                + ": " + result.Error;
            }

            // keep the string-token view available for reconnaissance output
            document.Strings = BamlDialect4074Strings.Tokenize(data);
            return document;
        }
    }
}
