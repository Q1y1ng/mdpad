# Changelog

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 风格。

## [未发布]

## [1.1.0] - 2026-10-06

### 新增
- **工具栏**：新建 / 打开 / 保存 · 三种视图 · 查找 · 主题切换 · 字号；图标运行时用 GDI+ 画，随主题换色，不依赖外部资源
- **主题系统**：跟随系统 / 浅色 / 深色，菜单、工具栏、状态栏、查找条、编辑区、预览区统一配色
  （默认的 ToolStrip 渲染器不认 BackColor，因此实现了 `MdPadRenderer`）
- **标题栏跟随主题**：DWM `CAPTION_COLOR` / `IMMERSIVE_DARK_MODE` / `TEXT_COLOR` / `BORDER_COLOR`，
  不再被系统强调色（本机是粉色）染成彩色标题栏
- 窗口图标（运行时生成）、窗口双缓冲、编辑区内边距、默认分栏 50%

### 修复
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
