using System;
using System.Collections.Generic;
using System.Text;

using BamlLonghorn;

namespace BamlLonghorn.Dialects
{
    /// <summary>
    /// Reader for the LONG-framing lineage: the pre-`System.Windows.Serialization`
    /// compiled XAML of Avalon (3683) and its immediate successors through 4042.
    ///
    /// These documents carry **no FormatVersion**, so there is no feature identifier or
    /// version tuple to detect on. The framing itself is the discriminator: an
    /// <c>[int64]</c> size followed by an <c>[int16]</c> type, whereas the later
    /// generation puts the type first. That makes detection a matter of testing whether
    /// the first eight bytes read as a sane record length AND the two bytes after them
    /// as an implemented code.
    ///
    /// The profile cannot always be pinned down, and that is expected rather than a
    /// defect: a small document that only uses early codes walks identically under every
    /// LONG profile. Detection therefore reports the **oldest profile that walks cleanly
    /// to EOF**, which is the most specific claim the bytes support. A document that
    /// needs a later code narrows it further, because the walk simply fails on an
    /// earlier profile.
    /// </summary>
    public sealed class BamlDialectLong : BamlDialectReaderBase
    {
        private RecordProfile _profile;

        public override BamlDialect Dialect
        {
            get
            {
                // one dialect reader, but the label names the resolved build when known
                return BamlDialect.Build3683;
            }
        }

        public override string Name
        {
            get
            {
                return _profile == null
                    ? "Longhorn LONG framing (Avalon, pre-System.Windows.Serialization)"
                    : "Longhorn LONG framing (" + _profile.Name + " profile)";
            }
        }

        /// <summary>The profile the last successful read selected, or null.</summary>
        public RecordProfile Profile { get { return _profile; } }

        /// <summary>
        /// Structural test for the LONG framing.
        ///
        /// Reads the leading <c>int64</c> as a candidate record length and the following
        /// <c>int16</c> as a candidate code, then requires both to be sane and the code
        /// to be implemented in the oldest LONG profile. No FormatVersion is available
        /// to test, which is why this is a shape test rather than a signature test.
        /// </summary>
        public override int Detect(byte[] data)
        {
            if (data == null || data.Length < 24)
            {
                return 0;
            }

            long size = BitConverter.ToInt64(data, 0);
            if (size < 10 || size > data.Length)
            {
                return 0;
            }

            short code = BitConverter.ToInt16(data, 8);
            // StartDocument is the first record of every LONG document
            short startDocument = RecordProfile.Build3683.CodeOf("StartDocument");
            if (code != startDocument)
            {
                return 0;
            }
            int score = 60;

            // the record must end inside the stream
            if (size <= data.Length)
            {
                score += 10;
            }

            // and the stream must actually walk under some LONG profile
            for (int i = 0; i < RecordProfile.LongLineage.Length; i++)
            {
                BamlRecordWalker.Result result =
                    BamlRecordWalker.Read(data, RecordProfile.LongLineage[i]);
                if (result.IsClean && result.Records.Count > 0)
                {
                    // record which profile fits, so the label and the Notes can name the
                    // generation rather than saying "some LONG document"
                    _profile = RecordProfile.LongLineage[i];
                    return 100;
                }
            }
            return score;
        }

        /// <summary>
        /// Reads the document, selecting the oldest LONG profile that walks cleanly.
        ///
        /// Trying oldest-first is deliberate. Every later profile is a superset of the
        /// codes the earlier ones define, so the oldest clean walk is the tightest fit;
        /// choosing the newest would silently accept codes the document never used.
        /// </summary>
        public override BamlDocument Read(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            BamlRecordWalker.Result best = null;
            RecordProfile bestProfile = null;

            for (int i = 0; i < RecordProfile.LongLineage.Length; i++)
            {
                BamlRecordWalker.Result attempt =
                    BamlRecordWalker.Read(data, RecordProfile.LongLineage[i]);
                if (attempt.IsClean && attempt.Records.Count > 0)
                {
                    best = attempt;
                    bestProfile = RecordProfile.LongLineage[i];
                    break;
                }
                // keep the furthest-reaching attempt so a partial read can be reported
                if (best == null || attempt.Records.Count > best.Records.Count)
                {
                    best = attempt;
                    bestProfile = RecordProfile.LongLineage[i];
                }
            }

            _profile = bestProfile;

            BamlDocument document = new BamlDocument();
            document.Dialect = BamlDialect.Build3683;
            document.SourceLength = data.Length;
            document.Entries = best.Records;
            document.RecordCount = best.Records.Count;
            document.IsCompleteParse = best.IsClean;
            if (!best.IsClean)
            {
                document.Note = "walk stopped at offset " + best.EndOffset + " of "
                                + best.Length + " using the " + bestProfile.Name
                                + " profile: " + best.Error;
            }
            return document;
        }
    }
}
