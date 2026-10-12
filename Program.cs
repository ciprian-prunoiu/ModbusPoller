using System;
using System.Windows.Forms;


namespace ModbusPoller
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
            SQLitePCL.raw.FreezeProvider(true);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
