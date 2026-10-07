using System;

namespace BamlLonghorn
{
    /// <summary>
    /// Thrown when a BAML stream is malformed or violates the dialect's rules.
    /// Carries the byte offset so the caller can point at the offending record.
    /// </summary>
    public class BamlParseException : Exception
    {
        /// <summary>Absolute byte offset in the stream where the problem was found.</summary>
        public long Offset { get; private set; }

        /// <summary>Record type being read when the problem occurred, if known.</summary>
        public BamlRecordType RecordType { get; private set; }

        public BamlParseException(string message)
            : base(message)
        {
            Offset = -1;
            RecordType = BamlRecordType.Unknown;
        }

        public BamlParseException(string message, long offset)
            : base(message + " (at offset " + offset.ToString() + ")")
        {
            Offset = offset;
            RecordType = BamlRecordType.Unknown;
        }

        public BamlParseException(string message, long offset, BamlRecordType recordType)
            : base(message + " (at offset " + offset.ToString() + ", record " + recordType.ToString() + ")")
        {
            Offset = offset;
            RecordType = recordType;
        }

        public BamlParseException(string message, Exception inner)
            : base(message, inner)
        {
            Offset = -1;
            RecordType = BamlRecordType.Unknown;
        }
    }
}
