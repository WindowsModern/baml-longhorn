using System;
using System.Collections.Generic;
using System.Globalization;

using BamlLonghorn.Dialects;

namespace BamlLonghorn
{
    /// <summary>
    /// A decoded BAML stream: the record list plus whatever dialect-specific
    /// structure the reader could justify.
    /// </summary>
    public sealed class BamlDocument
    {
        private readonly List<BamlRecord> _records = new List<BamlRecord>();
        private readonly List<KeyValuePair<string, object>> _headers =
            new List<KeyValuePair<string, object>>();

        /// <summary>Dialect this document was decoded as.</summary>
        public BamlDialect Dialect { get; internal set; }

        /// <summary>Size of the source stream in bytes.</summary>
        public int SourceLength { get; internal set; }

        /// <summary>Number of records decoded (0 for a recon-only dialect).</summary>
        public int RecordCount { get; internal set; }

        /// <summary>
        /// False when the dialect is only partially decoded, so callers know
        /// the record list is not a complete model of the document.
        /// </summary>
        public bool IsCompleteParse { get; internal set; }

        /// <summary>Optional explanation shown by the printer.</summary>
        public string Note { get; internal set; }

        /// <summary>Decoded records, in stream order.</summary>
        public List<BamlRecord> Records { get { return _records; } }

        /// <summary>Dialect-level header values (e.g. version byte).</summary>
        public List<KeyValuePair<string, object>> Headers { get { return _headers; } }

        /// <summary>
        /// String tokens, for the reconnaissance view. Populated by dialect
        /// readers that expose an unstructured string scan.
        /// </summary>
        public List<Baml4074String> Strings { get; internal set; }

        /// <summary>
        /// Decoded records of the SHORT-framing lineage, in stream order. Empty
        /// for dialects that decode into <see cref="Records"/> instead.
        /// </summary>
        public List<BamlRecordEntry> Entries { get; internal set; }

        internal BamlDocument()
        {
            Dialect = BamlDialect.Unknown;
            IsCompleteParse = true;
            Entries = new List<BamlRecordEntry>();
        }

        public void AddHeader(string name, object value)
        {
            _headers.Add(new KeyValuePair<string, object>(name, value));
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "BamlDocument({0}, {1} bytes, {2} records)",
                Dialect, SourceLength, RecordCount);
        }
    }

    /// <summary>
    /// Picks the dialect reader for a stream and exposes detection results.
    /// </summary>
    public static class BamlDetector
    {
        private static readonly IBamlDialectReader[] Readers =
        {
            // ORDER MATTERS ONLY FOR TIES, and there are none: the SHORT and LONG readers
            // are mutually exclusive, since one reads a type field where the other reads
            // the low half of an int64 size. 4074 is listed first because a clean 4074
            // score of 100 is the strongest claim any reader can make.
            BamlDialectShort.Create4074(),
            BamlDialectShort.Create4093(),
            new BamlDialectLong(),
            new BamlDialectCoreAvalon()
        };

        /// <summary>All registered dialect readers.</summary>
        public static IList<IBamlDialectReader> AllReaders
        {
            get { return Readers; }
        }

        /// <summary>Scores every reader against the buffer, best first.</summary>
        public static List<KeyValuePair<IBamlDialectReader, int>> Score(byte[] data)
        {
            List<KeyValuePair<IBamlDialectReader, int>> scores =
                new List<KeyValuePair<IBamlDialectReader, int>>();
            for (int i = 0; i < Readers.Length; i++)
            {
                int confidence;
                try
                {
                    confidence = Readers[i].Detect(data);
                }
                catch (Exception)
                {
                    confidence = 0;
                }
                scores.Add(new KeyValuePair<IBamlDialectReader, int>(Readers[i], confidence));
            }
            scores.Sort(delegate(KeyValuePair<IBamlDialectReader, int> a,
                                 KeyValuePair<IBamlDialectReader, int> b)
            {
                return b.Value.CompareTo(a.Value);
            });
            return scores;
        }

        /// <summary>
        /// Minimum score a reader must reach to be accepted.
        ///
        /// This is 100, not merely "greater than zero", because a partial match is
        /// the signature of a *different generation that shares some structure*.
        /// Concretely, the 4093 generation uses the same SHORT framing and the same
        /// "PreAlpha" feature identifier as 4074, so it scores 50; only the (0, 0)
        /// version tuple pushes 4074 to 100. Accepting a 50 would let a 4093 stream
        /// be parsed with 4074's 34-member enum, which survives a handful of records
        /// and then fails confusingly in the middle of the file.
        /// </summary>
        public const int MinConfidence = 100;

        /// <summary>
        /// The highest score seen by the most recent <see cref="Detect"/> call,
        /// whether or not it was accepted. Callers that want to report "this looked
        /// like 50% 4074" rather than a bare refusal can read it here.
        /// </summary>
        public static int LastConfidence { get; private set; }

        /// <summary>
        /// Returns the best reader and its confidence, or null when nothing
        /// recognises the stream well enough to be trusted.
        /// </summary>
        public static IBamlDialectReader Detect(byte[] data, out int confidence)
        {
            List<KeyValuePair<IBamlDialectReader, int>> scores = Score(data);
            confidence = scores.Count == 0 ? 0 : scores[0].Value;
            LastConfidence = confidence;
            if (scores.Count == 0 || scores[0].Value < MinConfidence)
            {
                return null;
            }
            return scores[0].Key;
        }

        /// <summary>Detects and decodes in one call.</summary>
        public static BamlDocument Load(byte[] data)
        {
            int confidence;
            IBamlDialectReader reader = Detect(data, out confidence);
            if (reader == null)
            {
                throw new BamlParseException(
                    "stream is not recognised as any known Longhorn BAML dialect");
            }
            return reader.Read(data);
        }
    }
}
