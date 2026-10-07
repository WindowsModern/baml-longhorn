using System;
using System.Collections.Generic;
using System.Text;

namespace BamlLonghorn
{
    /// <summary>
    /// How a Longhorn element or attribute name corresponds to WPF.
    /// </summary>
    public enum LhMapKind
    {
        /// <summary>This generation already names a type that exists in WPF. No rewrite.</summary>
        Identical,

        /// <summary>The element became a differently named WPF type with the same role.</summary>
        Renamed,

        /// <summary>The role exists in WPF but the element has no direct equivalent, so a
        /// substitute type has to stand in. Emitted only with an explanatory comment.</summary>
        Substituted,

        /// <summary>No WPF equivalent. Kept, moved into the <c>lh</c> namespace, and annotated.</summary>
        Unsupported
    }

    /// <summary>One element mapping, with the evidence for it.</summary>
    public sealed class LhElementMapping
    {
        public string LhName;
        public string WpfName;
        public LhMapKind Kind;
        public string Note;

        public LhElementMapping(string lh, string wpf, LhMapKind kind, string note)
        {
            LhName = lh;
            WpfName = wpf;
            Kind = kind;
            Note = note;
        }
    }

    /// <summary>One attribute mapping on a given element, or on any element when Owner is null.</summary>
    public sealed class LhAttributeMapping
    {
        public string Owner;
        public string LhName;
        public string WpfName;
        public string Note;

        public LhAttributeMapping(string owner, string lh, string wpf, string note)
        {
            Owner = owner;
            LhName = lh;
            WpfName = wpf;
            Note = note;
        }
    }

    /// <summary>
    /// The Longhorn to WPF mapping tables.
    ///
    /// Every entry here was checked against real WPF source or real WPF assemblies rather than
    /// recalled. Three kinds of evidence were used, and each mapping records which applies:
    ///
    ///   * the type is simply renamed and WPF has the same member shape. For example
    ///     <c>PropertyTrigger</c> and WPF <c>Trigger</c> both expose <c>Property</c>,
    ///     <c>Value</c> and a setter collection, so the rename carries no semantic change.
    ///   * the type is renamed and a member is renamed too, so both have to move together.
    ///     <c>Set</c> becomes <c>Setter</c>, and its <c>PropertyPath</c> becomes
    ///     <c>Property</c>: WPF's <c>Setter</c> declares <c>DependencyProperty Property</c>,
    ///     not a path.
    ///   * the type has no counterpart at all and is marked unsupported rather than guessed at.
    ///
    /// The unsupported set is deliberately large. Roughly three quarters of the element types in
    /// the available corpora belong to Longhorn's shell and explorer layers
    /// (<c>MS.Internal.Desktop.*</c>, <c>System.Windows.WCPExplorer.*</c>,
    /// <c>System.Windows.Explorer.Controls.*</c>) and were never part of any released WPF, so no
    /// mapping can honestly be invented for them.
    /// </summary>
    public static class LhWpfMappings
    {
        private static readonly LhElementMapping[] _elements = new LhElementMapping[]
        {
            // ---- renamed, member shape identical ------------------------------------
            new LhElementMapping("HyperLink", "Hyperlink", LhMapKind.Renamed,
                "WPF spells it with a lower-case l"),
            new LhElementMapping("System.Windows.PropertyTrigger", "Trigger", LhMapKind.Renamed,
                "same members: Property, Value, setters"),
            new LhElementMapping("System.Windows.Data.Bind", "Binding", LhMapKind.Renamed,
                "member names differ; see the attribute table"),

            // ---- renamed, with a member rename as well ------------------------------
            new LhElementMapping("Set", "Setter", LhMapKind.Renamed,
                "Setter uses Property, not PropertyPath"),

            // ---- role exists, no direct equivalent, substitute with a comment --------
            new LhElementMapping("TextPanel", "TextBlock", LhMapKind.Substituted,
                "Longhorn's text container maps to TextBlock"),
            // Longhorn's Text is a container that holds a run of text, and the WPF type that plays
            // that role is TextBlock. It was first mapped to Run, which is wrong in a way only a
            // real parse reveals: a Run cannot hold content text directly, so
            // <Run ID="x">Hello</Run> fails at load time with a constructor binding error, and a Run
            // is only valid inside a text-bearing parent.
            new LhElementMapping("Text", "TextBlock", LhMapKind.Substituted,
                "Longhorn's text run maps to TextBlock, which can hold content text where Run cannot"),
            new LhElementMapping("FlowPanel", "WrapPanel", LhMapKind.Substituted,
                "flow layout; WrapPanel approximates it but does not wrap identically"),
            new LhElementMapping("System.Windows.Controls.GridPanel", "Grid", LhMapKind.Substituted,
                "Longhorn's grid panel maps to Grid"),
            new LhElementMapping("System.Windows.Controls.TransformDecorator", "Decorator", LhMapKind.Substituted,
                "WPF applies RenderTransform/LayoutTransform instead of a decorator"),

            // ---- explicitly unsupported families -----------------------------------
            // These are listed so the report names them rather than treating them as unknown.
            new LhElementMapping("System.Windows.Controls.ColumnStyle", null, LhMapKind.Unsupported,
                "Longhorn grid column styling; WPF uses ColumnDefinition and SharedSizeGroup"),
            new LhElementMapping("System.Windows.Controls.ColumnStyles", null, LhMapKind.Unsupported,
                "Longhorn grid column styling; WPF uses ColumnDefinition and SharedSizeGroup"),
            new LhElementMapping("System.Windows.Data.ObjectDataSource", null, LhMapKind.Unsupported,
                "no WPF equivalent; WPF binds to objects directly"),
            new LhElementMapping("System.Windows.Data.TransformerSource", null, LhMapKind.Unsupported,
                "Longhorn value-transformer plumbing; WPF uses IValueConverter"),
            new LhElementMapping("System.Windows.Media.NineGridBrush", null, LhMapKind.Unsupported,
                "nine-grid scaling has no WPF brush equivalent"),
            new LhElementMapping("System.Windows.Data.CollectionContainer", null, LhMapKind.Unsupported,
                "Longhorn collections view; WPF uses CollectionViewSource"),
        };

        private static readonly LhAttributeMapping[] _attributes = new LhAttributeMapping[]
        {
            // Setter: Longhorn names the target with a path, WPF with a dependency property.
            new LhAttributeMapping("Set", "PropertyPath", "Property",
                "WPF Setter.Property takes a DependencyProperty, not a path"),

            // Longhorn marks an element for lookup by name with ID; WPF uses the XAML language
            // directive x:Name. This is the most common reason converted output fails to load --
            // "cannot set unknown member DockPanel.ID" -- because ID is not a member of any WPF type.
            new LhAttributeMapping(null, "ID", "x:Name", "Longhorn's ID is WPF's x:Name"),
            new LhAttributeMapping(null, "IDREF", "x:Name", "Longhorn's IDREF is WPF's x:Name"),

            // Wrapping: Longhorn spells it TextWrap, and Wrap on other types. WPF uses TextWrapping.
            new LhAttributeMapping(null, "TextWrap", "TextWrapping", null),
            new LhAttributeMapping(null, "Wrap", "TextWrapping", null),

            // WPF has TabStop on some types and IsTabStop on Control; IsTabStop covers the cases
            // seen in the corpus.
            new LhAttributeMapping(null, "TabStop", "IsTabStop", null),

            // Longhorn spells the size of a Rectangle's fill separately from the Rectangle's own
            // Width and Height. WPF has only the latter, so the two names collapse onto it -- and
            // this had to be a rename rather than a pass-through, because RectangleWidth is not a
            // member of any WPF type and its percentage value then escaped the length handling.
            new LhAttributeMapping("Rectangle", "RectangleWidth", "Width",
                "Longhorn's rectangle fill width is WPF's Width"),
            new LhAttributeMapping("Rectangle", "RectangleHeight", "Height",
                "Longhorn's rectangle fill height is WPF's Height"),

            // Longhorn's slider carries TrackLength, which WPF's Slider does not have -- its track
            // follows the control's own size. The name is dropped rather than passed through, because
            // a member no WPF type declares makes the document unparseable.
            new LhAttributeMapping("System.Windows.Controls.HorizontalSlider", "TrackLength", null,
                "WPF Slider sizes its track from the control"),
            new LhAttributeMapping("System.Windows.Controls.VerticalSlider", "TrackLength", null,
                "WPF Slider sizes its track from the control"),

            // Dock is an ATTACHED property, so it is written on the CHILD, never on the DockPanel:
            //
            //     <DockPanel>
            //       <TextBlock DockPanel.Dock="Left" />
            //     </DockPanel>
            //
            // The entry here used to name the DockPanel as the owner, which matched only the one
            // case where the attribute never appears, so a child's Dock was emitted unchanged and a
            // reader reported "cannot set unknown member TextBlock.Dock". No WPF type has a member
            // called Dock, so the mapping is unconditional.
            new LhAttributeMapping(null, "Dock", "DockPanel.Dock",
                "Dock is an attached property and belongs on the child element"),
        };

        private static readonly Dictionary<string, LhElementMapping> _byElement =
            new Dictionary<string, LhElementMapping>(StringComparer.Ordinal);

        static LhWpfMappings()
        {
            for (int i = 0; i < _elements.Length; i++)
            {
                _byElement[_elements[i].LhName] = _elements[i];
            }
        }

        public static IList<LhElementMapping> Elements
        {
            get { return _elements; }
        }

        public static IList<LhAttributeMapping> Attributes
        {
            get { return _attributes; }
        }

        /// <summary>Looks up an element mapping, or null when the element is not listed.</summary>
        public static LhElementMapping FindElement(string lhName)
        {
            LhElementMapping m;
            return _byElement.TryGetValue(lhName, out m) ? m : null;
        }

        /// <summary>
        /// Attached properties whose bare name is ambiguous until the containing panel is known.
        ///
        /// These are written on the ELEMENT BEING POSITIONED, never on the type that defines them, so
        /// the owner has to be resolved from the parent. "Left" is Canvas.Left inside a Canvas and
        /// nothing at all elsewhere; "Dock" is DockPanel.Dock inside a DockPanel. Reflecting over the
        /// installed assemblies confirms which types define them and that they are genuinely attached,
        /// rather than ordinary dependency properties.
        /// </summary>
        /// <summary>The names that mean "an attached property of the containing panel".</summary>
        private static readonly Dictionary<string, string[]> _panelAttached =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { "Canvas", new string[] { "Left", "Top", "Right", "Bottom" } },
                { "DockPanel", new string[] { "Dock" } },
                { "Grid", new string[] { "Row", "Column", "RowSpan", "ColumnSpan" } },
                { "InkCanvas", new string[] { "Left", "Top", "Right", "Bottom" } },
                { "FixedPage", new string[] { "Left", "Top", "Right", "Bottom" } },
            };

        /// <summary>
        /// Resolves a bare attribute name against the containing element, returning the qualified
        /// attached-property name or null when the name is not an attached property of that panel.
        ///
        /// Called before the ordinary mapping table, because these names have no owner of their own:
        /// a Longhorn document writes Dock or Left on a child and relies on the panel to interpret it.
        /// </summary>
        public static string ResolveAttached(string parentName, string attributeName)
        {
            if (string.IsNullOrEmpty(parentName) || string.IsNullOrEmpty(attributeName)) return null;

            // the parent may be namespaced or a full CLR type name; reduce it to the short form
            string parent = parentName;
            int colon = parent.IndexOf(':');
            if (colon >= 0) parent = parent.Substring(colon + 1);
            int dot = parent.LastIndexOf('.');
            if (dot >= 0) parent = parent.Substring(dot + 1);

            string[] names;
            if (!_panelAttached.TryGetValue(parent, out names)) return null;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], attributeName, StringComparison.Ordinal))
                {
                    return parent + "." + attributeName;
                }
            }
            return null;
        }

        /// <summary>
        /// Looks up an attribute rename for an element, owner-specific first.
        /// </summary>
        public static LhAttributeMapping FindAttribute(string owner, string lhName)
        {
            LhAttributeMapping generic = null;
            for (int i = 0; i < _attributes.Length; i++)
            {
                LhAttributeMapping m = _attributes[i];
                if (!string.Equals(m.LhName, lhName, StringComparison.Ordinal)) continue;
                if (m.Owner == null) { generic = m; continue; }
                if (string.Equals(m.Owner, owner, StringComparison.Ordinal)) return m;
            }
            return generic;
        }

        /// <summary>
        /// True when a namespace URI is written with every slash doubled, which the 2005
        /// generation's <c>XmlnsProperty</c> records do.
        ///
        /// Measured over the corpora rather than assumed. For the 2005 namespace the doubled form
        /// occurs 193 times and the plain form twice, while the 2003 namespace, which appears 110
        /// times, is never doubled. Collapsing each run of slashes to half its length turns the
        /// doubled form into <c>http://schemas.microsoft.com/2005/xaml/</c> exactly -- the form
        /// that also appears un-doubled in this corpus, which is what shows the plain URI is the
        /// intended one and the doubling is an artefact of how this generation wrote the value.
        ///
        /// The correction is applied in the converter rather than the reader. The reader's job is to
        /// report the bytes faithfully, and they really do contain the doubled form; deciding that
        /// one spelling means the other is a judgement about intent, which is this layer's business.
        /// </summary>
        public static bool IsDoubledSlashUri(string value)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf("//", StringComparison.Ordinal) >= 0
                   && value.IndexOf("////", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Rewrites a doubled-slash URI to its plain form by halving each run of slashes.
        /// Returns the input unchanged when it is not doubled.
        /// </summary>
        public static string UndoubleSlashes(string value)
        {
            if (!IsDoubledSlashUri(value)) return value;

            StringBuilder sb = new StringBuilder(value.Length);
            int i = 0;
            while (i < value.Length)
            {
                if (value[i] != '/')
                {
                    sb.Append(value[i]);
                    i++;
                    continue;
                }
                int j = i;
                while (j < value.Length && value[j] == '/') j++;
                int run = j - i;
                int half = run / 2;
                if (half < 1) half = 1;
                sb.Append('/', half);
                i = j;
            }
            return sb.ToString();
        }
    }
}
