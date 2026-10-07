using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// One decoded BAML record, dialect independent.
    ///
    /// A record is a type plus an ordered set of named fields, which keeps the
    /// model generic enough to host both supported dialects while still being
    /// self-describing when printed.  Node records additionally expose a
    /// <see cref="BamlNodeHeader"/>.
    /// </summary>
    public sealed class BamlRecord
    {
        private readonly List<KeyValuePair<string, object>> _fields =
            new List<KeyValuePair<string, object>>();

        /// <summary>Record kind.</summary>
        public BamlRecordType RecordType { get; internal set; }

        /// <summary>
        /// Absolute offset of the record's start (the size field) in the stream.
        /// </summary>
        public long Offset { get; internal set; }

        /// <summary>
        /// Value of the record's size field.
        ///
        /// In the 4074 dialect this is the TOTAL record length, including the
        /// 8-byte size field itself, so the next record begins at
        /// <c>Offset + Size</c>.
        /// </summary>
        public long Size { get; internal set; }

        /// <summary>
        /// Offset of the record's type field, i.e. <c>Offset + 8</c> in the
        /// 4074 dialect.  This is the base that relative offsets are stored
        /// against.
        /// </summary>
        public long TypeFieldOffset { get; internal set; }

        /// <summary>Node header, for the record types that carry one.</summary>
        public BamlNodeHeader Node { get; internal set; }

        /// <summary>Ordered named fields.</summary>
        public IList<KeyValuePair<string, object>> Fields { get { return _fields; } }

        internal BamlRecord(BamlRecordType recordType)
        {
            RecordType = recordType;
            Offset = -1;
            TypeFieldOffset = -1;
        }

        /// <summary>Append a field, preserving declaration order.</summary>
        public void AddField(string name, object value)
        {
            _fields.Add(new KeyValuePair<string, object>(name, value));
        }

        /// <summary>Fetch a field value by name, or null when absent.</summary>
        public object GetField(string name)
        {
            for (int i = 0; i < _fields.Count; i++)
            {
                if (string.Equals(_fields[i].Key, name, StringComparison.Ordinal))
                {
                    return _fields[i].Value;
                }
            }
            return null;
        }

        /// <summary>Fetch a field as string, or the supplied default.</summary>
        public string GetString(string name, string defaultValue)
        {
            object value = GetField(name);
            return value == null ? defaultValue : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Fetch a field as int, or the supplied default.</summary>
        public int GetInt32(string name, int defaultValue)
        {
            object value = GetField(name);
            if (value == null)
            {
                return defaultValue;
            }
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Fetch a field as bool, or the supplied default.</summary>
        public bool GetBoolean(string name, bool defaultValue)
        {
            object value = GetField(name);
            if (value == null)
            {
                return defaultValue;
            }
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(RecordType.ToString());
            sb.Append(" @");
            sb.Append(Offset.ToString(CultureInfo.InvariantCulture));
            sb.Append(" size=");
            sb.Append(Size.ToString(CultureInfo.InvariantCulture));
            if (Node != null)
            {
                sb.Append(" [");
                sb.Append(Node.ToString());
                sb.Append(']');
            }
            for (int i = 0; i < _fields.Count; i++)
            {
                sb.Append(' ');
                sb.Append(_fields[i].Key);
                sb.Append('=');
                string s = _fields[i].Value as string;
                if (s != null)
                {
                    sb.Append('"');
                    sb.Append(s);
                    sb.Append('"');
                }
                else
                {
                    sb.Append(Convert.ToString(_fields[i].Value, CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }
    }
}
