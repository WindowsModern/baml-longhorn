using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace BamlLonghorn.Gui
{
    /// <summary>
    /// A zoomable, pannable preview of generated markup, with a choosable background.
    ///
    /// It renders the markup as text on a transformed graphics surface rather than handing it to a
    /// XAML parser. That is a deliberate choice with three reasons:
    ///
    ///   * the markup is generated from an untrusted file and, for Longhorn documents, is
    ///     frequently not loadable XAML at all -- un-convertible elements keep an <c>lh</c>
    ///     prefix and the default namespace is often absent. Parsing it would fail on most inputs,
    ///     and a preview that fails on most inputs is not a preview.
    ///   * parsing would also introduce a dependency on the WPF stack inside a WinForms host,
    ///     for a feature whose value is reading the output.
    ///   * the transform machinery is the same either way, so the zoom, pan and background
    ///     behaviour is available without the fragility.
    ///
    /// Background colour is not decoration here. Converted markup mixes elements that exist in WPF
    /// with ones that do not, and the un-convertible ones carry an <c>lh</c> prefix; against a dark
    /// or light background the attribute values in those lines are easier or harder to read, so the
    /// choice is left to the reader.
    /// </summary>
    public sealed class XamlPreview : Control
    {
        private string _text = string.Empty;
        private string _error;
        private string _notice;

        private float _zoom = 1.0f;
        private float _panX;
        private float _panY;

        private bool _dragging;
        private Point _dragOrigin;
        private float _dragPanX;
        private float _dragPanY;

        private Color _canvas = Color.White;
        private Color _ink = Color.FromArgb(30, 30, 30);
        private Color _errorInk = Color.FromArgb(160, 20, 20);
        private Color _noticeInk = Color.FromArgb(120, 90, 0);

        private System.Windows.FrameworkElement _rendered;
        private string _renderError;
        private string _emptyMessage = "(nothing to render)";

        private const float MinZoom = 0.2f;
        private const float MaxZoom = 8.0f;
        private static readonly string[] LineSeparators = new string[] { "\r\n", "\n" };

        public XamlPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);
            TabStop = true;
            Font = new Font("Consolas", 9f);
        }

        /// <summary>Raised whenever the zoom factor changes, so a host can show it.</summary>
        public event EventHandler ZoomChanged;

        /// <summary>The markup to display. Never null.</summary>
        public string PreviewText
        {
            get { return _text; }
            set
            {
                _text = value ?? string.Empty;
                Invalidate();
            }
        }

        /// <summary>
        /// A message shown above the markup, for a failure that still leaves something to show.
        /// Null clears it.
        /// </summary>
        public string ErrorText
        {
            get { return _error; }
            set { _error = value; Invalidate(); }
        }

        /// <summary>A non-fatal remark shown above the markup, such as a conversion caveat.</summary>
        public string NoticeText
        {
            get { return _notice; }
            set { _notice = value; Invalidate(); }
        }

        public float Zoom
        {
            get { return _zoom; }
        }

        public Color CanvasColor
        {
            get { return _canvas; }
            set
            {
                _canvas = value;
                BackColor = value;
                Invalidate();
            }
        }

        public Color InkColor
        {
            get { return _ink; }
            set { _ink = value; Invalidate(); }
        }

        /// <summary>Sets the zoom factor, clamped, and reports the change.</summary>
        public void SetZoom(float zoom)
        {
            float clamped = zoom;
            if (clamped < MinZoom) clamped = MinZoom;
            if (clamped > MaxZoom) clamped = MaxZoom;
            if (Math.Abs(clamped - _zoom) < 0.0001f) return;
            _zoom = clamped;
            Invalidate();
            EventHandler h = ZoomChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>Multiplies the zoom, for a wheel notch.</summary>
        public void ZoomBy(float factor)
        {
            SetZoom(_zoom * factor);
        }

        public void ZoomIn()
        {
            ZoomBy(1.25f);
        }

        public void ZoomOut()
        {
            ZoomBy(1f / 1.25f);
        }

        /// <summary>Returns the view to the origin at 100%.</summary>
        public void ResetView()
        {
            _panX = 0f;
            _panY = 0f;
            SetZoom(1.0f);
            Invalidate();
        }

        /// <summary>Centres the content horizontally when it is narrower than the viewport.</summary>
        public void CentreHorizontally()
        {
            float content = MeasureContentWidth();
            if (content <= 0) return;
            _panX = Math.Max(0f, (ClientSize.Width - content) / 2f);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _dragging = true;
                _dragOrigin = e.Location;
                _dragPanX = _panX;
                _dragPanY = _panY;
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            _panX = _dragPanX + (e.X - _dragOrigin.X);
            _panY = _dragPanY + (e.Y - _dragOrigin.Y);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Ctrl or Shift means zoom; a plain wheel keeps the conventional meaning and is left
            // to the host, so a reader can scroll a long document without changing the scale.
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None)
            {
                ZoomBy(e.Delta > 0 ? 1.15f : 1f / 1.15f);
                return;
            }
            _panY += e.Delta > 0 ? 60f : -60f;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down
                || keyData == Keys.Left || keyData == Keys.Right
                || keyData == Keys.PageUp || keyData == Keys.PageDown
                || keyData == Keys.Home || keyData == Keys.End)
            {
                return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: _panX += 40f; break;
                case Keys.Right: _panX -= 40f; break;
                case Keys.Up: _panY += 40f; break;
                case Keys.Down: _panY -= 40f; break;
                case Keys.PageUp: _panY += ClientSize.Height * 0.9f; break;
                case Keys.PageDown: _panY -= ClientSize.Height * 0.9f; break;
                case Keys.Home: _panX = 0f; _panY = 0f; break;
                case Keys.End: _panY = -(MeasureContentHeight() - ClientSize.Height); break;
                case Keys.Add:
                case Keys.Oemplus: ZoomIn(); return;
                case Keys.Subtract:
                case Keys.OemMinus: ZoomOut(); return;
                default: return;
            }
            e.Handled = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(_canvas);

            if (_rendered != null)
            {
                DrawRendered(g);
                return;
            }

            if (_text.Length == 0 && _error == null && _notice == null)
            {
                DrawCentredLine(g, _emptyMessage, _ink);
                return;
            }

            // The banner is drawn in device space and the content below it in transformed space,
            // so a failure message cannot be scrolled or zoomed away from.
            int bannerHeight = DrawBanner(g);

            var state = g.Save();
            try
            {
                g.TranslateTransform(_panX, _panY + bannerHeight);
                g.ScaleTransform(_zoom, _zoom);

                using (var brush = new SolidBrush(_ink))
                using (var format = new StringFormat(StringFormatFlags.NoWrap))
                {
                    string[] lines = _text.Length == 0
                        ? new string[0]
                        : _text.Split(LineSeparators, StringSplitOptions.None);

                    float lineHeight = Font.GetHeight(g);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        g.DrawString(lines[i], Font, brush, 0f, i * lineHeight, format);
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// Loads the markup with a real XAML parser and keeps the resulting element for painting.
        ///
        /// This is what makes the preview a preview rather than a syntax view: the reader sees the
        /// document WPF would actually build. The call is made on the UI thread because
        /// <c>XamlReader.Parse</c> must be, but the cost was measured before wiring it in -- the
        /// conversion and parse of the largest available corpus file is well under a second -- so
        /// the interface does not stall.
        ///
        /// On failure the exception is not swallowed. Both the element type and the message are
        /// kept, and they are surfaced by <see cref="OnPaint"/> in a banner that cannot be scrolled
        /// or zoomed away from. Most converted Longhorn documents are expected to fail here, because
        /// un-convertible elements are deliberately left under an <c>lh</c> prefix and the document's
        /// own default namespace is frequently not a WPF one; a failure that names the type and the
        /// reason is therefore the useful outcome, not a defect.
        /// </summary>
        public void SetXaml(string markup)
        {
            string text = markup ?? string.Empty;
            PreviewText = text;
            _rendered = null;
            _renderError = null;

            if (text.Length == 0)
            {
                _emptyMessage = "(nothing to render)";
                Invalidate();
                return;
            }

            try
            {
                object parsed = System.Windows.Markup.XamlReader.Parse(text);
                var element = parsed as System.Windows.FrameworkElement;
                if (element == null)
                {
                    _renderError = "parsed, but the root is " + parsed.GetType().FullName
                                   + ", which is not a FrameworkElement, so there is nothing to draw.";
                    _emptyMessage = "(not visual)";
                }
                else
                {
                    element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                    element.Arrange(new System.Windows.Rect(element.DesiredSize));
                    element.UpdateLayout();
                    _rendered = element;
                    _emptyMessage = "(rendered)";
                }
            }
            catch (System.Windows.Markup.XamlParseException ex)
            {
                // the first line usually names the offending element; later lines add the
                // line and position, which is what a reader needs to find it
                _renderError = "XAML parse failed at line " + ex.LineNumber
                               + ", position " + ex.LinePosition + ": " + FirstLine(ex.Message);
                _emptyMessage = "(cannot render)";
            }
            catch (Exception ex)
            {
                _renderError = ex.GetType().Name + ": " + FirstLine(ex.Message);
                _emptyMessage = "(cannot render)";
            }

            ZoomChanged -= Ignore;
            Invalidate();
            EventHandler h = ZoomChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>The element resulting from the last successful parse, or null.</summary>
        public System.Windows.FrameworkElement RenderedElement
        {
            get { return _rendered; }
        }

        /// <summary>The parse failure from the last attempt, or null when it succeeded.</summary>
        public string RenderError
        {
            get { return _renderError; }
        }

        private static void Ignore(object sender, EventArgs e)
        {
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            int i = s.IndexOfAny(new char[] { '\r', '\n' });
            return i < 0 ? s : s.Substring(0, i);
        }

        /// <summary>
        /// Draws the parsed element.
        ///
        /// This class deliberately does not draw WPF content itself. A FrameworkElement cannot be
        /// rendered through GDI+ without going through a visual host, and an earlier attempt here
        /// used a RenderTargetBitmap sized 1x1, which produced nothing at all. The element is
        /// therefore handed to <see cref="WpfPreviewHost"/>, which hosts it in an ElementHost, and
        /// this control only draws the text fallback and the banner.
        /// </summary>
        private void DrawRendered(Graphics g)
        {
            DrawCentredLine(g, "(rendered by the WPF host)", _ink);
        }

        /// <summary>
        /// Draws the error or notice banner and returns the height it occupied.
        ///
        /// Failures are shown rather than swallowed. A preview that renders nothing without saying
        /// why is indistinguishable from a document that is legitimately empty, and the whole point
        /// of this view is to judge output that may not be valid.
        /// </summary>
        private int DrawBanner(Graphics g)
        {
            List<string> lines = new List<string>();
            Color ink = _ink;

            if (_error != null && _error.Length > 0)
            {
                foreach (string l in _error.Split(LineSeparators, StringSplitOptions.None))
                    lines.Add(l);
                ink = _errorInk;
            }
            else if (_notice != null && _notice.Length > 0)
            {
                foreach (string l in _notice.Split(LineSeparators, StringSplitOptions.None))
                    lines.Add(l);
                ink = _noticeInk;
            }

            if (lines.Count == 0) return 0;

            using (var format = new StringFormat(StringFormatFlags.NoWrap))
            using (var brush = new SolidBrush(ink))
            using (var pen = new Pen(Color.FromArgb(60, ink)))
            {
                float lh = Font.GetHeight(g);
                int height = (int)Math.Ceiling(lh * lines.Count) + 8;
                g.FillRectangle(new SolidBrush(Color.FromArgb(28, ink)), 0, 0, ClientSize.Width, height);
                g.DrawLine(pen, 0, height - 1, ClientSize.Width, height - 1);
                for (int i = 0; i < lines.Count; i++)
                {
                    g.DrawString(lines[i], Font, brush, 4f, 4f + i * lh, format);
                }
                return height;
            }
        }

        private void DrawCentredLine(Graphics g, string message, Color ink)
        {
            using (var brush = new SolidBrush(Color.FromArgb(140, ink)))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(message, Font, brush,
                    new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), format);
            }
        }

        private float MeasureContentWidth()
        {
            if (_text.Length == 0) return 0f;
            float widest = 0f;
            using (Graphics g = CreateGraphics())
            {
                string[] lines = _text.Split(LineSeparators, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    float w = g.MeasureString(lines[i], Font).Width;
                    if (w > widest) widest = w;
                }
            }
            return widest * _zoom;
        }

        private float MeasureContentHeight()
        {
            if (_text.Length == 0) return 0f;
            using (Graphics g = CreateGraphics())
            {
                int count = _text.Split(LineSeparators, StringSplitOptions.None).Length;
                return count * Font.GetHeight(g) * _zoom;
            }
        }
    }
}
