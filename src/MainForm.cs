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
        private readonly EditorBox editor = new EditorBox();
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
        private GutterPanel gutter;
        private ToolStrip toolbar;
        private MenuStrip menuStrip;
        private ToolStripMenuItem miThemeFollow;
        private ToolStripMenuItem miThemeLight;
        private ToolStripMenuItem miThemeDark;
        private int themeMode;              // 0 跟随系统 / 1 浅色 / 2 深色
        private bool isDarkTheme;
        private Font gutterFont;
        private Color themeEditorBg = Color.White;
        private Color themeDivider = Color.FromArgb(227, 227, 227);

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
        private string pendingFile;

        public MainForm(string[] args)
        {
            configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "mdpad\\config.ini");
            LoadConfig();

            Text = "mdpad";
            Font = PickUiFont(9f);
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

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            editor.DragEnter += OnDragEnter;
            editor.DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;
            pendingFile = (args != null && args.Length > 0 && File.Exists(args[0])) ? args[0] : null;

            // ★ 关键：在窗体第一次显示之前，把整棵控件树的句柄建好。
            //   否则句柄会在 WM_SHOWWINDOW 的 CreateControl 递归里创建 —— 本机（build 22631）
            //   实测那一步创建 EDIT 控件会失败：「创建窗口句柄时出错」，且 OnLoad 都来不及跑。
            //   注意 Control.CreateControl() 对「不可见」的子控件是跳过的（窗体尚未显示 → 全部不可见），
            //   所以必须直接访问 Handle 来强制创建。
            Safe("ctor-ForceHandles", delegate
            {
                ForceHandles(this);
                LogSafe("ctor: ForceHandles 完成; editor=" + editor.IsHandleCreated + " preview=" + preview.IsHandleCreated);
            });
        }

        private static void ForceHandles(Control c)
        {
            try { IntPtr h = c.Handle; }
            catch (Exception ex)
            {
                LogSafe("ForceHandles 失败: " + c.GetType().FullName + " [" + c.Name + "] " + ex.GetType().Name + " " + ex.Message);
                return;
            }
            foreach (Control child in c.Controls) ForceHandles(child);
        }

        // ---------------- 兜底：任何一步失败都写日志而不是弹崩溃框 ----------------
        internal static void LogSafe(string msg)
        {
            string[] dirs = new string[]
            {
                AppDomain.CurrentDomain.BaseDirectory,                                     // exe 同目录（最可靠）
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpad")
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    if (string.IsNullOrEmpty(dirs[i])) continue;
                    if (!Directory.Exists(dirs[i])) Directory.CreateDirectory(dirs[i]);
                    string file = Path.Combine(dirs[i], "mdpad-error-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
                    File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>
        /// 在窗体真正显示之前把整棵控件树的句柄建好。
        /// 不这么做的话，句柄会在 WM_SHOWWINDOW 的 CreateControl 递归里被创建 ——
        /// 实测这台机器上 EDIT 控件那一步会失败（「创建窗口句柄时出错」），且是间歇性的。
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LogSafe(string.Format("OnLoad: form={0} editor={1} preview={2}", IsHandleCreated, editor.IsHandleCreated, preview.IsHandleCreated));
            try
            {
                CreateControl();     // 递归创建子控件句柄（此时窗体已建句柄但尚未显示）
                LogSafe("OnLoad: CreateControl OK; editor=" + editor.IsHandleCreated + " preview=" + preview.IsHandleCreated);
            }
            catch (Exception ex)
            {
                LogSafe("OnLoad: CreateControl 失败: " + ex.ToString());
                try { editor.CreateControl(); LogSafe("OnLoad: 单独建 editor 句柄 OK"); }
                catch (Exception ex2) { LogSafe("OnLoad: 单独建 editor 句柄也失败: " + ex2.Message); }
            }
        }

        private static void Safe(string what, MethodInvoker act)
        {
            try { act(); }
            catch (Exception ex) { LogSafe("[" + what + "] " + ex.ToString()); }
        }

        // ================================================================ 界面

        private void BuildUi()
        {
            // ---- 编辑区（外面套一层带内边距的面板，视觉上不贴边）
            editorHost = new Panel();
            editorHost.Dock = DockStyle.Fill;
            editorHost.Padding = new Padding(2, 10, 8, 10);
            gutter = new GutterPanel(editor);
            gutter.Dock = DockStyle.Left;
            editorHost.Controls.Add(editor);      // 先加填充控件
            editorHost.Controls.Add(gutter);      // 再加左侧停靠控件

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
            editor.Scrolled += OnEditorScrolled;
            editor.KeyDown += OnEditorKeyDown;
            editor.Resize += delegate { UpdateEditorScrollbars(); };
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
            split.Panel1.Controls.Add(editorHost);            split.Panel2.Controls.Add(preview);
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
            statusStrip.SizingGrip = false;
            statusStrip.Padding = new Padding(8, 2, 8, 2);
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
            b.Size = new Size(40, 32);
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

            // Win11 原生调色（与记事本一致）：命令栏 #F3F3F3 / #202020，内容面 #FFFFFF / #202020
            Color bgChrome = isDarkTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
            Color bgEditor = isDarkTheme ? Color.FromArgb(32, 32, 32) : Color.White;
            Color fgEditor = isDarkTheme ? Color.FromArgb(232, 232, 232) : Color.FromArgb(27, 27, 27);
            Color fgChrome = isDarkTheme ? Color.FromArgb(232, 232, 232) : Color.FromArgb(27, 27, 27);
            Color divider = isDarkTheme ? Color.FromArgb(61, 61, 61) : Color.FromArgb(227, 227, 227);
            themeEditorBg = bgEditor;
            themeDivider = divider;

            DoubleBuffered = true;
            BackColor = bgChrome;
            ForeColor = fgChrome;

            editorHost.BackColor = bgEditor;
            editor.BackColor = bgEditor;
            editor.ForeColor = fgEditor;

            split.BackColor = divider;
            split.Panel1.BackColor = bgEditor;
            split.Panel2.BackColor = bgEditor;
            ApplyGutter();

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

            MdPadRenderer r = new MdPadRenderer(bgChrome, fgChrome, isDarkTheme,
                isDarkTheme ? Color.FromArgb(255, 255, 255, 18) : Color.FromArgb(0, 0, 0, 14),
                isDarkTheme ? Color.FromArgb(255, 255, 255, 28) : Color.FromArgb(0, 0, 0, 24));
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
            Icon = LoadAppIcon();
            if (oldIcon != null) oldIcon.Dispose();

            // 句柄还没建好：预览、标题栏材质、滚动条主题都留到 OnShown 再应用，
            // 否则会在这里强制 CreateHandle（WebBrowser/TextBox 都可能失败）。
            if (!IsHandleCreated) return;

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
                int backdrop = 2;      // DWMSBT_MAINWINDOW = Mica（标题栏原生材质）
                int border = (isDarkTheme ? Color.FromArgb(61, 61, 61) : Color.FromArgb(227, 227, 227)).ToArgb();
                int a;
                a = 20; DwmSetWindowAttribute(Handle, a, ref dark, 4);       // 沉浸式深色标题栏
                a = 38; DwmSetWindowAttribute(Handle, a, ref backdrop, 4);   // 系统背景材质 = Mica
                a = 34; DwmSetWindowAttribute(Handle, a, ref border, 4);     // 窗口描边
                // 注意：不设 CAPTION_COLOR/TEXT_COLOR —— 让系统按原生方式绘制 Mica 标题栏
            }
            catch { }
            ApplyDarkScrollbars();
        }

        // ---------------------------------------------------------------- 滚动条暗色
        // 编辑框与 IE 预览的滚动条是 Win32 经典控件，不跟应用配色；
        // 只能走 uxtheme 的暗色主题（SetWindowTheme + 未公开的 SetPreferredAppMode / AllowDarkModeForWindow）。

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        /// <summary>
        /// 深色滚动条：只用公开的 SetWindowTheme(hwnd, "DarkMode_Explorer", null)。
        /// ⚠️ 千万不要用 uxtheme 的未公开序号（#135 SetPreferredAppMode / #133 AllowDarkModeForWindow）：
        ///    序号随 Windows 版本漂移，本机 build 22631 上调用后会让 EDIT 控件再也建不出句柄
        ///    （表现：启动即「创建窗口句柄时出错」，实测 5 组开关矩阵定位）。
        /// </summary>
        private void ApplyDarkScrollbars()
        {
            try
            {
                if (!IsHandleCreated) return;
                string sub = isDarkTheme ? "DarkMode_Explorer" : "";
                ThemeWindow(editor.Handle, sub);
                ThemeWindow(preview.Handle, sub);
                ThemeScrollbarChildren(preview.Handle, sub);   // IE 的滚动条是 MSHTML 建出来的子窗口
                ThemeScrollbarChildren(editor.Handle, sub);
            }
            catch (Exception ex) { LogSafe("[ApplyDarkScrollbars] " + ex.ToString()); }
        }

        private static void ThemeWindow(IntPtr h, string sub)
        {
            if (h == IntPtr.Zero) return;
            try { SetWindowTheme(h, sub, sub); }
            catch { }
        }

        private void ThemeScrollbarChildren(IntPtr parent, string sub)
        {
            if (parent == IntPtr.Zero) return;
            try
            {
                EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
                {
                    StringBuilder sb = new StringBuilder(64);
                    GetClassName(h, sb, sb.Capacity);
                    if (sb.ToString() == "ScrollBar") ThemeWindow(h, sub);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        /// <summary>优先用仓库里那份多尺寸 mdpad.ico（tools\make-icon.ps1 生成），拿不到再退化成运行时绘制</summary>
        private static Icon LoadAppIcon()
        {
            string[] paths = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mdpad.ico"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mdpad", "mdpad.ico")
            };
            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    if (File.Exists(paths[i]))
                    {
                        using (Icon src = new Icon(paths[i], 32, 32)) return (Icon)src.Clone();
                    }
                }
                catch { }
            }
            return MakeAppIcon();
        }

        /// <summary>兜底图标（与 tools\make-icon.ps1 同一设计：圆角方形 + 渐变蓝 + M↓）</summary>
        private static Icon MakeAppIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
                    int inset = 1, side = 30, r = 7;
                    path.AddArc(inset, inset, r * 2, r * 2, 180, 90);
                    path.AddArc(inset + side - r * 2, inset, r * 2, r * 2, 270, 90);
                    path.AddArc(inset + side - r * 2, inset + side - r * 2, r * 2, r * 2, 0, 90);
                    path.AddArc(inset, inset + side - r * 2, r * 2, r * 2, 90, 90);
                    path.CloseFigure();
                    using (System.Drawing.Drawing2D.LinearGradientBrush bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Rectangle(0, 0, 32, 32), Color.FromArgb(88, 190, 255), Color.FromArgb(9, 84, 178), 90f))
                        g.FillPath(bg, path);
                    using (Pen p = new Pen(Color.White, 3f))
                    {
                        p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                        g.DrawLines(p, new PointF[] { new PointF(8f, 23f), new PointF(8f, 10f), new PointF(13.5f, 17f), new PointF(19f, 10f), new PointF(19f, 23f) });
                        g.DrawLine(p, 25.5f, 11f, 25.5f, 19f);
                        g.DrawLines(p, new PointF[] { new PointF(22.5f, 16f), new PointF(25.5f, 20f), new PointF(28.5f, 16f) });
                    }
                    path.Dispose();
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        /// <summary>空文档时的引导页（免得只看到一大片空白）</summary>
        private static string WelcomeBody()
        {
            string k = "<span class=\"kbd\">";
            return "<div class=\"welcome\">"
                 + "<h1>mdpad</h1>"
                 + "<p class=\"muted\">记事本式的 Markdown 编辑器 —— 左边改，右边即时渲染</p>"
                 + "<table>"
                 + "<tr><td>" + k + "Ctrl</span>" + k + "N</span> / " + k + "O</span> / " + k + "S</span></td><td>新建 / 打开 / 保存</td></tr>"
                 + "<tr><td>" + k + "Ctrl</span>" + k + "B</span> / " + k + "I</span> / " + k + "K</span></td><td>粗体 / 斜体 / 链接</td></tr>"
                 + "<tr><td>" + k + "Ctrl</span>" + k + "1</span> / " + k + "2</span> / " + k + "3</span></td><td>编辑 + 预览 / 仅编辑 / 仅预览</td></tr>"
                 + "<tr><td>" + k + "Ctrl</span>" + k + "F</span> / " + k + "H</span></td><td>查找 / 替换</td></tr>"
                 + "<tr><td>" + k + "Tab</span> / " + k + "Shift</span>" + k + "Tab</span></td><td>缩进 / 反缩进</td></tr>"
                 + "</table>"
                 + "<p class=\"muted\">把 .md 文件拖进窗口，或右键任意 .md → 「用 mdpad 编辑（Markdown）」</p>"
                 + "</div>";
        }

        /// <summary>工具栏图标：Win11 原生 Segoe Fluent Icons，随主题换色</summary>
        private Image Glyph(string kind, Color c)
        {
            string text = null;
            switch (kind)
            {
                case "new": text = "\uE7C3"; break;
                case "open": text = "\uE8E5"; break;
                case "save": text = "\uE74E"; break;
                case "col2": text = "\uE8A9"; break;
                case "col1": text = "\uEA37"; break;
                case "eye": text = "\uE7B3"; break;
                case "find": text = "\uE721"; break;
                case "theme": text = isDarkTheme ? "\uE706" : "\uE708"; break;   // 深色时显示太阳（点击切浅色）
            }
            if (text == null) return TextGlyph(kind == "plus" ? "A+" : "A-", c);

            Bitmap bmp = new Bitmap(20, 20);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font(IconFontName(), 11.5f, FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(c))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(text, f, b, new RectangleF(0, 0, 20, 20), sf);
                }
            }
            return bmp;
        }

        private static string iconFontCache;

        private static string IconFontName()
        {
            if (iconFontCache != null) return iconFontCache;
            iconFontCache = "Segoe Fluent Icons";
            try
            {
                using (Font f = new Font(iconFontCache, 10f))
                {
                    if (!string.Equals(f.Name, iconFontCache, StringComparison.OrdinalIgnoreCase))
                        iconFontCache = "Segoe MDL2 Assets";
                }
            }
            catch { iconFontCache = "Segoe MDL2 Assets"; }
            return iconFontCache;
        }

        private static Image TextGlyph(string text, Color c)
        {
            Bitmap bmp = new Bitmap(20, 20);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font f = new Font("Segoe UI Variable Text", 8.5f, FontStyle.Regular))
                using (SolidBrush b = new SolidBrush(c))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(text, f, b, new RectangleF(0, 0, 20, 20), sf);
                }
            }
            return bmp;
        }

        /// <summary>工具栏图标：运行时用 GDI+ 画，随主题换色，不依赖外部资源</summary>
        private static Image GlyphLegacy(string kind, Color c)
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
            try
            {
                editor.ScrollBars = editor.WordWrap ? ScrollBars.Vertical : ScrollBars.Both;
            }
            catch (Exception ex)
            {
                // 改 ScrollBars 会重建句柄，本机偶发失败；这是外观问题，不能影响其它初始化
                LogSafe("[ApplyWrap] " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private void ApplyViewMode()
        {
            split.Panel1Collapsed = viewMode == 2;
            split.Panel2Collapsed = viewMode == 1;
            miBoth.Checked = viewMode == 0;
            miEditOnly.Checked = viewMode == 1;
            miPreviewOnly.Checked = viewMode == 2;
            if (!IsHandleCreated) return;      // 句柄没建好之前绝不碰预览（否则会强制 CreateHandle 失败）
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
            ApplyGutter();
            if (previewReady) RenderPreview(true);
        }

        /// <summary>行号槽：字体比正文小 1.5pt，配色跟主题</summary>
        private void ApplyGutter()
        {
            if (gutter == null) return;
            Font oldG = gutterFont;
            gutterFont = new Font(editor.Font.FontFamily, Math.Max(6.5f, editor.Font.Size - 1.5f), FontStyle.Regular);
            gutter.SetTheme(themeEditorBg, isDarkTheme ? Color.FromArgb(133, 133, 133) : Color.FromArgb(150, 150, 150),
                            themeDivider, gutterFont);
            if (oldG != null) oldG.Dispose();
        }

        private static Font PickUiFont(float size)
        {
            string[] names = new string[] { "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Font f = new Font(names[i], size);
                    if (string.Equals(f.Name, names[i], StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font("Segoe UI", size);
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
            if (!IsHandleCreated) return;             // 窗体句柄没建好之前绝不碰 WebBrowser
            previewInitializing = true;
            previewReady = false;
            try
            {
                preview.DocumentText = MarkdownRenderer.WrapPage("<p></p>", darkPreview, fontPercent);
                previewFallback.Stop();
                previewFallback.Start();
            }
            catch (Exception ex)
            {
                previewInitializing = false;
                LogSafe("[InitPreview] " + ex.ToString());
            }
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
            string body = editor.TextLength == 0
                ? WelcomeBody()
                : MarkdownRenderer.RenderBody(editor.Text, baseDir, hardBreak);
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
            ApplyDarkScrollbars();      // IE 每次重排都会重建滚动条子窗口，渲染后补刷一次
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            debounce.Stop();
            UpdateEditorScrollbars();
            RenderPreview(false);
        }

        /// <summary>
        /// 不需要滚动时干脆不显示滚动条 —— Win32 滚动条的「禁用态」不走暗色主题，
        /// 在深色界面上会留一条刺眼的白条（空文档时最明显）。
        /// </summary>
        private void UpdateEditorScrollbars()
        {
            if (editor == null || !editor.IsHandleCreated) return;
            try
            {
                ScrollBars want = EditorNeedsScrollbar() ? ScrollBars.Vertical : ScrollBars.None;
                if (editor.ScrollBars != want) editor.ScrollBars = want;   // 会重建句柄，可能失败
            }
            catch (Exception ex)
            {
                // 本机实测：改 ScrollBars 触发的句柄重建偶尔失败（「创建窗口句柄时出错」）。
                // 这属于外观问题，绝不能让整个程序弹崩溃框 —— 保持原状即可。
                LogSafe("[UpdateEditorScrollbars] " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private bool EditorNeedsScrollbar()
        {
            if (editor.TextLength == 0) return false;
            if (editor.TextLength > 200000) return true;
            int lineH = Math.Max(1, editor.Font.Height);
            int visible = Math.Max(1, editor.ClientSize.Height / lineH);
            string[] lines = editor.Text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > visible * 3) return true;          // 粗判：行数远超可视行数
            if (!editor.WordWrap) return lines.Length > visible;
            using (Graphics g = editor.CreateGraphics())
            {
                int w = Math.Max(40, editor.ClientSize.Width - 6);
                int rows = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i];
                    if (t.Length == 0) { rows++; }
                    else
                    {
                        Size sz = TextRenderer.MeasureText(g, t, editor.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                        rows += Math.Max(1, (int)Math.Ceiling((double)sz.Width / w));
                    }
                    if (rows > visible) return true;
                }
                return rows > visible;
            }
        }

        // ================================================================ 编辑区事件

        private void OnEditorTextChanged(object sender, EventArgs e)
        {
            if (gutter != null) gutter.Invalidate();
            if (loading) return;
            SetDirty(true);
            debounce.Stop();
            debounce.Start();
            UpdateStatus();
        }

        private void OnEditorScrolled(object sender, EventArgs e)
        {
            if (gutter != null) gutter.Invalidate();
        }

        private void OnSelectionChangedLike(object sender, EventArgs e)
        {
            UpdateStatus();
            if (gutter != null) gutter.Invalidate();
        }

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
            UpdateEditorScrollbars();
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
                UpdateEditorScrollbars();
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
            // 句柄还没建好时不要碰 GetLineFromCharIndex/TextLength 之类的 API，
            // 否则会在窗体创建前强制 CreateHandle —— 实测会抛「创建窗口句柄时出错」。
            if (editor == null || !editor.IsHandleCreated) return;
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
            Safe("OnShown", delegate
            {
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
                ApplyTheme();
                ApplyViewMode();
                UpdateEditorScrollbars();
                UpdateStatus();
                if (maxConfig) WindowState = FormWindowState.Maximized;
                miStatus.Checked = statusStrip.Visible;

                // 文件打开放在最后（等窗体、预览、主题都就位），避免在 Shown 事件里提前建句柄
                if (pendingFile != null)
                {
                    string f = pendingFile;
                    pendingFile = null;
                    Safe("open-on-start", delegate { OpenFile(f, true); });
                }
                else editor.Focus();
            });
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

    /// <summary>Win11 风格的极简渲染器：透明感命令栏 + 圆角悬停高亮（默认渲染器不认 BackColor）</summary>
    internal sealed class MdPadRenderer : ToolStripProfessionalRenderer
    {
        private readonly Color bg;
        private readonly Color fg;
        private readonly bool dark;
        private readonly Color hoverFill;
        private readonly Color pressFill;

        public MdPadRenderer(Color background, Color foreground, bool isDark, Color hover, Color pressed)
            : base()
        {
            bg = background;
            fg = foreground;
            dark = isDark;
            hoverFill = hover;
            pressFill = pressed;
        }

        private static void FillRounded(Graphics g, Rectangle r, int radius, Color c)
        {
            using (System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = radius * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90);
                p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
                p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
                p.CloseFigure();
                using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(227, 227, 227)))
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
            Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            FillRounded(e.Graphics, r, 4, e.Item.Pressed ? pressFill : hoverFill);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            Rectangle r = new Rectangle(2, 2, e.Item.Width - 5, e.Item.Height - 5);
            FillRounded(e.Graphics, r, 4, e.Item.Pressed ? pressFill : hoverFill);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                int x = e.Item.Width / 2;
                using (Pen p = new Pen(dark ? Color.FromArgb(255, 255, 255, 24) : Color.FromArgb(0, 0, 0, 20)))
                    e.Graphics.DrawLine(p, x, 8, x, e.Item.Height - 8);
                return;
            }
            int y = e.Item.Height / 2;
            using (Pen p2 = new Pen(dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(205, 205, 205)))
                e.Graphics.DrawLine(p2, 3, y, e.Item.Width - 3, y);
        }
    }
}
