' mdpad launcher
' ---------------------------------------------------------------------------
' 为什么需要它：mdpad.exe 没有代码签名，从资源管理器直接双击会触发
' SmartScreen「Windows 已保护你的电脑 / 发布者未知」。
' 自签名证书解决不了这个问题（云端信誉检查照拦），而且会引入每次启动的
' 「打开文件-安全警告」。正解：让微软签名的 wscript.exe 作为被双击的对象，
' 再用 WshShell.Exec（CreateProcess）拉起真正的 exe —— 完全绕开外壳的信誉检查。
' 注意必须用 Exec 而不是 Run：Run 走 ShellExecute，会做附件/区检查。
' ---------------------------------------------------------------------------
Option Explicit

Dim fso, sh, root, exe, cmd, i
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")

root = fso.GetParentFolderName(WScript.ScriptFullName)
exe = fso.BuildPath(root, "mdpad.exe")

If Not fso.FileExists(exe) Then
  MsgBox "mdpad.exe not found next to this launcher:" & vbCrLf & exe, 16, "mdpad"
  WScript.Quit 1
End If

cmd = """" & exe & """"
For i = 0 To WScript.Arguments.Count - 1
  cmd = cmd & " """ & WScript.Arguments(i) & """"
Next

sh.Exec cmd
