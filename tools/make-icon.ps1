<#
  生成 mdpad 应用图标
  ---------------------------------------------------------------
  设计：Win11 Fluent 风格 —— 圆角方形 + 竖直渐变蓝（#4CC2FF → #0A5BB5）
        + 白色「M↓」记号（Markdown 的通用识别符号），无文字、无描边。
  产物：
    mdpad.ico          多尺寸真彩 ICO（16/24/32/48/64/128，BMP 帧）
    docs\icon.png      256×256 PNG（README 用）

  ⚠️ 为什么不用 PNG 压缩帧：Explorer 桌面快捷方式的图标提取路径不认 PNG 帧
     （见 E:\AI\tools.md §7-16 ⑤a，Pillow 那次踩过），所以这里全部写成 BMP(DIB) 帧。
  用法：& "E:\AI\tools\powershell\pwsh.exe" -NoProfile -File E:\AI\mdpad\tools\make-icon.ps1
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$icoPath = Join-Path $root 'mdpad.ico'
$pngPath = Join-Path $root 'docs\icon.png'
New-Item -ItemType Directory -Path (Split-Path $pngPath) -Force | Out-Null

# ---------------------------------------------------------------- 绘制（以 256 为基准，按 s 缩放）
function Draw-MdPadIcon([System.Drawing.Graphics]$g, [int]$size) {
  $s = $size / 256.0
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

  # 圆角方形底 + 竖直渐变
  $inset = [single](8 * $s)
  $side = [single]($size - 16 * $s)
  $radius = [single](56 * $s)
  $rect = New-Object System.Drawing.RectangleF($inset, $inset, $side, $side)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $radius * 2
  $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
  $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
  $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
  $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
  $path.CloseFigure()
  $gradRect = New-Object System.Drawing.RectangleF(0, 0, [single]$size, [single]$size)
  $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $gradRect,
    [System.Drawing.Color]::FromArgb(255, 88, 190, 255),
    [System.Drawing.Color]::FromArgb(255, 9, 84, 178),
    [single]90)
  $g.FillPath($bg, $path)
  $bg.Dispose()

  # 顶部高光（很淡，制造材质感）
  $hlRect = New-Object System.Drawing.RectangleF($inset, $inset, $side, [single]($side * 0.45))
  $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $hlRect,
    [System.Drawing.Color]::FromArgb(46, 255, 255, 255),
    [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
    [single]90)
  $old = $g.Clip
  $g.SetClip($path)
  $g.FillRectangle($hl, $hlRect)
  $g.Clip = $old
  $hl.Dispose()

  # 「M↓」记号
  $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [single](23 * $s))
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $m = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF((58 * $s), (184 * $s))),
    (New-Object System.Drawing.PointF((58 * $s), (86 * $s))),
    (New-Object System.Drawing.PointF((108 * $s), (138 * $s))),
    (New-Object System.Drawing.PointF((158 * $s), (86 * $s))),
    (New-Object System.Drawing.PointF((158 * $s), (184 * $s)))
  )
  $g.DrawLines($pen, $m)
  $g.DrawLine($pen, [single](204 * $s), [single](88 * $s), [single](204 * $s), [single](154 * $s))
  $arrow = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF((178 * $s), (130 * $s))),
    (New-Object System.Drawing.PointF((204 * $s), (162 * $s))),
    (New-Object System.Drawing.PointF((230 * $s), (130 * $s)))
  )
  $g.DrawLines($pen, $arrow)
  $pen.Dispose()
  $path.Dispose()
}

function New-IconFrame([System.Drawing.Bitmap]$bmp) {
  $w = $bmp.Width; $h = $bmp.Height
  $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
  $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $stride = $data.Stride
  $px = New-Object byte[] ($stride * $h)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length)
  $bmp.UnlockBits($data)

  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2))
  $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
  $bw.Write([int]($w * $h * 4)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $stride, $w * 4) }   # XOR 位图：自下而上
  $maskStride = [int]([math]::Ceiling($w / 8.0 / 4) * 4)
  $bw.Write((New-Object byte[] ($maskStride * $h)))                            # AND 掩码全 0（用 alpha）
  $bw.Flush()
  $bytes = $ms.ToArray()
  $bw.Dispose(); $ms.Dispose()
  return , $bytes        # 逗号阻止 PowerShell 把 byte[] 展开成 Object[]（展开后帧数据就丢了）
}

# ---------------------------------------------------------------- 生成 ICO
$sizes = @(16, 24, 32, 48, 64, 128)
$frames = @()
foreach ($sz in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $sz, $sz, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  Draw-MdPadIcon $g $sz
  $g.Dispose()
  $frames += , (New-IconFrame $bmp)
  if ($sz -eq 256) { $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png) }
  $bmp.Dispose()
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ico)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $sz = $sizes[$i]
  $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))   # 宽
  $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))   # 高
  $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([int16]1); $bw.Write([int16]32)
  $bw.Write([int]$frames[$i].Length)
  $bw.Write([int]$offset)
  $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
$bw.Dispose(); $ico.Dispose()

# 另存 256 PNG 供 README 使用
$big = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.Clear([System.Drawing.Color]::Transparent)
Draw-MdPadIcon $g2 256
$g2.Dispose()
$big.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()

# ---------------------------------------------------------------- 文件类型图标（.md 在资源管理器里显示的那个）
# 形状：白页 + 右上角折角 + 蓝色「M↓」记号（与应用图标同一套视觉）
function Draw-MdPadFileIcon([System.Drawing.Graphics]$g, [int]$size) {
  $s = $size / 256.0
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

  # 页面（右上角切一刀做折角）
  $left = [single](48 * $s); $top = [single](22 * $s)
  $right = [single](208 * $s); $bottom = [single](234 * $s)
  $fold = [single](46 * $s)
  $r = [single](14 * $s)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc($left, $top, $r * 2, $r * 2, 180, 90)
  $path.AddLine(($right - $fold), $top, $right, ($top + $fold))
  $path.AddLine($right, ($top + $fold), $right, ($bottom - $r))
  $path.AddArc(($right - $r * 2), ($bottom - $r * 2), $r * 2, $r * 2, 0, 90)
  $path.AddArc($left, ($bottom - $r * 2), $r * 2, $r * 2, 90, 90)
  $path.CloseFigure()
  $pageBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.RectangleF($left, $top, ($right - $left), ($bottom - $top))),
    [System.Drawing.Color]::White, [System.Drawing.Color]::FromArgb(255, 232, 240, 250), [single]90)
  $g.FillPath($pageBrush, $path)
  $pageBrush.Dispose()
  $penBorder = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 176, 190, 206), [single](4 * $s))
  $g.DrawPath($penBorder, $path)
  $penBorder.Dispose()
  # 折角
  $foldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
  $foldPath.AddPolygon([System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF(($right - $fold), $top)),
    (New-Object System.Drawing.PointF(($right - $fold), ($top + $fold))),
    (New-Object System.Drawing.PointF($right, ($top + $fold)))
  ))
  $fb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 206, 219, 232))
  $g.FillPath($fb, $foldPath)
  $fb.Dispose(); $foldPath.Dispose()

  # 「M↓」记号（应用图标同款蓝）
  $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 12, 92, 184), [single](22 * $s))
  $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
  $g.DrawLines($pen, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF((78 * $s), (196 * $s))),
    (New-Object System.Drawing.PointF((78 * $s), (108 * $s))),
    (New-Object System.Drawing.PointF((120 * $s), (152 * $s))),
    (New-Object System.Drawing.PointF((162 * $s), (108 * $s))),
    (New-Object System.Drawing.PointF((162 * $s), (196 * $s)))
  ))
  $g.DrawLine($pen, [single](200 * $s), [single](110 * $s), [single](200 * $s), [single](168 * $s))
  $g.DrawLines($pen, [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF((178 * $s), (148 * $s))),
    (New-Object System.Drawing.PointF((200 * $s), (176 * $s))),
    (New-Object System.Drawing.PointF((222 * $s), (148 * $s)))
  ))
  $pen.Dispose()
  $path.Dispose()
}

$fileIcoPath = Join-Path $root 'mdpad-file.ico'
$filePngPath = Join-Path $root 'docs\icon-file.png'
$fileSizes = @(16, 24, 32, 48, 64, 128, 256)
$fileFrames = @()
foreach ($sz in $fileSizes) {
  $bmp = New-Object System.Drawing.Bitmap $sz, $sz, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::Transparent)
  Draw-MdPadFileIcon $g $sz
  $g.Dispose()
  $fileFrames += , (New-IconFrame $bmp)
  $bmp.Dispose()
}
$fms = New-Object System.IO.MemoryStream
$fw = New-Object System.IO.BinaryWriter($fms)
$fw.Write([int16]0); $fw.Write([int16]1); $fw.Write([int16]$fileSizes.Count)
$foff = 6 + 16 * $fileSizes.Count
for ($i = 0; $i -lt $fileSizes.Count; $i++) {
  $sz = $fileSizes[$i]
  $fw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
  $fw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
  $fw.Write([byte]0); $fw.Write([byte]0)
  $fw.Write([int16]1); $fw.Write([int16]32)
  $fw.Write([int]$fileFrames[$i].Length)
  $fw.Write([int]$foff)
  $foff += $fileFrames[$i].Length
}
foreach ($f in $fileFrames) { $fw.Write([byte[]]$f) }
$fw.Flush()
[System.IO.File]::WriteAllBytes($fileIcoPath, $fms.ToArray())
$fw.Dispose(); $fms.Dispose()
$fbig = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g3 = [System.Drawing.Graphics]::FromImage($fbig)
$g3.Clear([System.Drawing.Color]::Transparent)
Draw-MdPadFileIcon $g3 256
$g3.Dispose()
$fbig.Save($filePngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$fbig.Dispose()
"FILE: $fileIcoPath ($((Get-Item $fileIcoPath).Length) 字节, $($fileSizes.Count) 个尺寸)"
"FILE PNG: $filePngPath"

"ICO : $icoPath ($((Get-Item $icoPath).Length) 字节, $($sizes.Count) 个尺寸)"
"PNG : $pngPath ($((Get-Item $pngPath).Length) 字节)"
