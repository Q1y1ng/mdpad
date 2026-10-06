# mdpad

**记事本式的 Markdown 阅读 / 编辑器：左边改，右边即时渲染。**
单个 exe（约 **57 KB**）、零依赖（.NET Framework 4.x + 系统自带 WebBrowser 控件）、不联网、不常驻。

生成物：`mdpad.exe`（57 KB）、`src\` 三个 .cs 共约 1600 行、`build.cmd`。

## 为什么有它

- Windows 11 自带记事本的 Markdown 支持是「语法视图」，**不能用来改 Markdown**（本轮实测确认）；
- 本机现成的替代品都不轻：Mark Text / Obsidian 都是 Electron，单 exe 225 MB 起；
- 于是自建一个「和记事本一样小、一样快，但能改 Markdown 并即时看到渲染」的东西。

## 界面

- **Win11 原生质感**：标题栏 Mica 材质 + 沉浸式深色、命令栏/状态栏用系统原生配色、字体 Segoe UI Variable Text
- 工具栏图标用系统自带 **Segoe Fluent Icons**；编辑区带**行号槽**；空文档显示快捷键引导页
- **主题**：`查看 → 主题`（跟随系统 / 浅色 / 深色），菜单、工具栏、状态栏、编辑区、预览、**滚动条**一起换
- 左编辑右预览，分隔条可拖动（默认 50%），位置会记住

## 启动方式（为什么有 .vbs）

`mdpad.exe` 没有代码签名，**从资源管理器直接双击会弹 SmartScreen「Windows 已保护你的电脑 / 发布者未知」**。
自签名证书解决不了这个问题（云端信誉检查照拦），而且会引入每次启动的「打开文件-安全警告」。
所以提供 `mdpad.vbs` 启动器 + `tools\install-shell.ps1`：让**微软签名的 wscript.exe** 作为被双击的对象，
再用 `WshShell.Exec`（CreateProcess）拉起真正的 exe —— 完全绕开外壳的信誉检查，**零签名、零安装**。

```
# 一次性安装 shell 集成（快捷方式 + 右键菜单 + 图标，只动 HKCU，不需要管理员）
& "E:\AI\tools\powershell\pwsh.exe" -NoProfile -File E:\AI\mdpad\tools\install-shell.ps1
```

装好后：桌面/开始菜单「mdpad」、右键任意 `.md` →「用 mdpad 编辑（Markdown）」，都不再弹 SmartScreen。

## 用法

```
mdpad.exe                 # 空窗口
mdpad.exe 笔记.md          # 直接打开（可关联 .md 双击）
```

菜单：**文件 / 编辑 / 查看 / 格式 / 帮助**，快捷键：

| 快捷键 | 作用 |
| --- | --- |
| `Ctrl+N / O / S / Shift+S` | 新建 / 打开 / 保存 / 另存为 |
| `Ctrl+B / I / K / E` | 粗体 / 斜体 / 链接 / 行内代码 |
| `Ctrl+F / H` | 查找 / 替换（含全部替换） |
| `Ctrl+1 / 2 / 3` | 编辑+预览 / 仅编辑 / 仅预览 |
| `Ctrl+加号 / 减号 / 0` | 放大 / 缩小 / 重置字号 |
| `Tab / Shift+Tab` | 缩进 / 反缩进（多行选择一起缩） |
| `F5` | 重新载入（外部改过时用） |

- 编辑区与预览区之间的分隔条可拖动，位置会记住
- 拖 .md 文件到窗口即可打开；`查看 → 深色预览` 切预览主题
- 外部程序改了同一个文件时会提示重新载入
- 配置写在 `%APPDATA%\mdpad\config.ini`（字号、视图、字数、最近文件…）

## 支持的 Markdown 语法

标题（`#` 与 `===` 两种）、粗体、斜体、删除线、行内代码、围栏代码块（带语言标记）、
引用、有序/无序列表（可嵌套）、任务列表、表格（含对齐）、分割线、链接、图片（本地相对路径自动解析）、
裸链接自动识别，以及一小撮内联 HTML 白名单（`<br> <u> <b> <i> <sup> <sub> <mark> <kbd>` 等）。
单换行默认渲染成换行（`查看 → 保留单换行` 可关）。

## 构建

```
build.cmd
```

用 Roslyn `csc.exe`（有 VS 2022 / Build Tools 就用它，否则退回 .NET Framework 的 csc）编译 `src\*.cs`。
源码是 **UTF-8 无 BOM**，所以必须带 `-codepage:65001`，否则中文界面会变乱码。

## 已知边界

- 预览用的是系统 WebBrowser（IE 内核），复杂 CSS / ES6 不支持；Mermaid、数学公式不渲染 —— 要看这些请用 QuickLook（空格预览）或 Mark Text
- 超大文件（>10 MB）预览会变慢 —— 建议 `Ctrl+2` 仅编辑模式
- 不做多标签页、不做文件树（保持"记事本"定位）
