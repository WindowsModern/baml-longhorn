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
            new LhElementMapping("Text", "Run", LhMapKind.Substituted,
                "Longhorn's inline text run maps to Run"),
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

            // Binding: Longhorn's Path is a plain string; WPF's is a PropertyPath. The markup
            // text is the same, so only the element name changes.
            new LhAttributeMapping("System.Windows.Data.Bind", "Path", "Path", null),

            // Dock: Longhorn attaches it to any child of a DockPanel; WPF needs the attached
            // property form whenever the attribute is not on a DockPanel itself.
            new LhAttributeMapping("System.Windows.Controls.DockPanel", "Dock", "DockPanel.Dock", null),
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

        /// <summary>Looks up an attribute rename for an element, owner-specific first.</summary>
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
    }
}
