using System;
using System.Collections.Generic;

namespace BamlLonghorn.Dialects
{
    /// <summary>
    /// Reads a single BAML revision.
    ///
    /// Longhorn shipped mutually incompatible revisions of the compiled-XAML
    /// format, so a dialect object owns both the detection test and the record
    /// decoder.
    /// </summary>
    public interface IBamlDialectReader
    {
        /// <summary>Which dialect this reader implements.</summary>
        BamlDialect Dialect { get; }

        /// <summary>Human readable name, for reports.</summary>
        string Name { get; }

        /// <summary>
        /// Confidence, 0..100, that <paramref name="data"/> is written in this
        /// dialect.  Must not throw and must not mutate the buffer.
        /// </summary>
        int Detect(byte[] data);

        /// <summary>
        /// Decode the whole stream.  Throws <see cref="BamlParseException"/> on
        /// malformed input.
        /// </summary>
        BamlDocument Read(byte[] data);
    }

    /// <summary>Shared helpers for dialect readers.</summary>
    public abstract class BamlDialectReaderBase : IBamlDialectReader
    {
        public abstract BamlDialect Dialect { get; }

        public abstract string Name { get; }

        public abstract int Detect(byte[] data);

        public abstract BamlDocument Read(byte[] data);

        /// <summary>
        /// True when the byte looks like a plausible 4074 record type code.
        /// Used by detection to test a candidate record boundary.
        /// </summary>
        protected static bool IsPlausibleRecordType(int value)
        {
            return value >= 0 && value <= 24;
        }
    }
}
