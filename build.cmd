@echo off
rem ============================================================
rem  mdpad build script
rem  Usage: build.cmd [output-name]     (default: mdpad.exe)
rem  NOTE: keep this file pure ASCII -- Chinese in a .cmd is
rem        parsed as GBK by cmd.exe and can break the script.
rem  Compiler: Roslyn csc.exe (VS 2022 Build Tools) if present,
rem            otherwise the .NET Framework 4.x csc.exe (C# 5).
rem  Source is UTF-8 without BOM, so -codepage:65001 is required
rem  (otherwise Chinese UI strings become mojibake on zh-CN).
rem ============================================================
setlocal
cd /d "%~dp0"

set "OUT=%~1"
if not defined OUT set "OUT=mdpad.exe"

set "CSC="
for %%P in (
  "%ProgramFiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles%\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\Roslyn\csc.exe"
  "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
) do (
  if not defined CSC if exist %%P set "CSC=%%~P"
)
if not defined CSC set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

echo [mdpad] compiler: %CSC%
"%CSC%" -nologo -codepage:65001 -target:winexe -optimize+ -platform:anycpu ^
  -out:%OUT% ^
  -win32manifest:src\app.manifest ^
  -r:System.dll -r:System.Core.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll ^
  src\*.cs

if errorlevel 1 (
  echo [mdpad] BUILD FAILED
  exit /b 4
)
echo [mdpad] OK: %OUT%
exit /b 0
