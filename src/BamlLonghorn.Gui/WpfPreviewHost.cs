using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

// The WPF namespaces are deliberately not imported wholesale: WinForms and WPF both define
// UserControl, Color and others, and importing both makes every such use ambiguous. The WPF types
// are written fully qualified instead, which also makes it obvious at a glance which framework a
// given line belongs to -- worth the verbosity in the one file that bridges the two.
using WpfBorder = System.Windows.Controls.Border;
using WpfGrid = System.Windows.Controls.Grid;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextWrapping = System.Windows.TextWrapping;
using WpfVisibility = System.Windows.Visibility;
using WpfThickness = System.Windows.Thickness;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfColor = System.Windows.Media.Color;
using WpfScaleTransform = System.Windows.Media.ScaleTransform;

namespace BamlLonghorn.Gui
{
    /// <summary>
    /// Renders converted markup with WPF itself, inside a WinForms host.
    ///
    /// Hosting matters here. The point of the preview is to show what WPF would build from the
    /// converted document, so the markup has to be handed to a real XAML parser and the resulting
    /// element has to be drawn by WPF. A WinForms control cannot draw a FrameworkElement, and an
    /// earlier attempt in this repository tried to and produced nothing; <see cref="ElementHost"/>
    /// is the supported bridge and leaves the layout and drawing to the framework that will
    /// eventually own the markup.
    ///
    /// Failures are the common case, not the exception. Converted Longhorn documents keep their
    /// un-convertible elements under an <c>lh</c> prefix and frequently carry a default namespace
    /// that is not a WPF one, so most will not parse. The control therefore reports the failure
    /// with the exception type, the parser's line and position, and the innermost message, and
    /// keeps the original markup available beside it. A preview that rendered nothing and said
    /// nothing would be indistinguishable from an empty document.
    /// </summary>
    public sealed class WpfPreviewHost : System.Windows.Forms.UserControl
    {
        private readonly ElementHost _host = new ElementHost();
        private readonly WpfBorder _frame = new WpfBorder();
        private readonly WpfScrollViewer _scroll = new WpfScrollViewer();
        private readonly WpfTextBlock _failure = new WpfTextBlock();
        private readonly WpfGrid _root = new WpfGrid();

        private System.Windows.FrameworkElement _element;
        private string _markup = string.Empty;

        public WpfPreviewHost()
        {
            _host.Dock = DockStyle.Fill;
            _host.Child = _root;
            Controls.Add(_host);

            // A ScrollViewer gives panning and scrolling, and a scale transform gives zoom; the
            // two together mean neither has to be reimplemented.
            _scroll.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            _scroll.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            _scroll.Content = _frame;

            _failure.Margin = new WpfThickness(12);
            _failure.TextWrapping = WpfTextWrapping.Wrap;
            _failure.Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(160, 20, 20));
            _failure.FontFamily = new WpfFontFamily("Consolas");
            _failure.Visibility = WpfVisibility.Collapsed;

            _root.Children.Add(_scroll);
            _root.Children.Add(_failure);
        }

        /// <summary>Background of the rendered document, so contrast can be judged.</summary>
        public Color CanvasColor
        {
            get
            {
                var c = (_frame.Background as WpfSolidColorBrush);
                if (c == null) return System.Drawing.Color.White;
                return System.Drawing.Color.FromArgb(c.Color.A, c.Color.R, c.Color.G, c.Color.B);
            }
            set
            {
                var c = WpfColor.FromArgb(value.A, value.R, value.G, value.B);
                _frame.Background = new WpfSolidColorBrush(c);
            }
        }

        /// <summary>The current zoom factor applied to the rendered document.</summary>
        public double Zoom { get; private set; }

        public WpfPreviewHost ZoomTo(double zoom) { Zoom = zoom; ApplyZoom(); return this; }
        public void ZoomIn() { ZoomTo(Zoom * 1.15); }
        public void ZoomOut() { ZoomTo(Zoom / 1.15); }
        public void ResetView() { ZoomTo(1.0); _scroll.ScrollToHome(); }

        /// <summary>True when the last load produced a drawable element.</summary>
        public bool HasContent
        {
            get { return _element != null; }
        }

        /// <summary>The failure text from the last load, or null.</summary>
        public string FailureText { get; private set; }

        /// <summary>
        /// Parses the markup and shows the result, or shows why it could not be shown.
        ///
        /// Every exception is caught and reported. The parser raises different types for different
        /// faults -- XamlParseException for a malformed document, and others for a missing assembly
        /// or a bad type reference -- and all of them are worth showing, so the type name is
        /// included rather than assuming one exception shape.
        /// </summary>
        public void Load(string markup)
        {
            _markup = markup ?? string.Empty;
            _element = null;
            FailureText = null;

            if (_markup.Length == 0)
            {
                ShowFailure("no markup to render", null);
                return;
            }

            try
            {
                object parsed = System.Windows.Markup.XamlReader.Parse(_markup);

                var element = parsed as System.Windows.FrameworkElement;
                if (element == null)
                {
                    ShowFailure("the document parsed, but its root is "
                                + parsed.GetType().FullName
                                + ", which is not a FrameworkElement and cannot be drawn.", null);
                    return;
                }

                element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                element.Arrange(new System.Windows.Rect(element.DesiredSize));
                element.UpdateLayout();

                _element = element;
                _frame.Child = element;
                _scroll.Visibility = WpfVisibility.Visible;
                _failure.Visibility = WpfVisibility.Collapsed;
                ZoomTo(1.0);
            }
            catch (System.Windows.Markup.XamlParseException ex)
            {
                ShowFailure("XAML parse failed at line " + ex.LineNumber + ", position "
                            + ex.LinePosition + ": " + Innermost(ex), ex);
            }
            catch (Exception ex)
            {
                ShowFailure(ex.GetType().Name + ": " + Innermost(ex), ex);
            }
        }

        /// <summary>
        /// Shows the innermost message of an exception chain.
        ///
        /// The useful sentence is usually at the bottom. A XAML parse failure surfaces as an outer
        /// exception whose message names the operation and an inner one that names the actual
        /// problem, such as a type that could not be resolved.
        /// </summary>
        private static string Innermost(Exception ex)
        {
            Exception e = ex;
            int guard = 0;
            while (e.InnerException != null && guard++ < 8) e = e.InnerException;
            string m = e.Message ?? string.Empty;
            int nl = m.IndexOfAny(new char[] { '\r', '\n' });
            return nl < 0 ? m : m.Substring(0, nl);
        }

        private void ShowFailure(string message, Exception detail)
        {
            FailureText = message;
            _frame.Child = null;
            _scroll.Visibility = WpfVisibility.Collapsed;

            var sb = new System.Text.StringBuilder();
            sb.Append("This document cannot be rendered by WPF.").Append('\n').Append('\n');
            sb.Append(message);
            if (detail != null)
            {
                sb.Append('\n').Append('\n').Append("exception: ").Append(detail.GetType().FullName);
            }
            sb.Append('\n').Append('\n');
            sb.Append("This is expected for most Longhorn documents: elements with no WPF ")
              .Append("equivalent are kept under an lh prefix, and the document's own default ")
              .Append("namespace is frequently not a WPF one. The markup itself is on the ")
              .Append("WPF XAML tab, and the conversion report there lists what did not convert.");

            _failure.Text = sb.ToString();
            _failure.Visibility = WpfVisibility.Visible;
        }

        private void ApplyZoom()
        {
            if (_element == null) return;
            var transform = new WpfScaleTransform(Zoom, Zoom);
            _element.LayoutTransform = transform;
            _element.UpdateLayout();
        }
    }
}
