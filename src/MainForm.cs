// Cisco Reset - main window. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CiscoReset
{
    public class MainForm : Form
    {
        readonly AppSettings settings;
        readonly List<DeviceTile> tiles = new List<DeviceTile>();
        readonly List<Control> busyDisabled = new List<Control>();
        PcNumberInfo detected = new PcNumberInfo();

        NumericUpDown numPc;
        Label lblPcSource, lblStatus;
        FlowLayoutPanel tilePanel;
        Button btnStop;
        ProgressBar progress;
        TextBox txtLog;
        SplitContainer splitMain;
        CancellationTokenSource cts;
        bool busy, loadingPc;

        public MainForm()
        {
            Logger.Dir = Paths.LogDir;
            settings = AppSettings.Load(Paths.SettingsFile);
            BuildUi();
            Logger.Line += OnLogLine;
        }

        // ================================================================== devices
        // The reset order matters: this PC reaches CUCM through coreBABA, so coreBABA goes last.
        List<Device> MakeDevices()
        {
            return new List<Device>
            {
                new Device { Key = "cucm", Name = "CUCM", Role = "Call manager (CME router)", Kind = DeviceKind.Router, Pattern = settings.CucmIp },
                new Device { Key = "taas", Name = "coreTAAS", Role = "Backup core switch", Kind = DeviceKind.Switch, Pattern = settings.TaasIp },
                new Device { Key = "baba", Name = "coreBABA", Role = "Main core switch", Kind = DeviceKind.Switch, Pattern = settings.BabaIp },
            };
        }

        // ================================================================== UI construction
        static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 2, 0, 2) };
            f.Controls.AddRange(controls);
            return f;
        }

        static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
        }

        Button Btn(string text, EventHandler click, bool disableWhenBusy = true)
        {
            var b = new Button { Text = text, AutoSize = true, Padding = new Padding(4, 1, 4, 1) };
            b.Click += click;
            if (disableWhenBusy) busyDisabled.Add(b);
            return b;
        }

        static void Stack(Control parent, Control fill, Control[] tops, Control[] bottoms)
        {
            if (fill != null) { fill.Dock = DockStyle.Fill; parent.Controls.Add(fill); }
            if (tops != null) for (int i = tops.Length - 1; i >= 0; i--) { tops[i].Dock = DockStyle.Top; parent.Controls.Add(tops[i]); }
            if (bottoms != null) for (int i = bottoms.Length - 1; i >= 0; i--) { bottoms[i].Dock = DockStyle.Bottom; parent.Controls.Add(bottoms[i]); }
        }

        static GroupBox Group(string title, Control fill, Control[] tops, Control[] bottoms)
        {
            var g = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
            Stack(g, fill, tops, bottoms);
            return g;
        }

        void BuildUi()
        {
            Text = "Cisco Reset  -  " + Environment.MachineName;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(720, 760);
            MinimumSize = new Size(720, 600);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var bold = new Font(Font, FontStyle.Bold);

            // ---- 1. This PC
            numPc = new NumericUpDown { Minimum = 0, Maximum = 254, Width = 60, Margin = new Padding(3, 4, 3, 0), Font = bold };
            numPc.ValueChanged += (s, e) => PcNumberChanged();
            busyDisabled.Add(numPc);
            lblPcSource = new Label { AutoSize = true, Margin = new Padding(6, 7, 3, 0), ForeColor = SystemColors.GrayText };
            var pcRow = Row(Lbl("PC number:"), numPc, lblPcSource);
            var pcButtons = Row(Btn("Detect from Wi-Fi", (s, e) => DetectPc(true)), Btn("Settings...", Settings_Click));
            var gPc = new GroupBox { Text = "1. This PC", Height = 92, Padding = new Padding(8, 4, 8, 4) };
            Stack(gPc, null, new Control[] { pcRow, pcButtons }, null);

            // ---- 2. Devices
            tilePanel = new FlowLayoutPanel { AutoScroll = true, BackColor = Color.FromArgb(244, 246, 249), Padding = new Padding(6) };
            int step = 1;
            foreach (Device d in MakeDevices())
            {
                var t = new DeviceTile(d, step++);
                t.Ticked = d.Key == "cucm" ? settings.TickCucm : d.Key == "taas" ? settings.TickTaas : settings.TickBaba;
                t.TickToggled += (s, e) => { var tile = (DeviceTile)s; tile.Ticked = !tile.Ticked; SaveTicks(); };
                tiles.Add(t);
                tilePanel.Controls.Add(t);
            }
            var tileHint = new Label { Height = 20, AutoEllipsis = true, ForeColor = SystemColors.GrayText,
                Text = "Click a device to tick or untick it.   Blue = working,  green = done,  red = problem,  gray = not reachable." };
            var tileButtons = Row(Btn("Test connection", Test_Click),
                                  Btn("Tick all", (s, e) => SetAllTicked(true)), Btn("Tick none", (s, e) => SetAllTicked(false)));
            var gTiles = Group("2. Cisco devices   (tick the ones to reset)", tilePanel, new Control[] { tileHint }, new Control[] { tileButtons });

            // ---- 3. Reset
            var btnReset = Btn("Reset ticked devices...", Reset_Click);
            btnReset.Font = bold;
            btnReset.ForeColor = Color.DarkRed;
            btnStop = Btn("Stop", (s, e) => { if (cts != null) { cts.Cancel(); lblStatus.Text = "Stopping..."; } }, false);
            btnStop.Enabled = false;
            var gReset = new GroupBox { Text = "3. Reset", Height = 96, Padding = new Padding(8, 4, 8, 4) };
            Stack(gReset, null, new Control[] {
                Row(Lbl("Types:  write erase  >  delete vlan.dat (switches only)  >  reload  -  in order CUCM, coreTAAS, coreBABA.")),
                Row(btnReset, btnStop) }, null);

            // ---- status
            progress = new ProgressBar { Height = 16 };
            lblStatus = new Label { Height = 22, AutoEllipsis = true, Text = "Ready.", Padding = new Padding(0, 4, 0, 0) };
            var statusPanel = new Panel { Height = 44, Padding = new Padding(3, 4, 3, 0) };
            Stack(statusPanel, null, new Control[] { progress, lblStatus }, null);

            // ---- log
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = SystemColors.Window, Font = new Font("Consolas", 9f) };
            var logButtons = Row(Btn("Open log folder", (s, e) => OpenFolder(Logger.Dir), false), Btn("Clear", (s, e) => txtLog.Clear(), false));
            var gLog = Group("Log", txtLog, null, new Control[] { logButtons });

            // ---- layout
            splitMain = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Padding = new Padding(4) };
            Stack(splitMain.Panel1, gTiles, new Control[] { gPc }, new Control[] { statusPanel, gReset });
            splitMain.Panel2.Controls.Add(gLog);
            Controls.Add(splitMain);
        }

        bool splitterSet;

        // Size the log once the window has its real size (not while it starts minimized).
        void ApplySplitter()
        {
            if (splitterSet || WindowState == FormWindowState.Minimized || splitMain.Height < 300) return;
            try { splitMain.SplitterDistance = Math.Max(420, splitMain.Height - 200); splitterSet = true; } catch { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            splitMain.FixedPanel = FixedPanel.Panel2;   // when the window grows, the devices get the space
            ApplySplitter();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (splitMain != null) ApplySplitter();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Logger.Write("Cisco Reset started on " + Environment.MachineName + ".");
            DetectPc(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                if (MessageBox.Show(this, "A reset is still running. Stop it and close Cisco Reset?", "Cisco Reset",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                if (cts != null) cts.Cancel();
            }
            base.OnFormClosing(e);
        }

        // ================================================================== log
        void OnLogLine(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(OnLogLine), line); } catch { } return; }
            if (txtLog.TextLength > 400000) txtLog.Text = txtLog.Text.Substring(200000);
            txtLog.AppendText(line + Environment.NewLine);
        }

        static void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Cisco Reset"); }
        }

        // ================================================================== PC number
        int CurrentPc() { return (int)numPc.Value; }

        void DetectPc(bool userClicked)
        {
            detected = PcNumber.Detect();
            if (userClicked)
            {
                settings.PcNumber = 0;   // follow the Wi-Fi IP again
                settings.Save(Paths.SettingsFile);
            }
            loadingPc = true;
            numPc.Value = settings.PcNumber > 0 ? settings.PcNumber : detected.Number;
            loadingPc = false;
            ShowPcSource();
            UpdateAddresses();
            if (detected.Number > 0)
                Logger.Write("Wi-Fi IP " + detected.Ip + " (" + detected.Adapter + ") = PC " + detected.Number + ".");
            else
                Logger.Write("No Wi-Fi IP found. Type this PC's number in '1. This PC'.");
            if (settings.PcNumber > 0 && settings.PcNumber != detected.Number)
                Logger.Write("Using PC " + settings.PcNumber + " (typed in by hand). Click 'Detect from Wi-Fi' to use the Wi-Fi IP instead.");
            if (userClicked && detected.Number == 0)
                MessageBox.Show(this, "No Wi-Fi IP address was found on this PC.\n\nType the PC number in the box instead.", "Cisco Reset",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void PcNumberChanged()
        {
            if (loadingPc) return;
            int n = CurrentPc();
            settings.PcNumber = n == detected.Number ? 0 : n;
            settings.Save(Paths.SettingsFile);
            ShowPcSource();
            UpdateAddresses();
        }

        void ShowPcSource()
        {
            int n = CurrentPc();
            if (n == 0) { lblPcSource.Text = "Unknown - no Wi-Fi IP found. Type the PC number."; lblPcSource.ForeColor = Color.Firebrick; return; }
            lblPcSource.ForeColor = SystemColors.GrayText;
            if (n == detected.Number) lblPcSource.Text = "from Wi-Fi IP " + detected.Ip + "  (last 2 digits)";
            else lblPcSource.Text = "typed in by hand" + (detected.Number > 0 ? "  (Wi-Fi IP " + detected.Ip + " says PC " + detected.Number + ")" : "");
        }

        void UpdateAddresses()
        {
            int n = CurrentPc();
            foreach (DeviceTile t in tiles)
            {
                t.Address = n > 0 ? t.Device.Address(n) : t.Device.Pattern;
                if (!busy) t.SetState(TileState.Idle, 0, "Ready");
            }
        }

        // ================================================================== ticks & settings
        void SetAllTicked(bool on)
        {
            foreach (DeviceTile t in tiles) t.Ticked = on;
            SaveTicks();
        }

        void SaveTicks()
        {
            foreach (DeviceTile t in tiles)
            {
                if (t.Device.Key == "cucm") settings.TickCucm = t.Ticked;
                else if (t.Device.Key == "taas") settings.TickTaas = t.Ticked;
                else settings.TickBaba = t.Ticked;
            }
            settings.Save(Paths.SettingsFile);
        }

        void Settings_Click(object sender, EventArgs e)
        {
            using (var f = new SettingsForm(settings))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            settings.Save(Paths.SettingsFile);
            foreach (DeviceTile t in tiles)
                t.Device.Pattern = t.Device.Key == "cucm" ? settings.CucmIp : t.Device.Key == "taas" ? settings.TaasIp : settings.BabaIp;
            UpdateAddresses();
            Logger.Write("Settings saved. Login user '" + settings.Username + "'. Addresses: CUCM " + settings.CucmIp +
                         ", coreTAAS " + settings.TaasIp + ", coreBABA " + settings.BabaIp + ".");
        }

        // ================================================================== busy state
        void SetBusy(bool on)
        {
            busy = on;
            foreach (Control c in busyDisabled) c.Enabled = !on;
            foreach (DeviceTile t in tiles) t.Clickable = !on;
            btnStop.Enabled = on;
            UseWaitCursor = false;
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch { }
        }

        // ================================================================== test connection
        async void Test_Click(object sender, EventArgs e)
        {
            int m = CurrentPc();
            if (m <= 0) { AskForPcNumber(); return; }
            SetBusy(true);
            lblStatus.Text = "Testing the Telnet connection to each device...";
            progress.Style = ProgressBarStyle.Marquee;
            foreach (DeviceTile t in tiles) t.SetState(TileState.Working, 0, "Testing...");
            var jobs = tiles.Select(t => Task.Run(() =>
            {
                string address = t.Device.Address(m), host;
                int port;
                Device.SplitHostPort(address, out host, out port);
                bool ok = TelnetSession.CanConnect(host, port, 3000);
                Ui(() => t.SetState(ok ? TileState.Done : TileState.Skipped, 100, ok ? "Reachable (Telnet)" : "Not reachable"));
                Logger.Write(t.Device.Name + " " + address + ": " + (ok ? "reachable (Telnet port " + port + " answers)." : "NOT reachable."));
                return ok;
            })).ToArray();
            bool[] results = await Task.WhenAll(jobs);
            progress.Style = ProgressBarStyle.Blocks;
            progress.Value = 0;
            int ok2 = results.Count(r => r);
            lblStatus.Text = ok2 + " of " + results.Length + " devices reachable.";
            if (ok2 < results.Length && !results[0] && results[2])
                Logger.Write("Tip: if CUCM is not reachable but coreBABA is, this PC may be missing its route (route add 10.0.0.0 mask 255.0.0.0 10." + m + ".1.4) or CUCM its default route.");
            SetBusy(false);
        }

        void AskForPcNumber()
        {
            MessageBox.Show(this, "This PC's number is unknown.\n\nType it in '1. This PC' (the last 2 digits of the Wi-Fi IP).",
                            "Cisco Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
            numPc.Focus();
        }

        // ================================================================== reset
        async void Reset_Click(object sender, EventArgs e)
        {
            int m = CurrentPc();
            if (m <= 0) { AskForPcNumber(); return; }
            List<DeviceTile> todo = tiles.Where(t => t.Ticked).ToList();   // already in reset order
            if (todo.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one device first.", "Cisco Reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Reset these devices for PC " + m + "?");
            sb.AppendLine();
            int i = 1;
            foreach (DeviceTile t in todo)
            {
                sb.AppendLine("   " + (i++) + ".  " + t.Device.Name + "   " + t.Device.Address(m));
                sb.AppendLine("         " + (t.Device.Kind == DeviceKind.Switch ? "write erase, delete vlan.dat, reload" : "write erase, reload"));
            }
            sb.AppendLine();
            sb.AppendLine("The saved configuration is erased and the devices restart. This can't be undone.");
            sb.AppendLine();
            sb.AppendLine("Afterwards Telnet won't work on them until they are set up again with the console cable.");
            if (MessageBox.Show(this, sb.ToString(), "Confirm reset", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            SetBusy(true);
            progress.Value = 0;
            foreach (DeviceTile t in tiles) t.SetState(t.Ticked ? TileState.Waiting : TileState.Idle, 0, t.Ticked ? "Waiting..." : "Not ticked");
            Credentials cred = settings.Credentials;
            bool checkName = settings.CheckName;
            Logger.Write("==== Reset for PC " + m + ": " + string.Join(", ", todo.Select(t => t.Device.Name + " " + t.Device.Address(m))));

            int okCount = 0, failCount = 0;
            await Task.Run(() =>
            {
                for (int n = 0; n < todo.Count; n++)
                {
                    DeviceTile t = todo[n];
                    int index = n;
                    string address = t.Device.Address(m);
                    if (ct.IsCancellationRequested)
                    {
                        Ui(() => t.SetState(TileState.Skipped, 100, "Stopped. Nothing was erased."));
                        continue;
                    }
                    Logger.Write("---- " + t.Device.Name + " " + address);
                    Ui(() => { t.SetState(TileState.Working, 0, "Connecting..."); lblStatus.Text = "Resetting " + t.Device.Name + " (" + address + ")..."; });
                    ResetResult r = Resetter.Run(t.Device, address, m, cred, checkName,
                        (pct, text) => Ui(() =>
                        {
                            t.SetState(TileState.Working, pct, text);
                            progress.Value = Math.Min(100, (index * 100 + pct) / todo.Count);
                            lblStatus.Text = t.Device.Name + ": " + text;
                        }),
                        line => Logger.Write("[" + t.Device.Name + "] " + line), ct);
                    if (r.Ok) okCount++; else failCount++;
                    Logger.Write((r.Ok ? "OK  " : "PROBLEM  ") + t.Device.Name + ": " + r.Message);
                    Ui(() => t.SetState(r.Ok ? TileState.Done : TileState.Error, 100, r.Message));
                }
            });

            progress.Value = 100;
            string summary = ct.IsCancellationRequested ? "Stopped. " : "Finished. ";
            summary += okCount + " reset" + (failCount > 0 ? ", " + failCount + " with a problem - see the red tiles and the log." : ".");
            lblStatus.Text = summary;
            Logger.Write("==== " + summary);
            cts = null;
            SetBusy(false);
        }
    }
}
