<#
  mdpad shell 集成安装脚本
  ---------------------------------------------------------------
  做四件事（全部只动 HKCU 与用户目录，不需要管理员）：
    1. 生成图标 %LOCALAPPDATA%\mdpad\mdpad.ico
       —— 必须放在 C:（与 wscript.exe 同盘），否则桌面快捷方式图标会变白块
    2. 建快捷方式（开始菜单 + 桌面），目标指向 **微软签名的 wscript.exe** + mdpad.vbs
       —— 这是绕开 SmartScreen「发布者未知」的关键：被双击的对象不再是未签名 exe
    3. 注册右键菜单「用 mdpad 编辑（Markdown）」→ 同样走 wscript 启动器
    4. 登记「打开方式」条目（Applications\mdpad.exe）
  已存在的 .lnk 必须先删掉再建（CreateShortcut 对已有文件是「加载→改→存」，
  会留下旧结构导致图标/参数不生效）。

  用法（普通权限即可）：
    & "E:\AI\tools\powershell\pwsh.exe" -NoProfile -File E:\AI\mdpad\tools\install-shell.ps1
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'mdpad.exe'
$vbs = Join-Path $root 'mdpad.vbs'
$wscript = Join-Path $env:WINDIR 'System32\wscript.exe'
$iconDir = Join-Path $env:LOCALAPPDATA 'mdpad'
$ico = Join-Path $iconDir 'mdpad.ico'

if (-not (Test-Path $exe)) { throw "找不到 $exe（先跑 build.cmd）" }
if (-not (Test-Path $vbs)) { throw "找不到 $vbs" }

Write-Host '=== 1) 安装图标（用仓库里那份 mdpad.ico） ===' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
$repoIco = Join-Path $root 'mdpad.ico'
if (-not (Test-Path $repoIco)) {
  Write-Host '  仓库里没有 mdpad.ico，先用 tools\make-icon.ps1 生成…' -ForegroundColor Yellow
  & (Join-Path $PSScriptRoot 'make-icon.ps1') | Out-Null
}
if (-not (Test-Path $repoIco)) { throw "图标生成失败：$repoIco" }
Copy-Item -LiteralPath $repoIco -Destination $ico -Force
Write-Host "  $ico  ($((Get-Item $ico).Length) 字节，源：$repoIco)"

Write-Host '=== 2) 建快捷方式（指向 wscript.exe） ===' -ForegroundColor Cyan
$desc = 'mdpad —— 记事本式 Markdown 编辑器'
function New-Lnk([string]$path, [string]$arguments) {
  if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
  $sh = New-Object -ComObject WScript.Shell
  $lnk = $sh.CreateShortcut($path)
  $lnk.TargetPath = $wscript
  $lnk.Arguments = $arguments
  $lnk.WorkingDirectory = $root
  $lnk.IconLocation = "$ico,0"
  $lnk.Description = $desc
  $lnk.Save()
  Write-Host "  $path"
}
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\mdpad.lnk'
New-Lnk $startMenu ('"' + $vbs + '"')
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'mdpad.lnk'
New-Lnk $desktop ('"' + $vbs + '"')

Write-Host '=== 3) 右键菜单 + 打开方式（走 wscript 启动器） ===' -ForegroundColor Cyan
$cmd = '"' + $wscript + '" "' + $vbs + '" "%1"'
$menuText = '用 mdpad 编辑（Markdown）'
foreach ($ext in @('.md', '.markdown', '.mdx')) {
  $base = "HKCU:\Software\Classes\SystemFileAssociations\$ext\shell\MdPadEdit"
  New-Item -Path $base -Force | Out-Null
  New-Item -Path "$base\command" -Force | Out-Null
  Set-ItemProperty -Path $base -Name '(Default)' -Value $menuText
  Set-ItemProperty -Path $base -Name 'Icon' -Value "$ico,0"
  Set-ItemProperty -Path "$base\command" -Name '(Default)' -Value $cmd
  Write-Host "  $ext -> $menuText"
}
$app = 'HKCU:\Software\Classes\Applications\mdpad.exe'
New-Item -Path "$app\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path $app -Name 'FriendlyAppName' -Value 'mdpad（Markdown 编辑器）'
Set-ItemProperty -Path "$app\shell\open\command" -Name '(Default)' -Value $cmd
Write-Host '  Applications\mdpad.exe 已登记'

# 正式 ProgID：让「打开方式」列表里显示为「Markdown 文档 (mdpad)」，
# 并加进 .md/.markdown/.mdx 的 OpenWithProgids（右键「打开方式」子菜单可见）
$prog = 'HKCU:\Software\Classes\MdPad.Document'
New-Item -Path "$prog\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path $prog -Name '(Default)' -Value 'Markdown 文档 (mdpad)'
Set-ItemProperty -Path $prog -Name 'FriendlyTypeName' -Value 'Markdown 文档 (mdpad)'
Set-ItemProperty -Path $prog -Name 'DefaultIcon' -Value "$ico,0"
Set-ItemProperty -Path "$prog\shell\open\command" -Name '(Default)' -Value $cmd
foreach ($ext in @('.md', '.markdown', '.mdx')) {
  $k = "HKCU:\Software\Classes\$ext\OpenWithProgids"
  New-Item -Path $k -Force | Out-Null
  New-ItemProperty -Path $k -Name 'MdPad.Document' -PropertyType String -Value '' -Force | Out-Null
}
Write-Host '  MdPad.Document 已登记并加入 OpenWithProgids'

# 注册为「已注册应用」：这样「设置 → 应用 → 默认应用」里会以 **mdpad** 的名字出现，
# 并提供一个「设置默认值」按钮，一次点完（Windows 自己写 UserChoice，不需要碰哈希）
$cap = 'HKCU:\SOFTWARE\Classes\MdPad\Capabilities'
New-Item -Path "$cap\FileAssociations" -Force | Out-Null
Set-ItemProperty -Path $cap -Name 'ApplicationName' -Value 'mdpad'
Set-ItemProperty -Path $cap -Name 'ApplicationDescription' -Value '记事本式 Markdown 编辑器：单 exe、零依赖、实时预览'
Set-ItemProperty -Path "$cap\FileAssociations" -Name '.md' -Value 'MdPad.Document'
Set-ItemProperty -Path "$cap\FileAssociations" -Name '.markdown' -Value 'MdPad.Document'
Set-ItemProperty -Path "$cap\FileAssociations" -Name '.mdx' -Value 'MdPad.Document'
New-Item -Path 'HKCU:\SOFTWARE\RegisteredApplications' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\SOFTWARE\RegisteredApplications' -Name 'mdpad' -Value 'SOFTWARE\Classes\MdPad\Capabilities'
Write-Host '  已注册为「已注册应用」mdpad（含 .md/.markdown/.mdx 能力声明）'

Write-Host ''
Write-Host '注意：把 .md 的【默认程序】改成 mdpad 无法用脚本完成 ——' -ForegroundColor Yellow
Write-Host '      HKCU\...\FileExts\.md\UserChoice 有哈希保护，脚本写入会被系统忽略。' -ForegroundColor Yellow
Write-Host '      请二选一（Windows 自己写这个键）：' -ForegroundColor Yellow
Write-Host '        · 右键任意 .md → 打开方式 → 选择其他应用 → mdpad → 勾「始终」' -ForegroundColor Yellow
Write-Host '        · 设置 → 应用 → 默认应用 → 搜索 “.md” → 选 mdpad' -ForegroundColor Yellow

Write-Host ''
Write-Host '完成。验收：' -ForegroundColor Green
Write-Host "  · 双击桌面「mdpad」快捷方式 → 不应再出现 SmartScreen"
Write-Host "  · 右键任意 .md → 显示更多选项 → 「$menuText」"
Write-Host "  · 命令行验证启动器：wscript.exe `"$vbs`" `"$root\demo.md`""
