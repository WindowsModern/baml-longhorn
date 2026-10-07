using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

using BamlLonghorn;

namespace BamlLonghorn.Gui
{
    /// <summary>
    /// WinForms front end for the decompiler.
    ///
    /// Layout: a folder tree and file list on the left, and on the right a tab per
    /// <see cref="BamlView"/> so a file can be read as XAML, as raw records, as a
    /// tree, as interning tables or as a string-token reconnaissance dump.
    ///
    /// All rendering goes through <see cref="BamlFileView"/>, the same facade the
    /// CLI uses, so the two front ends cannot disagree about what a file contains.
    /// Loading is done on a worker so a large stream never blocks the UI.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly TreeView _folders = new TreeView();
        private readonly ListView _files = new ListView();
        private readonly TabControl _tabs = new TabControl();
        private readonly TextBox _summary = NewText();
        private readonly TextBox _xaml = NewText();
        private readonly TextBox _records = NewText();
        private readonly TextBox _tree = NewText();
        private readonly TextBox _tables = NewText();
        private readonly TextBox _recon = NewText();
        private readonly StatusStrip _status = new StatusStrip();
        private readonly ToolStripStatusLabel _statusText = new ToolStripStatusLabel();
        private readonly ToolStripMenuItem _openFolder = new ToolStripMenuItem("&Open folder...");
        private readonly ToolStripMenuItem _openFile = new ToolStripMenuItem("Open &file...");
        private readonly ToolStripMenuItem _saveXaml = new ToolStripMenuItem("&Save XAML as...");
        private readonly ToolStripMenuItem _copyView = new ToolStripMenuItem("&Copy current view");
        private readonly ToolStripMenuItem _reload = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _exportAll = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _menuLanguage = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _langAuto = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _menuFile = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _menuView = new ToolStripMenuItem();

        /// <summary>Folder tree above the file list; sized in OnLoad, not here.</summary>
        private SplitContainer _left;

        /// <summary>Navigation pane on the left, view tabs on the right.</summary>
        private SplitContainer _outer;

        private TextBox _find;
        private Panel _findBar;
        private Label _findCount;
        private TextBox _filter;
        private Panel _filterBar;
        private Label _filterCount;
        private readonly ToolStripMenuItem _expandBrushes = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _menuExit = new ToolStripMenuItem();
        private bool _expandBrushesOn;
        private Button _findNextButton;
        private Button _findPreviousButton;
        private int _findStart;
        private int _cachedLength = -1;
        private string _cachedLower;
        private string _cachedTextKey;

        private readonly Dictionary<string, BamlFileView> _cache =
            new Dictionary<string, BamlFileView>(StringComparer.OrdinalIgnoreCase);

        private string _currentPath;

        public MainForm(string initialFolder)
        {
            Text = Localization.T(S.AppTitle);
            Width = 1180;
            Height = 760;
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Consolas", 9f);

            BuildMenu();
            BuildLayout();
            BuildTabs();

            _status.Items.Add(_statusText);
            _statusText.Spring = true;
            _statusText.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_status);

            ApplyLanguage();

            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder))
            {
                LoadFolder(initialFolder);
            }
            else
            {
                _statusText.Text = Localization.T(S.StatusReady);
            }
        }

        // ------------------------------------------------------------------
        // construction
        // ------------------------------------------------------------------

        private void BuildMenu()
        {
            MenuStrip menu = new MenuStrip();

            _menuFile.DropDownItems.Add(_openFolder);
            _menuFile.DropDownItems.Add(_openFile);
            _menuFile.DropDownItems.Add(new ToolStripSeparator());
            _menuFile.DropDownItems.Add(_saveXaml);
            _menuFile.DropDownItems.Add(_exportAll);
            _menuFile.DropDownItems.Add(_copyView);
            _menuFile.DropDownItems.Add(new ToolStripSeparator());
            _menuExit.Click += delegate { Close(); };
            _menuFile.DropDownItems.Add(_menuExit);

            _menuView.DropDownItems.Add(_reload);
            _expandBrushes.CheckOnClick = true;
            _expandBrushes.Click += delegate
            {
                _expandBrushesOn = _expandBrushes.Checked;
                if (_currentPath != null)
                {
                    BamlFileView v = GetView(_currentPath);
                    v.ExpandCompoundBrushes = _expandBrushesOn;
                    v.InvalidateViews();
                    ShowFile(_currentPath);
                }
            };
            _menuView.DropDownItems.Add(_expandBrushes);
            _menuView.DropDownItems.Add(new ToolStripSeparator());
            _menuView.DropDownItems.Add(_menuLanguage);

            // one entry per supported tag; the active one is checked
            _langAuto.Click += delegate { Localization.SetLanguage(Localization.AutoTag); };
            _menuLanguage.DropDownItems.Add(_langAuto);
            _menuLanguage.DropDownItems.Add(new ToolStripSeparator());
            PopulateLanguageMenu();

            menu.Items.Add(_menuFile);
            menu.Items.Add(_menuView);
            MainMenuStrip = menu;
            Controls.Add(menu);

            _openFolder.Click += delegate { OnOpenFolder(); };
            _openFile.Click += delegate { OnOpenFile(); };
            _saveXaml.Click += delegate { OnSaveXaml(); };
            _copyView.Click += delegate { OnCopyView(); };
            _reload.Click += delegate { OnReload(); };
            _exportAll.Click += delegate { OnExportAll(); };

            Localization.LanguageChanged += delegate { ApplyLanguage(); };
        }

        /// <summary>Builds the language list from the supported-tag table.</summary>
        private void PopulateLanguageMenu()
        {
            string[] tags = Localization.SupportedLanguages;
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                ToolStripMenuItem item = new ToolStripMenuItem(Localization.DisplayName(tag));
                item.Tag = tag;
                item.CheckOnClick = false;
                // languages with no table yet are offered but marked, so the menu
                // still reflects the supported list without pretending to translate
                if (!Localization.HasTranslation(tag))
                {
                    item.ForeColor = SystemColors.GrayText;
                    item.ToolTipText = "shows English text";
                }
                item.Click += delegate(object sender, EventArgs e)
                {
                    ToolStripMenuItem clicked = (ToolStripMenuItem)sender;
                    Localization.SetLanguage((string)clicked.Tag);
                };
                _menuLanguage.DropDownItems.Add(item);
            }
        }

        /// <summary>Pushes the active language into every caption.</summary>
        private void ApplyLanguage()
        {
            // Mirroring goes first, before any text is assigned, so the layout a RTL
            // language needs is in place when sizes are recomputed. Setting RightToLeft
            // on the form propagates to child controls; RightToLeftLayout also swaps the
            // scrollbar and splitter sides.
            bool rtl = Localization.IsRightToLeft;
            RightToLeft rtlMode = rtl ? RightToLeft.Yes : RightToLeft.No;
            if (RightToLeft != rtlMode)
            {
                RightToLeft = rtlMode;
                RightToLeftLayout = rtl;
            }

            Text = Localization.T(S.AppTitle);
            _menuFile.Text = Localization.T(S.MenuFile);
            _menuView.Text = Localization.T(S.MenuView);
            _menuLanguage.Text = Localization.T(S.MenuLanguage);
            // name the language Windows actually resolved to, so "follow Windows" is
            // not an opaque choice
            _langAuto.Text = Localization.T(S.MenuLanguageAuto)
                             + "  (" + Localization.DisplayName(Localization.SystemTag) + ")";
            _openFolder.Text = Localization.T(S.MenuOpenFolder);
            _openFile.Text = Localization.T(S.MenuOpenFile);
            _saveXaml.Text = Localization.T(S.MenuSaveXaml);
            _copyView.Text = Localization.T(S.MenuCopyView);
            _reload.Text = Localization.T(S.MenuReload);
            _exportAll.Text = Localization.T(S.MenuExportAll);
            _find.Text = Localization.T(S.MenuFind);
            _expandBrushes.Text = Localization.T(S.MenuExpandBrushes);

            _menuExit.Text = Localization.T(S.MenuExit);

            string[] tabs = { "TabSummary", "TabXaml", "TabRecords", "TabTree",
                              "TabTables", "TabRecon" };
            S[] keys = { S.TabSummary, S.TabXaml, S.TabRecords, S.TabTree,
                         S.TabTables, S.TabRecon };
            for (int i = 0; i < keys.Length && i < _tabs.TabPages.Count; i++)
            {
                _tabs.TabPages[i].Text = Localization.T(keys[i]);
            }
            if (tabs.Length == 0 && _tabs.TabPages.Count > 0)
            {
                _tabs.TabPages[0].Text = Localization.T(S.TabSummary);
            }
            // Indexed rather than assumed: a build with fewer columns would otherwise
            // throw here, during ApplyLanguage, which runs from the constructor.
            if (_files.Columns.Count > 0)
            {
                _files.Columns[0].Text = Localization.T(S.ColFile);
            }
            if (_files.Columns.Count > 1)
            {
                _files.Columns[1].Text = Localization.T(S.ColBytes);
            }
            if (_files.Columns.Count > 2)
            {
                _files.Columns[2].Text = Localization.T(S.ColDialect);
            }

            // reflect the selection in the language menu
            string current = Localization.CurrentTag;
            for (int i = 0; i < _menuLanguage.DropDownItems.Count; i++)
            {
                ToolStripMenuItem item = _menuLanguage.DropDownItems[i] as ToolStripMenuItem;
                if (item == null || item == _langAuto)
                {
                    continue;
                }
                item.Checked = string.Equals((string)item.Tag, current,
                    StringComparison.OrdinalIgnoreCase);
            }
            // "follow Windows" owns the tick while no explicit choice is stored
            _langAuto.Checked = Localization.IsFollowingSystem;

            // re-render whatever is on screen under the new captions
            if (_currentPath != null)
            {
                _summary.Text = string.Empty;
                _xaml.Text = string.Empty;
                _records.Text = string.Empty;
                _tree.Text = string.Empty;
                _tables.Text = string.Empty;
                _recon.Text = string.Empty;
                ShowFile(_currentPath);
            }
            else
            {
                _statusText.Text = Localization.T(S.StatusReady);
            }
        }

        private void BuildLayout()
        {
            // None of the sizing properties may be set here. SplitContainer's
            // Panel2MinSize SETTER itself throws
            //     InvalidOperationException: SplitterDistance must be between
            //     Panel1MinSize and Width - Panel2MinSize
            // because it internally applies the new minimum against a Width that is
            // still zero during construction. Panel1MinSize happens to survive, but
            // setting it here would only invite the same trap later. All three are
            // applied in ApplySplitterPositions, which runs from OnLoad.
            _outer = new SplitContainer();
            _outer.Dock = DockStyle.Fill;
            _outer.Orientation = Orientation.Vertical;

            _left = new SplitContainer();
            _left.Dock = DockStyle.Fill;
            _left.Orientation = Orientation.Horizontal;

            _folders.Dock = DockStyle.Fill;
            _folders.HideSelection = false;
            _folders.AfterSelect += delegate { OnFolderSelected(); };

            _files.Dock = DockStyle.Fill;
            _files.View = View.Details;
            _files.FullRowSelect = true;
            _files.HideSelection = false;
            _files.Columns.Add("file", 170);
            _files.Columns.Add("bytes", 60, HorizontalAlignment.Right);
            _files.Columns.Add("dialect", 70);
            _files.SelectedIndexChanged += delegate { OnFileSelected(); };

            _left.Panel1.Controls.Add(_folders);
            _left.Panel2.Controls.Add(_files);

            _outer.Panel1.Controls.Add(_left);
            // Docking order again: the find bar is added first so it takes the top
            // band, and the tabs fill what is left.
            _outer.Panel2.Controls.Add(_filterBar);
            _outer.Panel2.Controls.Add(_findBar);
            _outer.Panel2.Controls.Add(_tabs);

            // Docking order decides the layout: whichever control is added FIRST gets
            // the outermost band. The menu must therefore be added before the
            // splitter, or the splitter's Fill would cover it. Controls.Add appends,
            // so the most recently added control is at the front of the z-order and
            // the menu would end up behind — hence the explicit index fix below.
            Controls.Add(_outer);
            Controls.Add(MainMenuStrip);
            Controls.SetChildIndex(_outer, 0);
            Controls.SetChildIndex(MainMenuStrip, 1);
        }

        /// <summary>
        /// Applies the splitter positions, clamped to what the current size allows.
        ///
        /// SplitterDistance is only legal within
        /// <c>[Panel1MinSize, Width - Panel2MinSize]</c>, and a SplitContainer has no
        /// width until the form has been laid out — so this runs from OnLoad and again
        /// whenever the form is resized. The clamp is what makes a narrow window safe
        /// rather than fatal.
        /// </summary>
        private void ApplySplitterPositions()
        {
            SetSplitter(_outer, 320, 180, 320);
            SetSplitter(_left, 240, 80, 80);
        }

        /// <summary>
        /// Sizes one splitter safely.
        ///
        /// Order matters: the minimums are raised only up to what the current span
        /// actually allows, and the distance is set last. Setting a minimum that the
        /// existing distance violates is exactly what makes SplitContainer throw, so
        /// every value is clamped against the real width first.
        /// </summary>
        private static void SetSplitter(SplitContainer container, int preferred,
                                        int minPanel1, int minPanel2)
        {
            if (container == null)
            {
                return;
            }
            int span = container.Orientation == Orientation.Vertical
                ? container.Width
                : container.Height;
            if (span <= 0)
            {
                return;   // not laid out yet
            }

            // never demand more than the span can hold
            int p1 = minPanel1;
            int p2 = minPanel2;
            if (p1 + p2 > span)
            {
                p1 = Math.Max(0, span / 4);
                p2 = Math.Max(0, span / 4);
            }

            if (container.Panel1MinSize != p1) container.Panel1MinSize = p1;
            if (container.Panel2MinSize != p2) container.Panel2MinSize = p2;

            int low = p1;
            int high = span - p2;
            if (high < low)
            {
                return;
            }
            int value = preferred < low ? low : (preferred > high ? high : preferred);
            if (container.SplitterDistance != value)
            {
                container.SplitterDistance = value;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySplitterPositions();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplySplitterPositions();
        }

        /// <summary>
        /// Re-applies the language once the window is actually on screen.
        ///
        /// Applying it only in the constructor is not enough: any caption that handle
        /// creation or the first layout pass re-derives would be left in the designer's
        /// original English. Re-applying on Shown is cheap and guarantees the visible
        /// language is correct whatever happened during construction -- which is also
        /// what a user reaching for "refresh" expects.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyLanguage();
        }

        /// <summary>
        /// Ctrl+F focuses the search box; F3 and Shift+F3 step through matches.
        /// KeyPreview is what lets the form see these before the focused TextBox.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                _find.Focus();
                _find.SelectAll();
                return true;
            }
            if (keyData == Keys.F3)
            {
                FindNext();
                return true;
            }
            if (keyData == (Keys.Shift | Keys.F3))
            {
                FindPrevious();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BuildTabs()
        {
            // A search bar is added to the same container as the tabs and docked
            // above it, rather than being a field the designer owns. The panes
            // routinely hold thousands of lines -- a 34 KB stream decodes to far more
            // text than fits on screen -- so finding a string is the single most
            // useful thing the GUI can offer over the CLI.
            _findBar = new Panel();
            _findBar.Dock = DockStyle.Top;
            _findBar.Height = 28;
            _findBar.Padding = new Padding(4, 3, 4, 3);

            Button next = new Button();
            next.Text = "▼";
            next.Width = 30;
            next.Dock = DockStyle.Right;
            next.Click += delegate { FindNext(); };
            _findNextButton = next;

            Button previous = new Button();
            previous.Text = "▲";
            previous.Width = 30;
            previous.Dock = DockStyle.Right;
            previous.Click += delegate { FindPrevious(); };
            _findPreviousButton = previous;

            _findCount = new Label();
            _findCount.Dock = DockStyle.Right;
            _findCount.Width = 96;
            _findCount.TextAlign = ContentAlignment.MiddleRight;

            _find = new TextBox();
            _find.Dock = DockStyle.Fill;
            // Enter and Shift+Enter step through matches, as in a text editor
            _find.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;      // do not ding
                    if (e.Shift) { FindPrevious(); } else { FindNext(); }
                }
            };

            _findBar.Controls.Add(_find);
            _findBar.Controls.Add(_findCount);
            _findBar.Controls.Add(next);
            _findBar.Controls.Add(previous);

            // The filter applies to the Records tab, where a single document can
            // produce thousands of lines and only a few kinds matter at a time.
            _filterBar = new Panel();
            _filterBar.Dock = DockStyle.Top;
            _filterBar.Height = 26;
            _filterBar.Padding = new Padding(4, 2, 4, 2);
            _filterBar.Visible = false;

            Button applyFilter = new Button();
            applyFilter.Text = "OK";
            applyFilter.Width = 34;
            applyFilter.Dock = DockStyle.Right;
            applyFilter.Click += delegate { ApplyRecordFilter(); };

            _filterCount = new Label();
            _filterCount.Dock = DockStyle.Right;
            _filterCount.Width = 110;
            _filterCount.TextAlign = ContentAlignment.MiddleRight;

            _filter = new TextBox();
            _filter.Dock = DockStyle.Fill;
            _filter.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    ApplyRecordFilter();
                }
            };

            _filterBar.Controls.Add(_filter);
            _filterBar.Controls.Add(_filterCount);
            _filterBar.Controls.Add(applyFilter);

            _tabs.Dock = DockStyle.Fill;
            AddTab("Summary", _summary);
            AddTab("XAML", _xaml);
            AddTab("Records", _records);
            AddTab("Tree", _tree);
            AddTab("Tables", _tables);
            AddTab("Recon", _recon);
            _tabs.SelectedIndexChanged += delegate
            {
                // the filter only means anything on Records
                if (_filterBar != null)
                {
                    _filterBar.Visible = _tabs.SelectedIndex == 2;
                }
                if (_tabs.SelectedIndex == 2 && _filterCount != null
                    && _filterCount.Text.Length == 0 && _filter.TextLength > 0)
                {
                    ApplyRecordFilter();
                }
                RenderCurrentTab();
            };
        }

        private void AddTab(string title, TextBox box)
        {
            TabPage page = new TabPage(title);
            box.Dock = DockStyle.Fill;
            page.Controls.Add(box);
            _tabs.TabPages.Add(page);
        }

        private static TextBox NewText()
        {
            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = false;
            box.Font = new Font("Consolas", 9f);
            box.BackColor = Color.White;
            box.Dock = DockStyle.Fill;
            // Every box built here holds machine-readable output -- record dumps, XAML,
            // tables -- whose direction is a property of the data, not of the interface
            // language. Without this, selecting Arabic would flip hex offsets and markup
            // to right-to-left and make them unreadable. Pinning it in the one factory
            // keeps that decision in a single place.
            box.RightToLeft = RightToLeft.No;
            return box;
        }

        // ------------------------------------------------------------------
        // folder and file lists
        // ------------------------------------------------------------------

        private void OnOpenFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Localization.T(S.DlgChooseFolderDesc);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadFolder(dialog.SelectedPath);
                }
            }
        }

        private void OnOpenFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Localization.T(S.DlgOpenFileFilter);
                dialog.Title = Localization.T(S.DlgOpenFileTitle);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ShowFile(dialog.FileName);
                }
            }
        }

        /// <summary>Populates the folder tree with every directory holding a .baml.</summary>
        private void LoadFolder(string root)
        {
            _folders.BeginUpdate();
            _folders.Nodes.Clear();
            _cache.Clear();

            TreeNode rootNode = new TreeNode(root);
            rootNode.Tag = root;
            PopulateFolders(rootNode, root);
            _folders.Nodes.Add(rootNode);
            rootNode.Expand();
            _folders.EndUpdate();
            _folders.SelectedNode = rootNode;

            _statusText.Text = root;
        }

        private static bool HasBaml(string dir)
        {
            try
            {
                return Directory.GetFiles(dir, "*.baml").Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasBamlBelow(string dir, int depth)
        {
            if (depth <= 0)
            {
                return false;
            }
            try
            {
                string[] subs = Directory.GetDirectories(dir);
                for (int i = 0; i < subs.Length; i++)
                {
                    if (HasBaml(subs[i]) || HasBamlBelow(subs[i], depth - 1))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        private void PopulateFolders(TreeNode node, string dir)
        {
            string[] subs;
            try
            {
                subs = Directory.GetDirectories(dir);
            }
            catch (Exception)
            {
                return;
            }
            Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < subs.Length; i++)
            {
                if (!HasBaml(subs[i]) && !HasBamlBelow(subs[i], 3))
                {
                    continue;
                }
                TreeNode child = new TreeNode(System.IO.Path.GetFileName(subs[i]));
                child.Tag = subs[i];
                node.Nodes.Add(child);
                PopulateFolders(child, subs[i]);
            }
        }

        private void OnFolderSelected()
        {
            TreeNode node = _folders.SelectedNode;
            if (node == null || !(node.Tag is string))
            {
                return;
            }
            string dir = (string)node.Tag;

            _files.BeginUpdate();
            _files.Items.Clear();
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.baml");
            }
            catch (Exception ex)
            {
                _files.EndUpdate();
                _statusText.Text = Localization.T(S.StatusError, ex.Message);
                return;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                ListViewItem item = new ListViewItem(System.IO.Path.GetFileName(files[i]));
                item.SubItems.Add(new FileInfo(files[i]).Length.ToString(
                    CultureInfo.InvariantCulture));
                item.SubItems.Add("...");
                item.Tag = files[i];
                _files.Items.Add(item);
            }
            _files.EndUpdate();

            // describe each file off the UI thread; detection is cheap but a large
            // folder should still not stall the window
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < files.Length; i++)
                {
                    BamlFileView view = GetView(files[i]);
                    string dialect = view.IsRecognised
                        ? (view.Reader.Dialect.ToString() + " " + view.Confidence + "%")
                        : Localization.T(S.Unrecognised);
                    int index = i;
                    string label = dialect;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (index < _files.Items.Count)
                            {
                                _files.Items[index].SubItems[2].Text = label;
                            }
                        });
                    }
                    catch (Exception)
                    {
                        return;   // the form went away
                    }
                }
            });

            _statusText.Text = Localization.T(S.StatusFolder, dir, files.Length);
        }

        private void OnFileSelected()
        {
            if (_files.SelectedItems.Count == 0)
            {
                return;
            }
            string path = _files.SelectedItems[0].Tag as string;
            if (path != null)
            {
                ShowFile(path);
            }
        }

        // ------------------------------------------------------------------
        // document display
        // ------------------------------------------------------------------

        private BamlFileView GetView(string path)
        {
            BamlFileView view;
            if (!_cache.TryGetValue(path, out view))
            {
                view = BamlFileView.FromFile(path);
                _cache[path] = view;
            }
            return view;
        }

        private void ShowFile(string path)
        {
            _currentPath = path;
            Text = "BamlLonghorn — " + System.IO.Path.GetFileName(path);

            Cursor = Cursors.WaitCursor;
            try
            {
                BamlFileView view = GetView(path);
                // force the decode now so the status line can report it
                BamlDocument document = view.Document;

                for (int i = 0; i < _tabs.TabPages.Count; i++)
                {
                    _tabs.TabPages[i].Text = _tabs.TabPages[i].Text.Split(' ')[0];
                }

                _summary.Text = view.Render(BamlView.Summary);
                _summary.SelectionLength = 0;
                _summary.SelectionStart = 0;

                // clear the other tabs so stale text is never shown
                _xaml.Text = string.Empty;
                _records.Text = string.Empty;
                _tree.Text = string.Empty;
                _tables.Text = string.Empty;
                _recon.Text = string.Empty;
                _tabs.SelectedIndex = 0;

                // a new document invalidates any search position and the find cache
                _findStart = 0;
                _cachedTextKey = null;
                _cachedLower = null;
                _findCount.Text = string.Empty;

                string state;
                if (view.LoadError != null)
                {
                    state = Localization.T(S.StatusLoadError, view.LoadError);
                }
                else if (!view.IsRecognised)
                {
                    state = Localization.T(S.Unrecognised)
                            + (view.Confidence > 0
                               ? " " + Localization.T(S.StatusBestScore, view.Confidence)
                               : string.Empty);
                }
                else
                {
                    int count = document == null ? 0
                        : (document.Entries.Count > 0
                           ? document.Entries.Count : document.Records.Count);
                    state = view.DialectName + "   "
                            + Localization.T(S.StatusRecords, count)
                            + (view.IsCompleteParse
                               ? string.Empty
                               : "   " + Localization.T(S.StatusIncomplete));
                }
                _statusText.Text = Localization.T(S.StatusFile,
                    System.IO.Path.GetFileName(path), view.Length, state);
                UpdateTabTitles();
            }
            catch (Exception ex)
            {
                _statusText.Text = "error: " + ex.Message;
                MessageBox.Show(this, ex.Message, Localization.T(S.DlgErrorTitle),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private static BamlView ViewOfTab(int index)
        {
            switch (index)
            {
                case 1: return BamlView.Xaml;
                case 2: return BamlView.Records;
                case 3: return BamlView.Tree;
                case 4: return BamlView.Tables;
                case 5: return BamlView.Recon;
                default: return BamlView.Summary;
            }
        }

        private TextBox BoxOfTab(int index)
        {
            switch (index)
            {
                case 1: return _xaml;
                case 2: return _records;
                case 3: return _tree;
                case 4: return _tables;
                case 5: return _recon;
                default: return _summary;
            }
        }

        /// <summary>Renders the selected tab on first visit; results are cached.</summary>
        private void RenderCurrentTab()
        {
            if (_currentPath == null)
            {
                return;
            }
            int index = _tabs.SelectedIndex;
            if (index == 0)
            {
                return;   // summary is filled by ShowFile
            }
            TextBox box = BoxOfTab(index);
            if (box.Text.Length > 0)
            {
                return;
            }
            _findStart = 0;
            _cachedTextKey = null;
            _cachedLower = null;
            _findCount.Text = string.Empty;

            Cursor = Cursors.WaitCursor;
            try
            {
                BamlFileView view = GetView(_currentPath);
                box.Text = view.Render(ViewOfTab(index));
                box.SelectionStart = 0;
                box.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                box.Text = "error: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>Re-applies the localised tab captions.</summary>
        private void UpdateTabTitles()
        {
            S[] keys = { S.TabSummary, S.TabXaml, S.TabRecords, S.TabTree,
                         S.TabTables, S.TabRecon };
            for (int i = 0; i < _tabs.TabPages.Count && i < keys.Length; i++)
            {
                _tabs.TabPages[i].Text = Localization.T(keys[i]);
            }
        }

        // ------------------------------------------------------------------
        // commands
        // ------------------------------------------------------------------

        private void OnReload()
        {
            if (_currentPath == null)
            {
                return;
            }
            _cache.Remove(_currentPath);
            ShowFile(_currentPath);
        }

        /// <summary>
        /// Decompiles every .baml under the selected tree into the chosen folder,
        /// mirroring the source layout and writing one .xaml per input.
        ///
        /// Files the reader refuses are counted as skipped rather than written as
        /// empty output, so an export of a mixed tree stays honest about what it did.
        /// </summary>
        private void OnExportAll()
        {
            string root = null;
            if (_folders.SelectedNode != null && _folders.SelectedNode.Tag is string)
            {
                root = (string)_folders.SelectedNode.Tag;
            }
            if (root == null || !Directory.Exists(root))
            {
                MessageBox.Show(this, Localization.T(S.StatusReady),
                    Localization.T(S.DlgErrorTitle),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Localization.T(S.DlgExportAllTitle);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                string outRoot = dialog.SelectedPath;

                List<string> files;
                try
                {
                    files = new List<string>(Directory.GetFiles(root, "*.baml",
                        SearchOption.AllDirectories));
                }
                catch (Exception ex)
                {
                    _statusText.Text = Localization.T(S.StatusError, ex.Message);
                    return;
                }

                int written = 0;
                int skipped = 0;
                Cursor = Cursors.WaitCursor;
                try
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        BamlFileView view = GetView(files[i]);
                        if (!view.IsRecognised || view.Document == null
                            || view.Document.Entries.Count == 0
                            && view.Document.Records.Count == 0)
                        {
                            skipped++;
                            continue;
                        }

                        string text = view.Render(BamlView.Xaml);
                        string relative = files[i].Substring(root.Length)
                            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string target = Path.Combine(outRoot,
                            Path.ChangeExtension(relative, ".xaml"));
                        string parent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }
                        File.WriteAllText(target, text, new UTF8Encoding(false));
                        written++;
                    }
                }
                catch (Exception ex)
                {
                    _statusText.Text = Localization.T(S.StatusError, ex.Message);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                _statusText.Text = Localization.T(S.StatusExportAllDone, written, skipped);
            }
        }

        // ------------------------------------------------------------------
        // record filter
        // ------------------------------------------------------------------

        /// <summary>
        /// Hides every record line that does not contain the filter text.
        ///
        /// A single 34 KB document yields thousands of records, and reading them means
        /// wanting one kind at a time -- every TypeInfo, or every AttributeInfo named
        /// Width. Blank text restores the full dump, so the filter is never
        /// destructive.
        /// </summary>
        private void ApplyRecordFilter()
        {
            if (_currentPath == null)
            {
                return;
            }
            BamlFileView view = GetView(_currentPath);
            string full = view.Render(BamlView.Records);
            string term = _filter.Text.Trim();

            if (term.Length == 0)
            {
                _records.Text = full;
                _filterCount.Text = string.Empty;
                return;
            }

            string[] lines = full.Replace("\r\n", "\n").Split('\n');
            System.Text.StringBuilder kept = new System.Text.StringBuilder();
            int matched = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                kept.AppendLine(lines[i]);
                matched++;

                // a record's follow-on lines belong with it, but only when they are
                // indented continuations rather than the next record
                while (i + 1 < lines.Length && lines[i + 1].StartsWith("  ", StringComparison.Ordinal)
                       && lines[i + 1].TrimStart().StartsWith("@", StringComparison.Ordinal) == false)
                {
                    i++;
                    kept.AppendLine(lines[i]);
                }
            }

            _records.Text = kept.ToString();
            _filterCount.Text = Localization.T(S.StatusFiltered, matched, lines.Length - 1);
            _records.SelectionStart = 0;
            _records.SelectionLength = 0;

            // the find cache is keyed on text length, so it must be dropped
            _cachedTextKey = null;
            _cachedLower = null;
        }

        // ------------------------------------------------------------------
        // find
        // ------------------------------------------------------------------
        /// <summary>
        /// The active pane's text, lower-cased and cached.
        ///
        /// The cache is keyed on the control's current text length and the tab index,
        /// which is enough to notice a tab switch or a re-render without hashing
        /// tens of thousands of characters on every keystroke.
        /// </summary>
        private string LoweredForFind(TextBox box)
        {
            string key = _tabs.SelectedIndex.ToString(CultureInfo.InvariantCulture)
                         + ":" + box.Text.Length.ToString(CultureInfo.InvariantCulture);
            if (_cachedLower == null || _cachedTextKey != key)
            {
                _cachedLower = box.Text.ToLowerInvariant();
                _cachedTextKey = key;
                _cachedLength = box.Text.Length;
            }
            return _cachedLower;
        }

        private void FindNext()
        {
            Search(true);
        }

        private void FindPrevious()
        {
            Search(false);
        }

        private void Search(bool forward)
        {
            TextBox box = BoxOfTab(_tabs.SelectedIndex);
            if (box.Text.Length == 0)
            {
                RenderCurrentTab();
            }
            string needle = _find.Text;
            if (needle.Length == 0)
            {
                _findCount.Text = string.Empty;
                return;
            }

            string haystack = LoweredForFind(box);
            string lower = needle.ToLowerInvariant();
            int index;

            if (forward)
            {
                index = haystack.IndexOf(lower, Math.Min(_findStart, haystack.Length),
                                         StringComparison.Ordinal);
                if (index < 0)
                {
                    index = haystack.IndexOf(lower, 0, StringComparison.Ordinal);   // wrap
                }
            }
            else
            {
                int from = Math.Min(_findStart, haystack.Length) - 1;
                if (from < 0)
                {
                    from = 0;
                }
                index = from + lower.Length <= haystack.Length
                    ? haystack.LastIndexOf(lower, from, StringComparison.Ordinal)
                    : haystack.LastIndexOf(lower, StringComparison.Ordinal);
                if (index < 0)
                {
                    index = haystack.LastIndexOf(lower, StringComparison.Ordinal);  // wrap
                }
            }

            if (index < 0)
            {
                _findCount.Text = "0/0";
                _findStart = 0;
                return;
            }

            box.Focus();
            box.Select(index, needle.Length);
            box.ScrollToCaret();
            _findStart = forward ? index + needle.Length : index;

            int total = CountOccurrences(haystack, lower);
            int ordinal = CountOccurrences(haystack.Substring(0, index), lower) + 1;
            _findCount.Text = ordinal.ToString(CultureInfo.InvariantCulture) + "/"
                              + total.ToString(CultureInfo.InvariantCulture);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (needle.Length == 0)
            {
                return 0;
            }
            int count = 0;
            int at = 0;
            while (true)
            {
                at = haystack.IndexOf(needle, at, StringComparison.Ordinal);
                if (at < 0)
                {
                    return count;
                }
                count++;
                at += needle.Length;
            }
        }

        private void OnSaveXaml()
        {
            if (_currentPath == null)
            {
                return;
            }
            BamlFileView view = GetView(_currentPath);
            string text = view.Render(BamlView.Xaml);
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = Localization.T(S.DlgSaveXamlFilter);
                dialog.FileName = System.IO.Path.GetFileNameWithoutExtension(_currentPath)
                                  + ".xaml";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
                        _statusText.Text = Localization.T(S.StatusSaved, dialog.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, Localization.T(S.DlgErrorTitle),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OnCopyView()
        {
            TextBox box = BoxOfTab(_tabs.SelectedIndex);
            if (box.Text.Length == 0)
            {
                RenderCurrentTab();
            }
            if (box.Text.Length > 0)
            {
                try
                {
                    Clipboard.SetText(box.Text);
                    _statusText.Text = Localization.T(S.StatusCopied, box.Text.Length);
                }
                catch (Exception ex)
                {
                    _statusText.Text = Localization.T(S.StatusCopyFailed, ex.Message);
                }
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // DPI awareness comes from app.manifest: a manifest is the only mechanism
            // available on .NET Framework (Application.SetHighDpiMode is a .NET Core
            // 3.0+ API and does not exist here). Application.EnableVisualStyles below
            // renders the themed controls that the manifest's Common-Controls 6.0
            // dependency makes available.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string initial = args.Length > 0 ? args[0] : null;
            if (initial != null && File.Exists(initial))
            {
                initial = System.IO.Path.GetDirectoryName(
                    System.IO.Path.GetFullPath(initial));
            }
            Application.Run(new MainForm(initial));
        }
    }
}
