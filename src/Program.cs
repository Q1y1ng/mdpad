using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MdPad
{
    internal static class Program
    {
        private static string logPath;

        [STAThread]
        private static void Main(string[] args)
        {
            logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpad\\error.log");
            PruneOldLogs();
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            // 首次异常：任何异常一抛出就记堆栈（哪怕随后被 catch 吞掉），用来定位「创建窗口句柄」到底出在哪一步
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;

            try
            {
                EnableIe11Emulation();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args));
            }
            catch (Exception ex)
            {
                Log("Main: " + ex.ToString());
                MessageBox.Show(ex.ToString(), "mdpad 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
        }

        private static void OnFirstChance(object sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            try
            {
                string st = e.Exception.StackTrace;
                if (st == null || st.IndexOf("MdPad", StringComparison.Ordinal) < 0) return;   // 只看我们自己的代码
                Log("FirstChance: " + e.Exception.GetType().FullName + " : " + e.Exception.Message + Environment.NewLine + st);
            }
            catch { }
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Log("Thread: " + e.Exception.ToString());
            MessageBox.Show(e.Exception.Message, "mdpad 出错了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Log("Unhandled: " + (e.ExceptionObject == null ? "(null)" : e.ExceptionObject.ToString()));
        }

        /// <summary>
        /// 启动时清掉旧的诊断日志。mdpad-error-*.log 按 PID 命名、每次启动新开一个，
        /// 而 exe 常驻在部署目录（往往是仓库根/便携目录），不清理就会无限攒下去。
        /// 只删 7 天前的；本进程自己的文件（刚建）和太新的（可能是另一个还在跑的实例）不动。
        /// </summary>
        private static void PruneOldLogs()
        {
            try
            {
                string[] dirs = new string[]
                {
                    AppDomain.CurrentDomain.BaseDirectory,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mdpad")
                };
                int myPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                string mine = "mdpad-error-" + myPid + ".log";
                DateTime cutoff = DateTime.Now.AddDays(-7);
                for (int i = 0; i < dirs.Length; i++)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(dirs[i]) || !Directory.Exists(dirs[i])) continue;
                        foreach (string file in Directory.GetFiles(dirs[i], "mdpad-error-*.log"))
                        {
                            try
                            {
                                if (Path.GetFileName(file) == mine) continue;
                                if (File.GetLastWriteTime(file) >= cutoff) continue;
                                File.Delete(file);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void Log(string text)
        {
            string[] dirs = new string[]
            {
                Path.GetDirectoryName(logPath),
                AppDomain.CurrentDomain.BaseDirectory      // exe 同目录兜底（%APPDATA% 写不进去时用）
            };
            for (int i = 0; i < dirs.Length; i++)
            {
                try
                {
                    if (string.IsNullOrEmpty(dirs[i])) continue;
                    if (!Directory.Exists(dirs[i])) Directory.CreateDirectory(dirs[i]);
                    string file = Path.Combine(dirs[i], "mdpad-error-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
                    File.AppendAllText(file,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        /// <summary>
        /// WebBrowser 控件默认按 IE7 模式渲染，表格/圆角/字体都会走样。
        /// 给本程序写一条 FEATURE_BROWSER_EMULATION=11001（IE11 边缘模式），仅 HKCU，失败也不影响使用。
        /// </summary>
        private static void EnableIe11Emulation()
        {
            try
            {
                string exe = Path.GetFileName(Application.ExecutablePath);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    if (k != null) k.SetValue(exe, 11001, RegistryValueKind.DWord);
                }
                using (RegistryKey k2 = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_DISABLE_NAVIGATION_SOUNDS"))
                {
                    if (k2 != null) k2.SetValue(exe, 1, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // 忽略：拿不到就退回默认渲染模式
            }
        }
    }
}
