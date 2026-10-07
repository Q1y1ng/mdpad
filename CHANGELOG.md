# Changelog

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格。

## [未发布]

### 修复（2026-10-07 全库体检发现）
- **无 BOM 的 UTF-16 文件被按 UTF-8 误读**（最重的一条，会损坏文件）：NUL 是合法 UTF-8（U+0000），
  严格 UTF-8 解码不报错 → LE/BE 无 BOM 的 UTF-16 落进 UTF-8/GBK 分支显示成 NUL 乱码，
  用户随手 Ctrl+S 就把原文件**永久重写**成错误编码。现在 BOM 缺失时先探 NUL 节奏
  （偶数位=BE / 奇数位=LE，看前 128 字节）再落回原判定链。
- **保存改为原子写入**：先写 `target+".tmp"` 再 `File.Replace`/`File.Move`，
  中途崩溃/断电不再留下半截文件。
- **force-relayout 不再破坏「仅预览」**：句柄恢复的折叠-展开强排会把左栏展开，
  `ViewMode=2`（仅预览）时恰好把 OnShown 刚收起的左栏又打开 —— 强排后按配置 `ApplyViewMode()` 归位。

### 新增
- **诊断日志自动清理**（`Program.PruneOldLogs`）：`mdpad-error-*.log` 按 PID 命名、每次启动新开一个，
  而 exe 常驻部署目录（本仓库根），不清理会无限积累（今天已经攒了 5 份内容相同的记录）。
  启动时删掉两个日志目录（exe 目录 + `%APPDATA%\mdpad`）里 **7 天前**的旧日志；
  本进程自己的文件和 7 天内的新文件不动。
- **左侧「原始文档」栏可折叠**（纯读 Markdown 时把编辑区收掉，不挡地方）：
  - 分栏边上新增**折叠按钮**（`src\CollapseButton.cs`，自绘控件，悬停淡灰高亮；折叠后箭头朝右，展开时朝左）
  - 菜单 `查看 → 折叠 / 展开左侧原始栏` + 快捷键 **F9** + 工具栏按钮
  - 与原有 `Ctrl+3 仅预览` 是同一套状态（`ViewMode`），状态会被记住

### 修复
- **首次打开左侧一片空白**（要手动「折叠一次再展开」才显示源码）：编辑框句柄走过「首次创建失败 → 稍后重试成功」这条路径时，
  左侧面板会停在未绘制状态（内部状态完全正常，就是屏幕不画）→ 启动时自动补一次「折叠→展开 + 强制重排」
  （`editorHandleRetried` 标记 + `force-relayout`）
- **改字号只影响左边原始栏，右边渲染栏不变**：预览正文更新走的是页内 `mdSetContent`（只替换正文、保留滚动位置），
  **改不了 CSS** → 字号 / 深色预览 / 单换行这类需要改样式的变化改为**整页重写**（新增 `RenderPreviewFull`，
  `RenderPreview(true)` 走整页、`RenderPreview(false)` 走页内快速更新）
- **悬停高亮是亮黄色**：`Color.FromArgb(255, 255, 255, 18)` 的 alpha 写在了第一个参数 → 实际是 A=255 的**不透明黄**（本意是 alpha 18 的淡白）。
  修正为 `Color.FromArgb(18, 255, 255, 255)`（深色）/ `Color.FromArgb(14, 0, 0, 0)`（浅色），影响折叠按钮、工具栏、菜单悬停与竖向分隔线
- 折叠按钮原本用 Win32 `Button`，深色模式下会被系统主题接管（`FlatStyle.Flat` + `UseVisualStyleBackColor` 的坑）→ 改自绘控件
- `ApplyDarkScrollbars` 不再给 EDIT 控件本身套 `DarkMode_Explorer`（只刷滚动条子窗口），且 `SetWindowTheme` 的 subIdList 按标准用法传 `null`

### 变更
- `tools\install-shell.ps1` 额外登记正式 ProgID **`MdPad.Document`**（在「打开方式」里显示为「Markdown 文档 (mdpad)」），
  并加入 `.md/.markdown/.mdx` 的 `OpenWithProgids`（右键「打开方式」子菜单可见）；再注册为**已注册应用**
  （`RegisteredApplications` + `Capabilities`），这样「设置 → 应用 → 默认应用」里以 **mdpad** 出现并可一键设为默认
- 脚本末尾明确提示：**把 `.md` 的默认程序改成 mdpad 无法脚本化** —— `FileExts\.md\UserChoice` 有哈希保护，
  脚本写入会被系统忽略；只能由 Windows 自己写（「打开方式 → 始终」或「设置 → 默认应用」）

## [1.2.1] - 2026-10-07

### 变更
- **重画应用图标**：Fluent 风格圆角方形 + 竖直渐变蓝 + 白色「M↓」记号；`tools\make-icon.ps1` 生成
  16/24/32/48/64/128 六档**真彩 BMP 帧** ICO（约 100 KB）；运行时优先加载该 ico，失败才回退到运行时绘制
- `tools\install-shell.ps1` 改为安装仓库里那份 `mdpad.ico`（不再各自画一份，保证图标一致）
- **README 重写**：截图、特性表、快速开始（便携 / 装进系统 / 设为默认程序）、SmartScreen 与 wscript 启动链、
  快捷键表、语法支持范围、构建说明、目录结构、已知边界、开发踩坑清单
- 新增 **MIT LICENSE**

## [1.2.0] - 2026-10-07

### 新增
- **Win11 原生质感**：标题栏走 DWM Mica 材质（`SYSTEMBACKDROP_TYPE=2`）+ 沉浸式深色标题栏（不再自定标题栏颜色，交还系统原生绘制）；命令栏/状态栏用 Win11 原生配色（浅 `#F3F3F3` / 深 `#202020`）；界面字体 Segoe UI Variable Text
- **工具栏改用 Segoe Fluent Icons**（系统原生图标字体，随主题换色），按钮 40×32、悬停圆角高亮 4px、竖向细分隔线
- **行号槽**：`EditorBox`（拦 `WM_VSCROLL`/`WM_MOUSEWHEEL`/翻页键发出滚动通知）+ `GutterPanel`（按逻辑行编号，自动换行折行不另编号）
- **空文档引导页**：快捷键速查表，不再是纯黑空白
- **`mdpad.vbs` 启动器 + `tools\install-shell.ps1`**：快捷方式与右键菜单都指向**微软签名的 wscript.exe**（`WshShell.Exec` → CreateProcess），**彻底绕开 SmartScreen「发布者未知」，无需任何代码签名**
- 开始菜单 + 桌面快捷方式（图标必须放 `%LOCALAPPDATA%\mdpad\mdpad.ico`，与 wscript.exe 同盘，否则图标变白块）

### 修复
- **启动即崩「创建窗口句柄时出错」**（本机 build 22631 实测，100% 复现）：句柄会在窗体首次显示时的 `WM_SHOWWINDOW → CreateControl` 递归里创建，而那一步创建 EDIT 控件会失败，且 `OnLoad` 都来不及跑
  → 构造函数末尾 `ForceHandles()` 递归强制建句柄（注意 `Control.CreateControl()` 对**不可见**子控件是跳过的，必须直接访问 `Handle`）；所有外观类操作（`ScrollBars` 重建句柄、uxtheme 主题、预览初始化、OnShown 全体）一律包 try/catch，失败只记日志、不弹崩溃框
- **不要用 uxtheme 的未公开序号**（`#135 SetPreferredAppMode` / `#133 AllowDarkModeForWindow`）：序号随 Windows 版本漂移，本机调用后 EDIT 控件再也建不出句柄。滚动条暗色只用公开 API `SetWindowTheme(hwnd, "DarkMode_Explorer", null)`（IE 的滚动条是 MSHTML 的子窗口，需枚举 `ScrollBar` 类补刷）
- 日志改为按进程号分文件、同时写 exe 同目录（崩溃实例的模态框会锁住日志文件；沙箱下 `%APPDATA%` 也可能写不进）
- 空文档时禁用态滚动条仍是白色 → 不需要滚动时干脆不显示滚动条（`EditorNeedsScrollbar()`）

## [1.1.0] - 2026-10-06

### 新增
- **工具栏**：新建 / 打开 / 保存 · 三种视图 · 查找 · 主题切换 · 字号；图标运行时用 GDI+ 画，随主题换色，不依赖外部资源
- **主题系统**：跟随系统 / 浅色 / 深色，菜单、工具栏、状态栏、查找条、编辑区、预览区统一配色
  （默认的 ToolStrip 渲染器不认 BackColor，因此实现了 `MdPadRenderer`）
- **标题栏跟随主题**：DWM `CAPTION_COLOR` / `IMMERSIVE_DARK_MODE` / `TEXT_COLOR` / `BORDER_COLOR`，
  不再被系统强调色（本机是粉色）染成彩色标题栏
- 窗口图标（运行时生成）、窗口双缓冲、编辑区内边距、默认分栏 50%

### 修复
- **深色模式下滚动条仍是白色**（编辑框与 IE 预览）：Win32/IE 经典滚动条不跟应用配色
  → `SetWindowTheme(hwnd,"DarkMode_Explorer",null)` + uxtheme 未公开序号
  `#135 SetPreferredAppMode` / `#133 AllowDarkModeForWindow`，并枚举 IE 的 `ScrollBar` 子窗口在每次渲染后补刷
- **主题切换后预览窗空白**：同一时刻重复设置 `WebBrowser.DocumentText` 会让 IE 不触发 `DocumentCompleted`
  → 加导航幂等守卫 + 1.5 s 兜底定时器
- **保存带 BOM 的文件会丢 BOM**：`Encoding.GetBytes` 不含前导码，改为显式写入 `GetPreamble()`
- `TextBox` 没有 `SelectionChanged` / `Find`，查找改用 `string.IndexOf` 实现

## [1.0.0] - 2026-10-06

### 新增
- 首版：记事本式 Markdown 阅读/编辑器，单 exe、零依赖、不联网
- 左编辑 / 右预览分栏，可拖动分隔条，实时渲染（380 ms 防抖）
- 视图三态：编辑+预览 / 仅编辑 / 仅预览（Ctrl+1/2/3）
- Markdown 渲染：标题（`#` 与 `===`）、粗体、斜体、删除线、行内代码、围栏代码块、
  引用、有序/无序列表（嵌套）、任务列表、表格（含对齐）、分割线、链接、图片（本地相对路径解析）、
  裸链接自动识别、内联 HTML 白名单（`<br> <u> <b> <i> <sup> <sub> <mark> <kbd>` 等）
- 文件能力：新建/打开/保存/另存为、拖放打开、命令行参数打开（可关联 .md）、
  编码自动识别（UTF-8 BOM / UTF-8 / GBK）、换行风格（CRLF/LF）保持、外部修改检测与重载
- 编辑辅助：查找/替换（含全部替换、区分大小写）、Tab 多行缩进、格式菜单（标题/列表/任务/表格/代码块…）
- 状态栏：路径、编码与换行、行列、字符数、视图模式
- 预览外部链接改用系统浏览器打开；深色预览主题；字号缩放
- 配置持久化到 `%APPDATA%\mdpad\config.ini`（字号、视图、分隔条位置、窗口尺寸、最近文件）
- 启动时写 `FEATURE_BROWSER_EMULATION=11001`（仅 HKCU），让 WebBrowser 按 IE11 模式渲染
