using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// One decoded record of any lineage.
    ///
    /// Fields are stored in declaration order, because print output and round-trip
    /// comparison both depend on that order and a dictionary would not preserve it.
    /// </summary>
    public sealed class BamlRecordEntry
    {
        private readonly List<KeyValuePair<string, object>> _fields =
            new List<KeyValuePair<string, object>>();

        /// <summary>Absolute offset of the record's first byte.</summary>
        public int Offset { get; internal set; }

        /// <summary>Numeric record code.</summary>
        public short Code { get; internal set; }

        /// <summary>Record kind name.</summary>
        public string Name { get; internal set; }

        /// <summary>
        /// For SHORT-framing variable-sized records, the value of the size field.
        /// For LONG-framing records, the total record length. For SHORT-framing
        /// fixed records, the total record length.
        /// </summary>
        public int Size { get; internal set; }

        /// <summary>Absolute offset just past this record.</summary>
        public int EndOffset { get; internal set; }

        /// <summary>Ordered named fields.</summary>
        public IList<KeyValuePair<string, object>> Fields { get { return _fields; } }

        internal BamlRecordEntry()
        {
            Offset = -1;
            EndOffset = -1;
        }

        internal void Add(string name, object value)
        {
            _fields.Add(new KeyValuePair<string, object>(name, value));
        }

        /// <summary>Field value by name, or null.</summary>
        public object Get(string name)
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

        /// <summary>Field as string, or the supplied default.</summary>
        public string GetString(string name, string fallback)
        {
            object v = Get(name);
            return v == null ? fallback : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        /// <summary>Field as int, or the supplied default.</summary>
        public int GetInt32(string name, int fallback)
        {
            object v = Get(name);
            return v == null ? fallback : Convert.ToInt32(v, CultureInfo.InvariantCulture);
        }

        /// <summary>True when at least one field of this entry is named <paramref name="name"/>.</summary>
        public bool Has(string name)
        {
            return Get(name) != null;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append('@');
            sb.Append(Offset.ToString(CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(Name);
            sb.Append(" size=");
            sb.Append(Size.ToString(CultureInfo.InvariantCulture));
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
