using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MdPad
{
    /// <summary>
    /// mdpad —— 记事本式的 Markdown 阅读/编辑器：左边改，右边即时渲染。
    /// 单个 exe、零依赖（.NET Framework 4.x + WebBrowser 控件），不联网、常驻后台。
    /// </summary>
    internal sealed class MainForm : Form
    {
        // ---------------- 控件
        private readonly TextBox editor = new TextBox();
        private readonly WebBrowser preview = new WebBrowser();
        private readonly SplitContainer split = new SplitContainer();
        private readonly Timer debounce = new Timer();
        private readonly Timer previewFallback = new Timer();
        private readonly StatusStrip statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel stFile = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel stEnc = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel stPos = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel stLen = new ToolStripStatusLabel();
        private readonly ToolStripStatusLabel stMode = new ToolStripStatusLabel();
        private Panel findBar;
        private TextBox findBox;
        private TextBox replBox;
        private CheckBox findCase;
        private ToolStripMenuItem miWrap;
        private ToolStripMenuItem miStatus;
        private ToolStripMenuItem miHardBreak;
        private ToolStripMenuItem miRecent;
        private ToolStripMenuItem miEditOnly;
        private ToolStripMenuItem miPreviewOnly;
        private ToolStripMenuItem miBoth;
        private Panel editorHost;
        private ToolStrip toolbar;
        private MenuStrip menuStrip;
        private ToolStripMenuItem miThemeFollow;
        private ToolStripMenuItem miThemeLight;
        private ToolStripMenuItem miThemeDark;
        private int themeMode;              // 0 跟随系统 / 1 浅色 / 2 深色
        private bool isDarkTheme;

        // ---------------- 状态
        private string currentPath;
        private Encoding fileEncoding = new UTF8Encoding(false);
        private string newline = "\r\n";
        private bool dirty;
        private bool previewReady;
        private bool previewInitializing;
        private bool loading;
        private int fontPercent = 100;
        private bool darkPreview;
        private bool hardBreak = true;
        private int viewMode;                       // 0 编辑+预览 / 1 仅编辑 / 2 仅预览
        private readonly List<string> recent = new List<string>();
        private FileSystemWatcher watcher;
        private DateTime lastSaveUtc = DateTime.MinValue;
        private readonly string configPath;

        public MainForm(string[] args)
        {
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "mdpad\\config.ini");
            LoadConfig();

            Text = "mdpad";
            MinimumSize = new Size(560, 340);
            Width = 1180;
            Height = 760;
            if (widthConfig > 400) Width = widthConfig;
            if (heightConfig > 300) Height = heightConfig;
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            KeyPreview = true;

            BuildUi();
            ApplyFont();
            ApplyTheme();
            ApplyViewMode();
            UpdateStatus();
            HandleCreated += delegate { ApplyDwm(); };

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            editor.DragEnter += OnDragEnter;
            editor.DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;
            Shown += delegate
            {
                if (args != null && args.Length > 0 && File.Exists(args[0])) OpenFile(args[0], true);
                else editor.Focus();
            };
        }

        // ================================================================ 界面

        private void BuildUi()
        {
            // ---- 编辑区（外面套一层带内边距的面板，视觉上不贴边）
            editorHost = new Panel();
            editorHost.Dock = DockStyle.Fill;
            editorHost.Padding = new Padding(14, 10, 8, 10);
            editorHost.Controls.Add(editor);

            editor.Multiline = true;
            editor.Dock = DockStyle.Fill;
            editor.BorderStyle = BorderStyle.None;
            editor.ScrollBars = ScrollBars.Both;
            editor.WordWrap = true;
            editor.AcceptsReturn = true;
            editor.AcceptsTab = true;
            editor.HideSelection = false;
            editor.AllowDrop = true;
            editor.TextChanged += OnEditorTextChanged;
            editor.KeyDown += OnEditorKeyDown;
            editor.KeyUp += OnSelectionChangedLike;
            editor.MouseUp += OnSelectionChangedLike;

            // ---- 预览区
            preview.Dock = DockStyle.Fill;
            preview.ScriptErrorsSuppressed = true;
            preview.AllowWebBrowserDrop = false;
            preview.IsWebBrowserContextMenuEnabled = true;
            preview.WebBrowserShortcutsEnabled = true;
            preview.DocumentCompleted += OnPreviewDocumentCompleted;
            preview.Navigating += OnPreviewNavigating;

            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 6;
            split.Panel1MinSize = 120;
            split.Panel2MinSize = 120;
            split.Panel1.Controls.Add(editorHost);
            split.Panel2.Controls.Add(preview);
            split.Panel2.BackColor = Color.White;

            // ---- 查找/替换条
            findBar = new Panel();
            findBar.Dock = DockStyle.Bottom;
            findBar.Height = 30;
            findBar.Visible = false;

            Label lbFind = new Label();
            lbFind.Text = "查找";
            lbFind.AutoSize = true;
            lbFind.Location = new Point(8, 7);
            findBar.Controls.Add(lbFind);

            findBox = new TextBox();
            findBox.Location = new Point(48, 3);
            findBox.Width = 200;
            findBox.KeyDown += OnFindBoxKeyDown;
            findBar.Controls.Add(findBox);

            Label lbRepl = new Label();
            lbRepl.Text = "替换";
            lbRepl.AutoSize = true;
            lbRepl.Location = new Point(258, 7);
            findBar.Controls.Add(lbRepl);

            replBox = new TextBox();
            replBox.Location = new Point(298, 3);
            replBox.Width = 200;
            findBar.Controls.Add(replBox);

            findCase = new CheckBox();
            findCase.Text = "区分大小写";
            findCase.AutoSize = true;
            findCase.Location = new Point(508, 5);
            findBar.Controls.Add(findCase);

            findBar.Controls.Add(MakeButton("下一个", 588, 2, delegate { FindNext(); }));
            findBar.Controls.Add(MakeButton("替换", 668, 2, delegate { ReplaceCurrent(); }));
            findBar.Controls.Add(MakeButton("全部替换", 738, 2, delegate { ReplaceAll(); }));
            findBar.Controls.Add(MakeButton("关闭", 818, 2, delegate { HideFindBar(); }));

            // ---- 状态栏
            statusStrip.Items.AddRange(new ToolStripItem[] { stFile, stEnc, stPos, stLen, stMode });
            stFile.Spring = true;
            stFile.TextAlign = ContentAlignment.MiddleLeft;
            stEnc.BorderSides = ToolStripStatusLabelBorderSides.Left;
            stPos.BorderSides = ToolStripStatusLabelBorderSides.Left;
            stLen.BorderSides = ToolStripStatusLabelBorderSides.Left;
            stMode.BorderSides = ToolStripStatusLabelBorderSides.Left;

            // ---- 菜单
            MenuStrip menu = new MenuStrip();
            menu.Dock = DockStyle.Top;

            ToolStripMenuItem mFile = new ToolStripMenuItem("文件(&F)");
            mFile.DropDownItems.Add(Mi("新建(&N)", Keys.Control | Keys.N, delegate { NewFile(); }));
            mFile.DropDownItems.Add(Mi("打开(&O)…", Keys.Control | Keys.O, delegate { OpenDialog(); }));
            miRecent = new ToolStripMenuItem("最近打开");
            mFile.DropDownItems.Add(miRecent);
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Mi("保存(&S)", Keys.Control | Keys.S, delegate { Save(false); }));
            mFile.DropDownItems.Add(Mi("另存为(&A)…", Keys.Control | Keys.Shift | Keys.S, delegate { Save(true); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Mi("重新载入", Keys.F5, delegate { Reload(); }));
            mFile.DropDownItems.Add(Mi("打开所在文件夹", Keys.None, delegate { OpenContainingFolder(); }));
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(Mi("退出(&X)", Keys.None, delegate { Close(); }));

            ToolStripMenuItem mEdit = new ToolStripMenuItem("编辑(&E)");
            mEdit.DropDownItems.Add(Mi("撤销", Keys.Control | Keys.Z, delegate { editor.Undo(); }));
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(Mi("剪切", Keys.Control | Keys.X, delegate { editor.Cut(); }));
            mEdit.DropDownItems.Add(Mi("复制", Keys.Control | Keys.C, delegate { editor.Copy(); }));
            mEdit.DropDownItems.Add(Mi("粘贴", Keys.Control | Keys.V, delegate { editor.Paste(); }));
            mEdit.DropDownItems.Add(Mi("全选", Keys.Control | Keys.A, delegate { editor.SelectAll(); }));
            mEdit.DropDownItems.Add(new ToolStripSeparator());
            mEdit.DropDownItems.Add(Mi("查找…", Keys.Control | Keys.F, delegate { ShowFindBar(false); }));
            mEdit.DropDownItems.Add(Mi("替换…", Keys.Control | Keys.H, delegate { ShowFindBar(true); }));

            ToolStripMenuItem mView = new ToolStripMenuItem("查看(&V)");
            miBoth = Mi("编辑 + 预览", Keys.Control | Keys.D1, delegate { viewMode = 0; ApplyViewMode(); });
            miEditOnly = Mi("仅编辑", Keys.Control | Keys.D2, delegate { viewMode = 1; ApplyViewMode(); });
            miPreviewOnly = Mi("仅预览", Keys.Control | Keys.D3, delegate { viewMode = 2; ApplyViewMode(); });
            mView.DropDownItems.Add(miBoth);
            mView.DropDownItems.Add(miEditOnly);
            mView.DropDownItems.Add(miPreviewOnly);
            mView.DropDownItems.Add(new ToolStripSeparator());
            miWrap = Mi("自动换行", Keys.None, delegate { ApplyWrap(); });
            miWrap.CheckOnClick = true;
            miWrap.Checked = wrapConfig;
            mView.DropDownItems.Add(miWrap);
            miStatus = Mi("状态栏", Keys.None, delegate { statusStrip.Visible = miStatus.Checked; });
            miStatus.CheckOnClick = true;
            miStatus.Checked = true;
            mView.DropDownItems.Add(miStatus);
            ToolStripMenuItem mTheme = new ToolStripMenuItem("主题");
            miThemeFollow = Mi("跟随系统", Keys.None, delegate { SetTheme(0); });
            miThemeLight = Mi("浅色", Keys.None, delegate { SetTheme(1); });
            miThemeDark = Mi("深色", Keys.None, delegate { SetTheme(2); });
            mTheme.DropDownItems.Add(miThemeFollow);
            mTheme.DropDownItems.Add(miThemeLight);
            mTheme.DropDownItems.Add(miThemeDark);
            mView.DropDownItems.Add(mTheme);
            miHardBreak = Mi("保留单换行", Keys.None, delegate { hardBreak = miHardBreak.Checked; RenderPreview(true); });
            miHardBreak.CheckOnClick = true;
            miHardBreak.Checked = true;
            mView.DropDownItems.Add(miHardBreak);
            mView.DropDownItems.Add(new ToolStripSeparator());
            mView.DropDownItems.Add(Mi("放大字号", Keys.Control | Keys.Oemplus, delegate { Zoom(1); }));
            mView.DropDownItems.Add(Mi("缩小字号", Keys.Control | Keys.OemMinus, delegate { Zoom(-1); }));
            mView.DropDownItems.Add(Mi("重置字号", Keys.Control | Keys.D0, delegate { fontPercent = 100; ApplyFont(); RenderPreview(true); }));

            ToolStripMenuItem mFmt = new ToolStripMenuItem("格式(&O)");
            mFmt.DropDownItems.Add(Mi("标题 1", Keys.None, delegate { PrefixLine("# "); }));
            mFmt.DropDownItems.Add(Mi("标题 2", Keys.None, delegate { PrefixLine("## "); }));
            mFmt.DropDownItems.Add(Mi("标题 3", Keys.None, delegate { PrefixLine("### "); }));
            mFmt.DropDownItems.Add(new ToolStripSeparator());
            mFmt.DropDownItems.Add(Mi("粗体", Keys.Control | Keys.B, delegate { Wrap("**", "**"); }));
            mFmt.DropDownItems.Add(Mi("斜体", Keys.Control | Keys.I, delegate { Wrap("*", "*"); }));
            mFmt.DropDownItems.Add(Mi("删除线", Keys.None, delegate { Wrap("~~", "~~"); }));
            mFmt.DropDownItems.Add(Mi("行内代码", Keys.Control | Keys.E, delegate { Wrap("`", "`"); }));
            mFmt.DropDownItems.Add(Mi("代码块", Keys.None, delegate { InsertBlock("```\n", "\n```"); }));
            mFmt.DropDownItems.Add(new ToolStripSeparator());
            mFmt.DropDownItems.Add(Mi("引用", Keys.None, delegate { PrefixLine("> "); }));
            mFmt.DropDownItems.Add(Mi("无序列表", Keys.None, delegate { PrefixLine("- "); }));
            mFmt.DropDownItems.Add(Mi("有序列表", Keys.None, delegate { PrefixLine("1. "); }));
            mFmt.DropDownItems.Add(Mi("任务", Keys.None, delegate { PrefixLine("- [ ] "); }));
            mFmt.DropDownItems.Add(new ToolStripSeparator());
            mFmt.DropDownItems.Add(Mi("链接", Keys.Control | Keys.K, delegate { Wrap("[", "](https://)"); }));
            mFmt.DropDownItems.Add(Mi("图片", Keys.None, delegate { Wrap("![", "](图片路径)") ; }));
            mFmt.DropDownItems.Add(Mi("表格", Keys.None, delegate { InsertBlock("| 列 1 | 列 2 |\n| --- | --- |\n|  |  |\n", ""); }));
            mFmt.DropDownItems.Add(Mi("分割线", Keys.None, delegate { InsertBlock("\n---\n", ""); }));

            ToolStripMenuItem mHelp = new ToolStripMenuItem("帮助(&H)");
            mHelp.DropDownItems.Add(Mi("Markdown 语法速查", Keys.None, delegate { ShowCheatSheet(); }));
            mHelp.DropDownItems.Add(Mi("关于 mdpad", Keys.None, delegate { ShowAbout(); }));

            menu.Items.Add(mFile);
            menu.Items.Add(mEdit);
            menu.Items.Add(mView);
            menu.Items.Add(mFmt);
            menu.Items.Add(mHelp);
            MainMenuStrip = menu;
            menuStrip = menu;

            BuildToolbar();

            debounce.Interval = 380;
            debounce.Tick += OnDebounceTick;
            previewFallback.Interval = 1500;
            previewFallback.Tick += OnPreviewFallbackTick;

            // 注意顺序：先加填充控件，再按自下而上加停靠控件
            Controls.Add(split);
            Controls.Add(findBar);
            Controls.Add(statusStrip);
            Controls.Add(toolbar);
            Controls.Add(menu);

            RebuildRecentMenu();
        }

        private static Button MakeButton(string text, int x, int y, EventHandler h)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(x, y);
            b.Size = new Size(76, 25);
            b.Click += h;
            return b;
        }

        private static ToolStripMenuItem Mi(string text, Keys keys, EventHandler h)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            if (keys != Keys.None) it.ShortcutKeys = keys;
            if (h != null) it.Click += h;
            return it;
        }

        // ================================================================ 工具栏 / 主题 / 图标

        private void BuildToolbar()
        {
            toolbar = new ToolStrip();
            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Padding = new Padding(6, 3, 6, 3);
            toolbar.ImageScalingSize = new Size(20, 20);

            AddTb("新建（Ctrl+N）", "new", delegate { NewFile(); });
            AddTb("打开（Ctrl+O）", "open", delegate { OpenDialog(); });
            AddTb("保存（Ctrl+S）", "save", delegate { Save(false); });
            toolbar.Items.Add(new ToolStripSeparator());
            AddTb("编辑 + 预览（Ctrl+1）", "col2", delegate { viewMode = 0; ApplyViewMode(); });
            AddTb("仅编辑（Ctrl+2）", "col1", delegate { viewMode = 1; ApplyViewMode(); });
            AddTb("仅预览（Ctrl+3）", "eye", delegate { viewMode = 2; ApplyViewMode(); });
            toolbar.Items.Add(new ToolStripSeparator());
            AddTb("查找 / 替换（Ctrl+F）", "find", delegate { ShowFindBar(false); });
            AddTb("切换浅色 / 深色", "theme", delegate { SetTheme(isDarkTheme ? 1 : 2); });
            toolbar.Items.Add(new ToolStripSeparator());
            AddTb("缩小字号", "minus", delegate { Zoom(-1); });
            AddTb("放大字号", "plus", delegate { Zoom(1); });
        }

        private void AddTb(string tip, string kind, EventHandler h)
        {
            ToolStripButton b = new ToolStripButton();
            b.DisplayStyle = ToolStripItemDisplayStyle.Image;
            b.ToolTipText = tip;
            b.Tag = kind;
            b.AutoSize = false;
            b.Size = new Size(30, 26);
            b.Click += h;
            toolbar.Items.Add(b);
        }

        private void SetTheme(int mode)
        {
            themeMode = mode;
            ApplyTheme();
            RenderPreview(true);
        }

        private static bool IsSystemDark()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return Convert.ToInt32(v) == 0;
            }
            catch { return false; }
        }

        private void ApplyTheme()
        {
            isDarkTheme = themeMode == 2 || (themeMode == 0 && IsSystemDark());
            darkPreview = isDarkTheme;

            Color bgChrome = isDarkTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
            Color bgEditor = isDarkTheme ? Color.FromArgb(30, 30, 30) : Color.White;
            Color fgEditor = isDarkTheme ? Color.FromArgb(220, 220, 220) : Color.FromArgb(31, 31, 31);
            Color fgChrome = isDarkTheme ? Color.FromArgb(228, 228, 228) : Color.FromArgb(32, 32, 32);
            Color divider = isDarkTheme ? Color.FromArgb(58, 58, 58) : Color.FromArgb(226, 226, 226);

            DoubleBuffered = true;
            BackColor = bgChrome;
            ForeColor = fgChrome;

            editorHost.BackColor = bgEditor;
            editor.BackColor = bgEditor;
            editor.ForeColor = fgEditor;

            split.BackColor = divider;
            split.Panel1.BackColor = bgEditor;
            split.Panel2.BackColor = bgEditor;

            findBar.BackColor = bgChrome;
            findBar.ForeColor = fgChrome;
            foreach (Control c in findBar.Controls)
            {
                c.ForeColor = fgChrome;
                TextBox tb = c as TextBox;
                if (tb != null) { tb.BackColor = bgEditor; tb.ForeColor = fgEditor; }
            }

            menuStrip.BackColor = bgChrome;
            menuStrip.ForeColor = fgChrome;
            toolbar.BackColor = bgChrome;
            toolbar.ForeColor = fgChrome;
            statusStrip.BackColor = bgChrome;
            statusStrip.ForeColor = fgChrome;
            foreach (ToolStripItem it in statusStrip.Items) it.ForeColor = fgChrome;

            MdPadRenderer r = new MdPadRenderer(bgChrome, fgChrome, isDarkTheme);
            menuStrip.Renderer = r;
            toolbar.Renderer = r;
            statusStrip.Renderer = r;

            foreach (ToolStripItem it in toolbar.Items)
            {
                ToolStripButton b = it as ToolStripButton;
                if (b != null && b.Tag is string)
                {
                    Image oldImg = b.Image;
                    b.Image = Glyph((string)b.Tag, fgChrome);
                    if (oldImg != null) oldImg.Dispose();
                }
            }

            miThemeFollow.Checked = themeMode == 0;
            miThemeLight.Checked = themeMode == 1;
            miThemeDark.Checked = themeMode == 2;

            Icon oldIcon = Icon;
            Icon = MakeAppIcon();
            if (oldIcon != null) oldIcon.Dispose();

            previewReady = false;
            previewInitializing = false;
            if (viewMode != 1) InitPreview();
            ApplyDwm();
        }

        private void ApplyDwm()
        {
            try
            {
                if (!IsHandleCreated) return;
                int dark = isDarkTheme ? 1 : 0;
                int cap = (isDarkTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243)).ToArgb();
                int txt = (isDarkTheme ? Color.White : Color.FromArgb(28, 28, 28)).ToArgb();
                int border = (isDarkTheme ? Color.FromArgb(58, 58, 58) : Color.FromArgb(219, 219, 219)).ToArgb();
                int a;
                a = 20; DwmSetWindowAttribute(Handle, a, ref dark, 4);
                a = 35; DwmSetWindowAttribute(Handle, a, ref cap, 4);
                a = 36; DwmSetWindowAttribute(Handle, a, ref txt, 4);
                a = 34; DwmSetWindowAttribute(Handle, a, ref border, 4);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon MakeAppIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(58, 122, 214)))
                        g.FillEllipse(b, 0, 0, 31, 31);
                    using (Font f = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush w = new SolidBrush(Color.White))
                    {
                        StringFormat sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString("M", f, w, new RectangleF(0, 1, 31, 31), sf);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        /// <summary>工具栏图标：运行时用 GDI+ 画，随主题换色，不依赖外部资源</summary>
        private static Image Glyph(string kind, Color c)
        {
            Bitmap bmp = new Bitmap(20, 20);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen p = new Pen(c, 1.6f))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    switch (kind)
                    {
                        case "new":
                            g.DrawRectangle(p, 4, 3, 12, 14);
                            g.DrawLine(p, 7, 7, 13, 7);
                            g.DrawLine(p, 7, 10, 13, 10);
                            break;
                        case "open":
                            g.DrawLine(p, 3, 15, 17, 15);
                            g.DrawLine(p, 3, 6, 3, 15);
                            g.DrawLine(p, 3, 6, 8, 6);
                            g.DrawLine(p, 8, 6, 9, 8);
                            g.DrawLine(p, 9, 8, 17, 8);
                            g.DrawLine(p, 17, 8, 17, 15);
                            break;
                        case "save":
                            g.DrawRectangle(p, 4, 3, 12, 14);
                            g.DrawRectangle(p, 7, 3, 6, 5);
                            g.DrawRectangle(p, 6, 11, 8, 6);
                            break;
                        case "col2":
                            g.DrawRectangle(p, 3, 4, 14, 12);
                            g.DrawLine(p, 10, 4, 10, 16);
                            break;
                        case "col1":
                            g.DrawRectangle(p, 3, 4, 14, 12);
                            break;
                        case "eye":
                            {
                                g.DrawEllipse(p, 3, 6, 14, 9);
                                using (SolidBrush sb = new SolidBrush(c)) g.FillEllipse(sb, 8, 9, 4, 4);
                            }
                            break;
                        case "find":
                            g.DrawEllipse(p, 4, 4, 9, 9);
                            g.DrawLine(p, 12, 12, 16, 16);
                            break;
                        case "theme":
                            {
                                using (SolidBrush sb = new SolidBrush(c)) g.FillPie(sb, 4, 4, 12, 12, 90, 180);
                                g.DrawEllipse(p, 4, 4, 12, 12);
                            }
                            break;
                        case "minus":
                            g.DrawLine(p, 5, 10, 15, 10);
                            break;
                        case "plus":
                            g.DrawLine(p, 5, 10, 15, 10);
                            g.DrawLine(p, 10, 5, 10, 15);
                            break;
                    }
                }
            }
            return bmp;
        }

        private void ApplyWrap()
        {
            editor.WordWrap = miWrap.Checked;
            editor.ScrollBars = editor.WordWrap ? ScrollBars.Vertical : ScrollBars.Both;
        }

        private void ApplyViewMode()
        {
            split.Panel1Collapsed = viewMode == 2;
            split.Panel2Collapsed = viewMode == 1;
            miBoth.Checked = viewMode == 0;
            miEditOnly.Checked = viewMode == 1;
            miPreviewOnly.Checked = viewMode == 2;
            if (viewMode != 1)
            {
                if (!previewReady) InitPreview();
                else RenderPreview(true);
            }
            UpdateStatus();
        }

        private void ApplyFont()
        {
            float pt = 10.5f * fontPercent / 100f;
            string fam = "Cascadia Mono";
            try
            {
                using (Font test = new Font(fam, pt)) { if (test.Name != fam) fam = "Consolas"; }
            }
            catch { fam = "Consolas"; }
            Font old = editor.Font;
            editor.Font = new Font(fam, pt, FontStyle.Regular);
            if (old != null) old.Dispose();
            if (previewReady) RenderPreview(true);
        }

        private void Zoom(int delta)
        {
            fontPercent += delta * 10;
            if (fontPercent < 60) fontPercent = 60;
            if (fontPercent > 220) fontPercent = 220;
            ApplyFont();
        }

        // ================================================================ 预览

        private void InitPreview()
        {
            if (previewInitializing) return;          // 同一时刻只允许一次导航，否则 IE 不触发 DocumentCompleted
            previewInitializing = true;
            previewReady = false;
            try
            {
                preview.DocumentText = MarkdownRenderer.WrapPage("<p></p>", darkPreview, fontPercent);
                previewFallback.Stop();
                previewFallback.Start();
            }
            catch { previewInitializing = false; }
        }

        private void OnPreviewDocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            previewFallback.Stop();
            previewInitializing = false;
            previewReady = true;
            RenderPreviewNow();
        }

        private void OnPreviewFallbackTick(object sender, EventArgs e)
        {
            previewFallback.Stop();
            if (previewReady) return;
            previewInitializing = false;
            previewReady = true;
            RenderPreviewNow();
        }

        private void OnPreviewNavigating(object sender, WebBrowserNavigatingEventArgs e)
        {
            // 让外部链接在系统浏览器里打开，别把预览窗导航走
            if (e.Url != null && e.Url.Scheme != "about" && previewReady)
            {
                e.Cancel = true;
                try { System.Diagnostics.Process.Start(e.Url.AbsoluteUri); }
                catch { }
            }
        }

        private void RenderPreview(bool force)
        {
            if (viewMode == 1) return;
            if (!previewReady) { InitPreview(); return; }
            RenderPreviewNow();
        }

        private void RenderPreviewNow()
        {
            string baseDir = currentPath != null ? Path.GetDirectoryName(currentPath) : null;
            string body = MarkdownRenderer.RenderBody(editor.Text, baseDir, hardBreak);
            try
            {
                object y = preview.Document.InvokeScript("mdGetScroll");
                preview.Document.InvokeScript("mdSetContent", new object[] { body, y });
            }
            catch
            {
                try
                {
                    preview.DocumentText = MarkdownRenderer.WrapPage(body, darkPreview, fontPercent);
                }
                catch { }
            }
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            debounce.Stop();
            RenderPreview(false);
        }

        // ================================================================ 编辑区事件

        private void OnEditorTextChanged(object sender, EventArgs e)
        {
            if (loading) return;
            SetDirty(true);
            debounce.Stop();
            debounce.Start();
            UpdateStatus();
        }

        private void OnSelectionChangedLike(object sender, EventArgs e) { UpdateStatus(); }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab && !e.Shift && !e.Control)
            {
                e.SuppressKeyPress = true;
                if (editor.SelectionLength > 0 && editor.Text.Substring(editor.SelectionStart, editor.SelectionLength).IndexOf('\n') >= 0)
                    IndentSelection("    ", false);
                else
                    editor.SelectedText = "    ";
            }
            else if (e.KeyCode == Keys.Tab && e.Shift && !e.Control)
            {
                e.SuppressKeyPress = true;
                IndentSelection("    ", true);
            }
            else if (e.KeyCode == Keys.Escape && findBar.Visible)
            {
                HideFindBar();
            }
        }

        // ================================================================ 文件操作

        private void OpenContainingFolder()
        {
            if (currentPath == null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + currentPath + "\""); }
            catch { }
        }

        private void NewFile()
        {
            if (!ConfirmDiscard()) return;
            loading = true;
            editor.Clear();
            loading = false;
            currentPath = null;
            fileEncoding = new UTF8Encoding(false);
            newline = "\r\n";
            SetDirty(false);
            UpdateTitle();
            RenderPreview(true);
        }

        private void OpenDialog()
        {
            if (!ConfirmDiscard()) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "打开 Markdown / 文本";
                dlg.Filter = "Markdown (*.md;*.markdown;*.mdx)|*.md;*.markdown;*.mdx|文本 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                if (currentPath != null) dlg.InitialDirectory = Path.GetDirectoryName(currentPath);
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName, true);
            }
        }

        private void OpenFile(string path, bool addRecent)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                bool bom;
                Encoding enc = DetectEncoding(bytes, out bom);
                int skip = bom ? enc.GetPreamble().Length : 0;
                string raw = enc.GetString(bytes, skip, bytes.Length - skip);
                newline = CountOccurrences(raw, "\r\n") >= CountOccurrences(raw, "\n") ? "\r\n" : "\n";
                string text = raw.Replace("\r\n", "\n").Replace('\r', '\n');

                loading = true;
                editor.Text = text.Replace("\n", "\r\n");
                editor.SelectionStart = 0;
                editor.SelectionLength = 0;
                loading = false;

                currentPath = path;
                fileEncoding = enc;
                SetDirty(false);
                UpdateTitle();
                SetupWatcher(path);
                RenderPreview(true);
                UpdateStatus();
                if (addRecent) AddRecent(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开失败：" + ex.Message, "mdpad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static int CountOccurrences(string s, string needle)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(needle)) return 0;
            int n = 0, i = 0;
            while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        private static Encoding DetectEncoding(byte[] b, out bool bom)
        {
            bom = false;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { bom = true; return new UTF8Encoding(true); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { bom = true; return Encoding.Unicode; }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { bom = true; return Encoding.BigEndianUnicode; }
            try
            {
                UTF8Encoding strict = new UTF8Encoding(false, true);
                strict.GetString(b);
                return new UTF8Encoding(false);
            }
            catch
            {
                try { return Encoding.GetEncoding(936); }
                catch { return Encoding.Default; }
            }
        }

        private void Save(bool saveAs)
        {
            string target = currentPath;
            if (saveAs || target == null)
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "保存为";
                    dlg.Filter = "Markdown (*.md)|*.md|所有文件 (*.*)|*.*";
                    dlg.DefaultExt = "md";
                    if (target != null) { dlg.FileName = Path.GetFileName(target); dlg.InitialDirectory = Path.GetDirectoryName(target); }
                    else dlg.FileName = "未命名.md";
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    target = dlg.FileName;
                }
            }
            try
            {
                string content = editor.Text.Replace("\r\n", "\n").Replace("\n", newline);
                lastSaveUtc = DateTime.UtcNow;
                byte[] preamble = fileEncoding.GetPreamble();
                byte[] body = fileEncoding.GetBytes(content);
                byte[] all = new byte[preamble.Length + body.Length];
                Buffer.BlockCopy(preamble, 0, all, 0, preamble.Length);
                Buffer.BlockCopy(body, 0, all, preamble.Length, body.Length);
                File.WriteAllBytes(target, all);
                currentPath = target;
                SetDirty(false);
                UpdateTitle();
                SetupWatcher(target);
                AddRecent(target);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存失败：" + ex.Message, "mdpad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Reload()
        {
            if (currentPath == null) return;
            if (dirty && MessageBox.Show(this, "当前修改会丢失，确定重新载入？", "mdpad",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            OpenFile(currentPath, false);
        }

        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            DialogResult r = MessageBox.Show(this, "「" + DisplayName() + "」有未保存的修改，要保存吗？", "mdpad",
                                             MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) { Save(false); return !dirty; }
            return true;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            SaveConfig();
        }

        private string DisplayName()
        {
            return currentPath == null ? "未命名" : Path.GetFileName(currentPath);
        }

        private void SetDirty(bool v)
        {
            dirty = v;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            Text = (dirty ? "*" : "") + DisplayName() + " - mdpad";
        }

        // ================================================================ 外部改动

        private void SetupWatcher(string path)
        {
            try
            {
                if (watcher != null) { watcher.Dispose(); watcher = null; }
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                watcher = new FileSystemWatcher(dir, Path.GetFileName(path));
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
                watcher.Changed += OnExternalChange;
                watcher.Created += OnExternalChange;
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private void OnExternalChange(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.UtcNow - lastSaveUtc).TotalSeconds < 3) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (currentPath == null) return;
                    if (MessageBox.Show(this, "文件已在外部被修改，是否重新载入？", "mdpad",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                        OpenFile(currentPath, false);
                });
            }
            catch { }
        }

        // ================================================================ 状态 / 配置

        private void UpdateStatus()
        {
            stFile.Text = currentPath == null ? "未命名" : currentPath;
            stEnc.Text = fileEncoding.WebName.ToUpperInvariant() + (fileEncoding.GetPreamble().Length > 0 ? " (BOM)" : "") + " · " + (newline == "\r\n" ? "CRLF" : "LF");
            int line = editor.GetLineFromCharIndex(editor.SelectionStart) + 1;
            int col = editor.SelectionStart - editor.GetFirstCharIndexFromLine(line - 1) + 1;
            stPos.Text = "行 " + line + "，列 " + col;
            stLen.Text = editor.TextLength + " 字符";
            stMode.Text = viewMode == 0 ? "编辑+预览" : (viewMode == 1 ? "仅编辑" : "仅预览");
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(configPath)) return;
                foreach (string raw in File.ReadAllLines(configPath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "FontPercent": fontPercent = ParseInt(v, 100); break;
                        case "DarkPreview": darkPreview = v == "1"; break;
                        case "ThemeMode": themeMode = ParseInt(v, 0); break;
                        case "HardBreak": hardBreak = v == "1"; break;
                        case "WordWrap": wrapConfig = v != "0"; break;
                        case "StatusBar": showStatusConfig = v != "0"; break;
                        case "ViewMode": viewMode = ParseInt(v, 0); break;
                        case "Splitter": splitterConfig = ParseInt(v, 0); break;
                        case "Width": widthConfig = ParseInt(v, 0); break;
                        case "Height": heightConfig = ParseInt(v, 0); break;
                        case "Maximized": maxConfig = v == "1"; break;
                    }
                    if (k.StartsWith("Recent"))
                    {
                        int idx = ParseInt(k.Substring(6), -1);
                        if (idx >= 0 && File.Exists(v) && !recent.Contains(v)) recent.Add(v);
                    }
                }
                if (widthConfig > 400) Width = widthConfig;
                if (heightConfig > 300) Height = heightConfig;
            }
            catch { }
        }

        private bool showStatusConfig = true;
        private bool wrapConfig = true;
        private int splitterConfig;
        private int widthConfig;
        private int heightConfig;
        private bool maxConfig;

        private void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                List<string> lines = new List<string>();
                lines.Add("FontPercent=" + fontPercent);
                lines.Add("DarkPreview=" + (darkPreview ? "1" : "0"));
                lines.Add("ThemeMode=" + themeMode);
                lines.Add("HardBreak=" + (hardBreak ? "1" : "0"));
                lines.Add("WordWrap=" + (editor.WordWrap ? "1" : "0"));
                lines.Add("StatusBar=" + (statusStrip.Visible ? "1" : "0"));
                lines.Add("ViewMode=" + viewMode);
                lines.Add("Splitter=" + split.SplitterDistance);
                lines.Add("Width=" + (WindowState == FormWindowState.Normal ? Width : RestoreBounds.Width));
                lines.Add("Height=" + (WindowState == FormWindowState.Normal ? Height : RestoreBounds.Height));
                lines.Add("Maximized=" + (WindowState == FormWindowState.Maximized ? "1" : "0"));
                for (int i = 0; i < recent.Count && i < 10; i++) lines.Add("Recent" + i + "=" + recent[i]);
                File.WriteAllLines(configPath, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static int ParseInt(string s, int def)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (splitterConfig > 0 && splitterConfig < split.Width - 120) split.SplitterDistance = splitterConfig;
            else
            {
                try { split.SplitterDistance = Math.Max(200, split.Width / 2); }
                catch { }
            }
            statusStrip.Visible = showStatusConfig;
            miStatus.Checked = showStatusConfig;
            miHardBreak.Checked = hardBreak;
            ApplyWrap();
            ApplyViewMode();
            if (maxConfig) WindowState = FormWindowState.Maximized;
            miStatus.Checked = statusStrip.Visible;
        }

        private void AddRecent(string path)
        {
            recent.RemoveAll(delegate(string s) { return string.Equals(s, path, StringComparison.OrdinalIgnoreCase); });
            recent.Insert(0, path);
            while (recent.Count > 10) recent.RemoveAt(recent.Count - 1);
            RebuildRecentMenu();
        }

        private void RebuildRecentMenu()
        {
            if (miRecent == null) return;
            miRecent.DropDownItems.Clear();
            if (recent.Count == 0)
            {
                ToolStripMenuItem none = new ToolStripMenuItem("（空）");
                none.Enabled = false;
                miRecent.DropDownItems.Add(none);
                return;
            }
            for (int i = 0; i < recent.Count; i++)
            {
                string p = recent[i];
                ToolStripMenuItem it = new ToolStripMenuItem("&" + (i + 1) + " " + p);
                it.Click += delegate { if (ConfirmDiscard()) OpenFile(p, true); };
                miRecent.DropDownItems.Add(it);
            }
        }

        // ================================================================ 查找 / 替换

        private void ShowFindBar(bool withReplace)
        {
            findBar.Visible = true;
            if (editor.SelectionLength > 0 && editor.SelectionLength < 200 && editor.SelectedText.IndexOf('\n') < 0)
                findBox.Text = editor.SelectedText;
            if (withReplace) replBox.Focus(); else findBox.Focus();
        }

        private void HideFindBar()
        {
            findBar.Visible = false;
            editor.Focus();
        }

        private void OnFindBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); }
        }

        private void FindNext()
        {
            string needle = findBox.Text;
            if (needle.Length == 0) return;
            StringComparison cmp = findCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            string text = editor.Text;
            int start = editor.SelectionStart + editor.SelectionLength;
            if (start > text.Length) start = 0;
            int idx = start <= text.Length ? text.IndexOf(needle, start, cmp) : -1;
            if (idx < 0 && start > 0) idx = text.IndexOf(needle, 0, cmp);
            if (idx < 0)
            {
                MessageBox.Show(this, "找不到「" + needle + "」。", "mdpad", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            editor.Select(idx, needle.Length);
            editor.ScrollToCaret();
            editor.Focus();
        }

        private void ReplaceCurrent()
        {
            string needle = findBox.Text;
            if (needle.Length == 0) return;
            if (editor.SelectionLength == needle.Length &&
                string.Equals(editor.SelectedText, needle, findCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                editor.SelectedText = replBox.Text;
            }
            FindNext();
        }

        private void ReplaceAll()
        {
            string needle = findBox.Text;
            if (needle.Length == 0) return;
            RegexOptions opt = findCase.Checked ? RegexOptions.None : RegexOptions.IgnoreCase;
            string before = editor.Text;
            string after = Regex.Replace(before, Regex.Escape(needle), replBox.Text.Replace("$", "$$"), opt);
            if (after == before) { MessageBox.Show(this, "没有可替换的内容。", "mdpad"); return; }
            int caret = editor.SelectionStart;
            loading = true;
            editor.Text = after;
            loading = false;
            editor.SelectionStart = Math.Min(caret, editor.TextLength);
            SetDirty(true);
            RenderPreview(true);
            MessageBox.Show(this, "已全部替换。", "mdpad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================================================================ 格式插入

        private void Wrap(string pre, string post)
        {
            int s = editor.SelectionStart, l = editor.SelectionLength;
            string sel = editor.SelectedText;
            editor.SelectedText = pre + sel + post;
            if (l == 0) editor.SelectionStart = s + pre.Length;
            else editor.Select(s + pre.Length, l);
            editor.Focus();
        }

        private void PrefixLine(string prefix)
        {
            int first = editor.GetFirstCharIndexOfCurrentLine();
            editor.SelectionStart = first;
            editor.SelectionLength = 0;
            editor.SelectedText = prefix;
            editor.Focus();
        }

        private void InsertBlock(string pre, string post)
        {
            int s = editor.SelectionStart;
            string sel = editor.SelectedText;
            editor.SelectedText = pre + sel + post;
            editor.SelectionStart = s + pre.Length + sel.Length;
            editor.Focus();
        }

        private void IndentSelection(string pad, bool outdent)
        {
            int start = editor.SelectionStart, len = editor.SelectionLength;
            string text = editor.Text;
            int firstLine = editor.GetLineFromCharIndex(start);
            int lastLine = editor.GetLineFromCharIndex(start + len);
            int a = editor.GetFirstCharIndexFromLine(firstLine);
            if (a < 0) a = 0;
            int b;
            if (lastLine + 1 <= editor.GetLineFromCharIndex(text.Length))
            {
                b = editor.GetFirstCharIndexFromLine(lastLine + 1);
                if (b < 0) b = text.Length;
            }
            else b = text.Length;

            string block = text.Substring(a, b - a);
            string[] arr = block.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < arr.Length; i++)
            {
                if (outdent)
                {
                    if (arr[i].StartsWith(pad)) arr[i] = arr[i].Substring(pad.Length);
                    else if (arr[i].StartsWith("\t")) arr[i] = arr[i].Substring(1);
                    else if (arr[i].StartsWith(" ")) arr[i] = arr[i].Substring(1);
                }
                else arr[i] = pad + arr[i];
            }
            string nb = string.Join(newline, arr);
            loading = true;
            editor.Select(a, b - a);
            editor.SelectedText = nb;
            loading = false;
            editor.Select(a, Math.Max(0, Math.Min(nb.Length, editor.TextLength - a)));
            SetDirty(true);
            RenderPreview(true);
        }

        // ================================================================ 拖放 / 帮助

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                e.Effect = (files != null && files.Length == 1) ? DragDropEffects.Copy : DragDropEffects.None;
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length != 1) return;
            if (ConfirmDiscard()) OpenFile(files[0], true);
        }

        private void ShowCheatSheet()
        {
            string t =
                "# 一级标题        ## 二级        ### 三级\r\n" +
                "**粗体**    *斜体*    ~~删除线~~    `行内代码`\r\n" +
                "> 引用\r\n" +
                "- 无序项  /  1. 有序项  /  - [ ] 任务\r\n" +
                "```\r\n代码块\r\n```\r\n" +
                "| 表头 | 表头 |\r\n| --- | --- |\r\n| 单元格 | 单元格 |\r\n" +
                "[文字](https://example.com)    ![图注](图片.png)\r\n" +
                "---（分割线）\r\n" +
                "\r\n快捷键：Ctrl+S 保存、Ctrl+B 粗体、Ctrl+I 斜体、Ctrl+K 链接、Ctrl+F 查找、\r\n" +
                "        Tab 缩进、Ctrl+1/2/3 切换视图、Ctrl+加号/减号 缩放";
            MessageBox.Show(this, t, "Markdown 语法速查", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowAbout()
        {
            MessageBox.Show(this,
                "mdpad 1.1.0\r\n\r\n记事本式的 Markdown 阅读 / 编辑器：左边改，右边即时渲染。\r\n" +
                "单个 exe、零依赖、不联网、不常驻。\r\n\r\n" +
                "配置文件：" + configPath,
                "关于 mdpad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>极简主题渲染器：把菜单 / 工具栏 / 状态栏刷成主题色（默认渲染器不认 BackColor）</summary>
    internal sealed class MdPadRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color bg;
        private readonly Color fg;
        private readonly bool dark;

        public MdPadRenderer(Color background, Color foreground, bool isDark)
            : base()
        {
            bg = background;
            fg = foreground;
            dark = isDark;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(219, 219, 219)))
                e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled
                ? fg
                : (dark ? Color.FromArgb(120, 120, 120) : Color.FromArgb(160, 160, 160));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            using (SolidBrush b = new SolidBrush(dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(225, 225, 225)))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            using (SolidBrush b = new SolidBrush(dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(226, 226, 226)))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(205, 205, 205)))
                e.Graphics.DrawLine(p, 3, y, e.Item.Width - 3, y);
        }
    }
}
