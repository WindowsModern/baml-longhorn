using System;

namespace BamlLonghorn
{
    /// <summary>
    /// The 12-byte header that begins every tree-node record's payload in the
    /// 4074 dialect (Element, ParseLiteralContent, Text, ClrObject).
    ///
    /// On disk (see BamlNodeRecord.LoadRecordData):
    ///     int16 depth
    ///     int32 parentOffset          relative to the record's own position
    ///     int32 rightSiblingOffset    relative to the record's own position
    ///     int16 leftElementSiblingsCount
    ///
    /// "Relative" means: absolute = storedValue + recordPosition, where
    /// recordPosition is the offset of the record's type field
    /// (i.e. recordStart + 8).  A stored value &lt;= 0 means "none" — which is
    /// exactly why the encoding is relative, so the test is a simple sign test.
    /// </summary>
    public sealed class BamlNodeHeader
    {
        /// <summary>Size of this header on disk, in bytes.</summary>
        public const int SizeOnDisk = 12;

        /// <summary>Nesting depth; -1 when unset.</summary>
        public short Depth { get; internal set; }

        /// <summary>Absolute offset of the parent record, or -1 for none.</summary>
        public int ParentOffset { get; internal set; }

        /// <summary>Absolute offset of the next sibling, or -1 for none.</summary>
        public int RightSiblingOffset { get; internal set; }

        /// <summary>Count of preceding sibling elements (used for indexing).</summary>
        public short LeftElementSiblingsCount { get; internal set; }

        public BamlNodeHeader()
        {
            Depth = -1;
            ParentOffset = -1;
            RightSiblingOffset = -1;
        }

        /// <summary>True when there is a parent link (stored value was positive).</summary>
        public bool HasParent { get { return ParentOffset > 0; } }

        /// <summary>True when there is a right sibling (stored value was positive).</summary>
        public bool HasRightSibling { get { return RightSiblingOffset > 0; } }

        public override string ToString()
        {
            return string.Format("depth={0} parent={1} right={2} leftSiblings={3}",
                Depth,
                HasParent ? ParentOffset.ToString() : "-",
                HasRightSibling ? RightSiblingOffset.ToString() : "-",
                LeftElementSiblingsCount);
        }
    }
}
