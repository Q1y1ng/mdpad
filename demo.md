# mdpad 演示笔记

这是 **mdpad** 的渲染验收样例：左边改，右边立刻变。支持 *斜体*、~~删除线~~、`行内代码`。

## 1. 列表与任务

- 一级项
  - 二级项（缩进 2 格即可）
  - 还有一个
- [x] 已完成的事
- [ ] 待办的事

1. 有序第一
2. 有序第二

> 引用：一个 exe、零依赖、不联网、不常驻。
> 第二行还是引用。

## 2. 表格与代码

| 项目 | 体积 | 说明 |
| --- | ---: | :---: |
| mdpad | 47 KB | 自建，单 exe |
| Mark Text | 216 MB | Electron |
| Obsidian | 215 MB | Electron |

```powershell
# 代码块带语言标记
& "E:\AI\tools\powershell\pwsh.exe" -NoProfile -File E:\AI\show-usb-disks.ps1
```

## 3. 链接与图片

- 链接：[OpenAI](https://openai.com)
- 裸链接自动识别：https://github.com
- 本地图片（相对路径自动解析）：

![mdpad 演示图](_tmp/mdpad-demo.png)

---

### 4. 内联 HTML 白名单

<u>下划线</u>、<mark>高亮</mark>、H<sub>2</sub>O、x<sup>2</sup>、<kbd>Ctrl</kbd>+<kbd>S</kbd>、<br>
这里靠 `<br>` 换行。

单换行默认也换行
就像这样。
