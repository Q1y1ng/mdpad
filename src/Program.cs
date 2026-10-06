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
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

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

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Log("Thread: " + e.Exception.ToString());
            MessageBox.Show(e.Exception.Message, "mdpad 出错了", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            Log("Unhandled: " + (e.ExceptionObject == null ? "(null)" : e.ExceptionObject.ToString()));
        }

        private static void Log(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
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
