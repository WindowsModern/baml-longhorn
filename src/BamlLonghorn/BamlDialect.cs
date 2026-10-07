using System;

namespace BamlLonghorn
{
    /// <summary>
    /// Which BAML generation a stream is written in.
    ///
    /// Longhorn shipped several revisions of the compiled-XAML format, and the assembly
    /// FileVersion string does NOT distinguish them: for example the 4074 and 4093
    /// PresentationFramework binaries report the same version yet declare different
    /// BamlRecordType enums (34 and 37 members). Detection is therefore structural --
    /// framing plus enum shape -- never version-string based.
    ///
    /// The members below are named after the **system build** the tables were taken
    /// from, which is the only reliable label: a directory named "3683" means the files
    /// were extracted from that build, not that any library reports version 3683.
    /// </summary>
    public enum BamlDialect
    {
        /// <summary>Not yet determined.</summary>
        Unknown = 0,

        /// <summary>
        /// Core-Avalon flat record stream, decoded from the decompiled
        /// System.Windows.dll 6.0.3708.0 (SHA256 B286F1DC...B9C5).
        ///
        /// Shape: no magic number, no version header, no interning table:
        ///     int64 recordSize   TOTAL record length, INCLUDING this field
        ///     int16 recordType
        ///     payload
        /// Tree-node records carry a 12-byte header of position-relative offsets.
        /// Specification: docs/CORE-AVALON-BAML-SPEC.md
        ///
        /// No sample file in either known corpus is written in this variant, so it is
        /// validated by round-trip self-consistency only.
        /// </summary>
        CoreAvalon = 1,

        /// <summary>
        /// PRIMARY TARGET: the compiled-XAML format of real Longhorn applications
        /// (build 4074 era), as shipped in Microsoft.Windows.WCPClient.
        ///
        /// Shape: SHORT framing (int16 type, then an optional int32 size), a
        /// FormatVersion carrying the feature identifier "PreAlpha" and the tuple
        /// (0, 0), then a genuine string/type interning table.
        /// Sample corpus: samples\xaml (103 files, 9 topic folders).
        /// Specification: docs/BAML4074-FORMAT.md
        /// </summary>
        Build4074 = 4074,

        /// <summary>
        /// The later SHORT-framing generation, whose FormatVersion tuple is (0, 1) and
        /// whose enum has 37 members. Refused by the 4074 reader, deliberately.
        /// </summary>
        Build4093 = 4093,

        /// <summary>
        /// The LONG-framing Avalon lineage: an int64 record size followed by an int16
        /// type, and **no FormatVersion at all**. Covers the 3683 avalon assemblies and
        /// their successors up to 4042; the reader selects the oldest profile that
        /// walks the stream cleanly.
        ///
        /// Samples: For Test (example.baml, 481.baml). Specification: docs/BAML3683-FORMAT.md
        /// </summary>
        Build3683 = 3683
    }
}
