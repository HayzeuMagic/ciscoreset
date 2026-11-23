// Cisco Reset - the Settings window: login and device addresses.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CiscoReset
{
    public class SettingsForm : Form
    {
        readonly AppSettings s;
        readonly TextBox txtUser, txtPass, txtEnable, txtCucm, txtEdge, txtTaas, txtBaba;
        readonly CheckBox chkShow, chkName;

        public SettingsForm(AppSettings settings)
        {
            s = settings;
            Text = "Cisco Reset settings";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Header(t, "Login (the same on every device)");
            txtUser = Field(t, "Username (login local):", s.Username, false);
            txtPass = Field(t, "Telnet password:", s.Password, true);
            txtEnable = Field(t, "Enable password:", s.EnablePassword, true);
            chkShow = new CheckBox { Text = "Show passwords", AutoSize = true, Margin = new Padding(3, 2, 3, 6) };
            chkShow.CheckedChanged += (o, e) => { txtPass.UseSystemPasswordChar = txtEnable.UseSystemPasswordChar = !chkShow.Checked; };
            t.Controls.Add(new Label());
            t.Controls.Add(chkShow);
            Note(t, "The lab default is \"pass\". It is saved in CiscoReset.settings.ini, so don't use a real password here.");

            Header(t, "Device addresses   ({m} = PC number, e.g. 10.{m}.1.4 = 10.12.1.4 on PC 12)");
            txtCucm = Field(t, "CUCM (step 1):", s.CucmIp, false);
            txtEdge = Field(t, "EDGE 4321 (step 2):", s.EdgeIp, false);
            txtTaas = Field(t, "coreTAAS (step 3):", s.TaasIp, false);
            txtBaba = Field(t, "coreBABA (step 4):", s.BabaIp, false);
            Note(t, "Telnet port 23 is used. Add :port at the end to use another port.");

            Header(t, "Safety");
            chkName = new CheckBox
            {
                Text = "Don't reset a device whose name ends in another PC's number\n(for example COREbaba-21 when this is PC 12)",
                AutoSize = true, Checked = s.CheckName, Margin = new Padding(3, 2, 3, 6)
            };
            t.Controls.Add(chkName);
            t.SetColumnSpan(chkName, 2);

            var ok = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var defaults = new Button { Text = "Restore defaults", AutoSize = true };
            defaults.Click += (o, e) =>
            {
                var d = new AppSettings();
                txtUser.Text = d.Username; txtPass.Text = d.Password; txtEnable.Text = d.EnablePassword;
                txtCucm.Text = d.CucmIp; txtEdge.Text = d.EdgeIp; txtTaas.Text = d.TaasIp; txtBaba.Text = d.BabaIp;
                chkName.Checked = d.CheckName;
            };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            buttons.Controls.AddRange(new Control[] { cancel, ok, defaults });
            t.Controls.Add(buttons);
            t.SetColumnSpan(buttons, 2);

            Controls.Add(t);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        static void Header(TableLayoutPanel t, string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, 10, 3, 4) };
            t.Controls.Add(l);
            t.SetColumnSpan(l, 2);
        }

        static void Note(TableLayoutPanel t, string text)
        {
            var l = new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(470, 0), Margin = new Padding(3, 0, 3, 4) };
            t.Controls.Add(new Label { AutoSize = true });
            t.Controls.Add(l);
        }

        static TextBox Field(TableLayoutPanel t, string label, string value, bool password)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 6, 8, 0) });
            var tb = new TextBox { Text = value, Width = 260, UseSystemPasswordChar = password };
            t.Controls.Add(tb);
            return tb;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                foreach (TextBox tb in new[] { txtCucm, txtEdge, txtTaas, txtBaba })
                {
                    if (tb.Text.Trim().Length == 0)
                    {
                        MessageBox.Show(this, "Every device needs an address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        tb.Focus();
                        e.Cancel = true;
                        return;
                    }
                }
                s.Username = txtUser.Text.Trim();
                s.Password = txtPass.Text;
                s.EnablePassword = txtEnable.Text;
                s.CucmIp = txtCucm.Text.Trim();
                s.EdgeIp = txtEdge.Text.Trim();
                s.TaasIp = txtTaas.Text.Trim();
                s.BabaIp = txtBaba.Text.Trim();
                s.CheckName = chkName.Checked;
            }
            base.OnFormClosing(e);
        }
    }
}
