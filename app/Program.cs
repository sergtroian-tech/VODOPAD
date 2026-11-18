using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Водопад — прошивка LED-панели")]
[assembly: System.Reflection.AssemblyProduct("Vodopad")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]

namespace Vodopad
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.ToString(), "Водопад — ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            try { Paths.ExtractAll(); }
            catch (Exception e) { MessageBox.Show("Не удалось распаковать файлы программы:\n" + e.Message, "Водопад", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            Application.Run(new MainForm());
        }
    }
}
