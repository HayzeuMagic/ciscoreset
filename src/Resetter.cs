// Cisco Reset - logs in to one device over Telnet and types:
//   enable, write erase, delete vlan.dat (switches only), reload
// waiting for the right prompt before every answer.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CiscoReset
{
    public class Credentials
    {
        public string Username = "admin";
        public string Password = "pass";
        public string EnablePassword = "pass";
    }

    public class ResetResult
    {
        public bool Ok;
        public string Message = "";
        public string Hostname = "";
    }

    class ResetFailed : Exception
    {
        public ResetFailed(string msg) : base(msg) { }
    }

    public static class Resetter
    {
        const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // A prompt is the last line of the text, e.g. "COREbaba-12>" or "COREbaba-12#".
        static readonly Regex RxUserPrompt = new Regex(@"(?:\A|[\r\n])([^\s>#\[\]]{1,63})> ?\z", O);
        static readonly Regex RxPrivPrompt = new Regex(@"(?:\A|[\r\n])([^\s>#\[\]]{1,63})# ?\z", O);
        static readonly Regex RxUsername = new Regex(@"(?:username|login): ?\z", O);
        static readonly Regex RxPassword = new Regex(@"password: ?\z", O);
        static readonly Regex RxBadLogin = new Regex(@"% ?(bad passwords|login invalid|authentication failed|bad secrets|access denied)", O);
        static readonly Regex RxNoVtyPass = new Regex(@"password required, but none set", O);
        static readonly Regex RxNoEnablePass = new Regex(@"% ?no password set", O);
        static readonly Regex RxConfirm = new Regex(@"\[confirm\] ?\z", O);
        static readonly Regex RxSave = new Regex(@"\[yes/no\]: ?\z", O);
        static readonly Regex RxDeleteName = new Regex(@"delete filename \[[^\]]*\]\? ?\z", O);
        static readonly Regex RxConfigMode = new Regex(@"(?:\A|[\r\n])[^\s>#]{1,63}\([^)]*\)# ?\z", O);

        const int StepTimeout = 30000;

        /// <summary>
        /// Resets one device. progress(percent, text) reports what is happening; log(line) gets the session text.
        /// pcNumber is used to check the device's name (COREbaba-12 must belong to PC 12) when checkName is true.
        /// </summary>
        public static ResetResult Run(Device dev, string address, int pcNumber, Credentials cred, bool checkName,
                                      Action<int, string> progress, Action<string> log, CancellationToken ct)
        {
            var r = new ResetResult();
            string host;
            int port;
            Device.SplitHostPort(address, out host, out port);
            var lines = new LineLog(log);
            bool erased = false;
            try
            {
                progress(5, "Connecting...");
                TelnetSession t;
                try { t = TelnetSession.Connect(host, port, 4000, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    throw new ResetFailed("Can't reach " + address + " (Telnet). Is it on and set up? Nothing was changed.");
                }
                using (t)
                using (ct.Register(t.Dispose))
                {
                    t.Received += lines.Add;

                    progress(15, "Logging in...");
                    r.Hostname = Login(t, cred, lines, ct);
                    progress(35, "Logged in to " + r.Hostname);

                    if (checkName)
                    {
                        Match m = Regex.Match(r.Hostname, @"(\d{1,2})$");
                        int n;
                        if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n != pcNumber)
                            throw new ResetFailed(r.Hostname + " looks like PC " + n + "'s device, not PC " + pcNumber +
                                                  "'s. Nothing was erased. Check the PC number.");
                    }

                    // ---- write erase
                    progress(45, "Erasing the saved configuration (write erase)...");
                    string outText = Command(t, "write erase", lines, ct);
                    if (Regex.IsMatch(outText, @"%\s*(error|invalid|incomplete)", O))
                        throw new ResetFailed("write erase failed: " + FirstErrorLine(outText));
                    erased = true;

                    // ---- delete vlan.dat (switches only)
                    string warning = null;
                    if (dev.Kind == DeviceKind.Switch)
                    {
                        progress(65, "Deleting vlan.dat...");
                        outText = Command(t, "delete vlan.dat", lines, ct);
                        if (Regex.IsMatch(outText, @"no such file", O))
                            lines.Note("vlan.dat was not there (already deleted).");
                        else if (Regex.IsMatch(outText, @"%\s*(error|invalid)", O))
                            warning = "vlan.dat could not be deleted: " + FirstErrorLine(outText);
                    }

                    // ---- reload
                    progress(85, "Reloading...");
                    Reload(t, lines, ct);
                    lines.Flush();

                    r.Ok = warning == null;
                    r.Message = warning == null ? "Erased and reloading" : "Erased and reloading, but " + warning;
                    progress(100, r.Message);
                }
            }
            catch (OperationCanceledException)
            {
                lines.Flush();
                r.Message = erased ? "Stopped after write erase (not reloaded yet)" : "Stopped. Nothing was erased.";
            }
            catch (ResetFailed ex)
            {
                lines.Flush();
                r.Message = ex.Message;
                if (erased && !r.Message.Contains("erased")) r.Message += " (The configuration was already erased.)";
            }
            catch (Exception ex)
            {
                lines.Flush();
                r.Message = "Error: " + ex.Message + (erased ? " (The configuration was already erased.)" : "");
            }
            return r;
        }

        static string FirstErrorLine(string text)
        {
            foreach (string l in text.Split('\r', '\n'))
                if (l.Contains("%")) return l.Trim();
            return text.Trim();
        }

        /// <summary>Logs in and gets to the # prompt. Returns the device's hostname.</summary>
        static string Login(TelnetSession t, Credentials cred, LineLog lines, CancellationToken ct)
        {
            var pats = new[] { RxUsername, RxPassword, RxPrivPrompt, RxUserPrompt, RxBadLogin, RxNoVtyPass, RxNoEnablePass, RxConfigMode };
            int userTries = 0, passTries = 0, enableTries = 0, enableCmds = 0, nudges = 0;
            bool enableSent = false;
            DateTime end = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < end)
            {
                int i = t.Expect(pats, 4000, ct);
                switch (i)
                {
                    case 0:   // Username:
                        if (++userTries > 1) throw new ResetFailed("Wrong username or password (" + cred.Username + "). Nothing was erased.");
                        t.Send(cred.Username);
                        break;
                    case 1:   // Password:
                        if (!enableSent)
                        {
                            if (++passTries > 1) throw new ResetFailed("Wrong Telnet password. Nothing was erased. Check Settings.");
                            t.Send(cred.Password);
                        }
                        else
                        {
                            if (++enableTries > 1) throw new ResetFailed("Wrong enable password. Nothing was erased. Check Settings.");
                            t.Send(cred.EnablePassword);
                        }
                        lines.Note("(password sent)");
                        break;
                    case 2:   // HOST#
                        return t.LastMatch.Groups[1].Value;
                    case 3:   // HOST>
                        if (++enableCmds > 2) throw new ResetFailed("Wrong enable password. Nothing was erased. Check Settings.");
                        t.Send("enable");
                        enableSent = true;
                        break;
                    case 4:
                        throw new ResetFailed((enableSent ? "Wrong enable password" : "Wrong username or password") + ". Nothing was erased. Check Settings.");
                    case 5:
                        throw new ResetFailed("Telnet login isn't set up on this device (no vty password). Nothing was erased.");
                    case 6:
                        throw new ResetFailed("The device has no enable password, so it can't be reset over Telnet. Nothing was erased.");
                    case 7:   // HOST(config)#  - someone left it in config mode
                        t.Send("end");
                        break;
                    case TelnetSession.Closed:
                        throw new ResetFailed("The device closed the connection while logging in. Nothing was erased.");
                    case TelnetSession.Timeout:
                        if (++nudges > 3) throw new ResetFailed("No login prompt from the device. Nothing was erased.");
                        t.Send("");   // press Enter to wake it up
                        break;
                }
            }
            throw new ResetFailed("Could not log in (took too long). Nothing was erased.");
        }

        /// <summary>Runs a command at the # prompt, answering its questions with Enter. Returns its output.</summary>
        static string Command(TelnetSession t, string cmd, LineLog lines, CancellationToken ct)
        {
            t.Send(cmd);
            var output = new StringBuilder();
            var pats = new[] { RxConfirm, RxDeleteName, RxPrivPrompt, RxSave };
            int answers = 0;
            while (true)
            {
                int i = t.Expect(pats, StepTimeout, ct);
                output.Append(t.LastBefore);
                if (i == 2) return output.ToString();
                if (i == TelnetSession.Closed) throw new ResetFailed("The device closed the connection during '" + cmd + "'.");
                if (i == TelnetSession.Timeout) throw new ResetFailed("No answer from the device during '" + cmd + "'.");
                output.Append(t.LastMatch.Value);
                if (++answers > 4) throw new ResetFailed("Unexpected questions during '" + cmd + "'.");
                if (i == 3)
                {
                    // never save anything
                    t.Send("no");
                    lines.Note("(answered no)");
                }
                else
                {
                    t.Send("");
                    lines.Note("(pressed Enter)");
                }
            }
        }

        static void Reload(TelnetSession t, LineLog lines, CancellationToken ct)
        {
            t.Send("reload");
            var pats = new[] { RxSave, RxConfirm, RxPrivPrompt };
            for (int n = 0; n < 4; n++)
            {
                int i = t.Expect(pats, StepTimeout, ct);
                switch (i)
                {
                    case 0:   // Save? [yes/no]:  - never save, the config was just erased
                        t.Send("no");
                        lines.Note("(answered no)");
                        break;
                    case 1:   // Proceed with reload? [confirm]
                        t.Send("");
                        lines.Note("(pressed Enter - reloading)");
                        t.WaitClosed(10000, ct);
                        return;
                    case 2:
                        throw new ResetFailed("The device did not reload: " + FirstErrorLine(t.LastBefore));
                    case TelnetSession.Closed:
                        return;   // already reloading
                    default:
                        throw new ResetFailed("No answer from the device after 'reload'.");
                }
            }
            throw new ResetFailed("Unexpected questions during 'reload'.");
        }
    }

    /// <summary>Turns received text into whole lines for the log.</summary>
    class LineLog
    {
        readonly Action<string> log;
        readonly StringBuilder partial = new StringBuilder();
        readonly object lk = new object();

        public LineLog(Action<string> log) { this.log = log; }

        public void Add(string s)
        {
            lock (lk)
            {
                foreach (char c in s)
                {
                    if (c == '\n' || c == '\r') { Emit(); continue; }
                    if (c < ' ' && c != '\t') continue;
                    partial.Append(c);
                }
            }
        }

        public void Note(string s)
        {
            lock (lk) { Emit(); log("    " + s); }
        }

        public void Flush()
        {
            lock (lk) Emit();
        }

        void Emit()
        {
            string line = partial.ToString().TrimEnd();
            partial.Length = 0;
            if (line.Trim().Length > 0) log("  | " + line);
        }
    }
}
