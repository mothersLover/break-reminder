using System;
using System.Threading;
using System.Windows.Forms;

namespace BreakReminder
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "BreakReminder_SingleInstance_7f3a", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Отдыхалка уже запущена — посмотрите значок в трее.", "Отдыхалка",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool tray = Array.IndexOf(args, "--tray") >= 0; // так нас запускает автозапуск Windows
                Application.Run(new MainForm(tray));
            }
        }
    }
}
