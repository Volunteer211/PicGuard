using System;
using System.Threading;
using System.Windows.Forms;

namespace PicGuard
{
    internal static class Program
    {
        private static Mutex _appMutex;

        /// <summary>应用入口</summary>
        [STAThread]
        private static void Main()
        {
            bool createdNew = false;
            _appMutex = new Mutex(true, "PicGuard.Main.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("PicGuard 已在运行中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            string machineId = null;
            Application.Run(new MainForm(machineId));

            try
            {
                if (_appMutex != null)
                {
                    _appMutex.ReleaseMutex();
                    _appMutex.Close();
                }
            }
            catch { }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try { PicChecker.Log("<APP> UI线程异常：" + (e.Exception == null ? "unknown" : e.Exception.ToString())); }
            catch { }
            MessageBox.Show("程序出现异常，但已记录日志。\r\n\r\n" + (e.Exception == null ? "unknown" : e.Exception.Message),
                "PicGuard 异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { PicChecker.Log("<APP> 未处理异常：" + (e.ExceptionObject == null ? "unknown" : e.ExceptionObject.ToString())); }
            catch { }
        }
    }
}
