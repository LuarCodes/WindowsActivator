using System;
using System.Threading;
using System.Windows.Forms;

namespace WinActivationAssistant
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            using (var mutex = new Mutex(true, "WinActivationAssistant.SingleInstance", out bool first))
            {
                if (!first) return;
                Application.Run(new MainForm());
            }
        }

        static void ShowError(Exception ex)
        {
            // Only the error type/code is shown – never user input.
            string code = ex == null ? "" : " (0x" + Licensing.HResultOf(ex).ToString("X8") + ")";
            MessageBox.Show("Something unexpected went wrong" + code + ".\n" + (ex?.GetType().Name ?? ""),
                "Windows Activation Assistant", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
