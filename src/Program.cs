// Cisco Reset - entry point.
using System;
using System.Windows.Forms;

namespace CiscoReset
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.Message, "Cisco Reset - error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
        }
    }
}
