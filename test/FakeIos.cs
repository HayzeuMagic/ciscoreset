// Cisco Reset - a pretend Cisco IOS device for testing (no real switch needed).
// It answers Telnet like a Catalyst switch or a CME router: login, enable, write erase,
// delete vlan.dat, reload. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CiscoReset.Test
{
    public class FakeIos
    {
        public string Hostname = "COREbaba-12";
        public bool IsSwitch = true;
        public bool LocalLogin;          // "login local": Username + Password, privilege 15
        public bool NoVtyPassword;       // "Password required, but none set"
        public string Username = "admin", Password = "pass", Enable = "pass";
        public bool HasVlanDat = true;
        public bool CrNulOnly;           // nothing special; kept for symmetry

        // what happened
        public volatile bool Erased, VlanDeleted, Reloaded, Saved;
        public readonly List<string> Commands = new List<string>();

        public int Port { get; private set; }
        TcpListener listener;

        public FakeIos Start()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var t = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        TcpClient c = listener.AcceptTcpClient();
                        var h = new Thread(() => Serve(c)) { IsBackground = true };
                        h.Start();
                    }
                }
                catch { }
            }) { IsBackground = true };
            t.Start();
            return this;
        }

        public void Stop() { try { listener.Stop(); } catch { } }

        // ------------------------------------------------------------------ one Telnet session
        class Conn
        {
            public NetworkStream S;
            public bool Echo = true;
            bool lastCr;
            public void Write(string s) { byte[] b = Encoding.ASCII.GetBytes(s); S.Write(b, 0, b.Length); }

            // Reads one line, skipping Telnet commands; echoes typed characters when Echo is on.
            public string ReadLine()
            {
                var sb = new StringBuilder();
                while (true)
                {
                    int b = S.ReadByte();
                    if (b < 0) throw new System.IO.EndOfStreamException();
                    if (b == 255)
                    {
                        int v = S.ReadByte();
                        if (v >= 251 && v <= 254) S.ReadByte();
                        else if (v == 250) { while (S.ReadByte() != 240) { } }
                        continue;
                    }
                    if (b == 10 || b == 0) { if (lastCr) { lastCr = false; continue; } }
                    lastCr = b == 13;
                    if (b == 13 || b == 10) { if (Echo) Write("\r\n"); return sb.ToString(); }
                    sb.Append((char)b);
                    if (Echo) Write(((char)b).ToString());
                }
            }
        }

        void Serve(TcpClient c)
        {
            var conn = new Conn { S = c.GetStream() };
            try
            {
                // IAC WILL ECHO, IAC WILL SGA, IAC DO TTYPE, IAC DO NAWS - like IOS
                conn.S.Write(new byte[] { 255, 251, 1, 255, 251, 3, 255, 253, 24, 255, 253, 31 }, 0, 12);
                conn.Write("\r\n\r\nUser Access Verification\r\n\r\n");
                if (NoVtyPassword) { conn.Write("Password required, but none set\r\n"); return; }

                bool priv = false;
                if (LocalLogin)
                {
                    int tries = 0;
                    while (true)
                    {
                        conn.Write("Username: ");
                        string u = conn.ReadLine();
                        conn.Write("Password: ");
                        conn.Echo = false;
                        string p = conn.ReadLine();
                        conn.Echo = true;
                        conn.Write("\r\n");
                        if (u == Username && p == Password) { priv = true; break; }
                        conn.Write("% Login invalid\r\n\r\n");
                        if (++tries >= 3) return;
                    }
                }
                else
                {
                    int tries = 0;
                    while (true)
                    {
                        conn.Write("Password: ");
                        conn.Echo = false;
                        string p = conn.ReadLine();
                        conn.Echo = true;
                        conn.Write("\r\n");
                        if (p == Password) break;
                        if (++tries >= 3) { conn.Write("% Bad passwords\r\n"); return; }
                    }
                }

                bool modified = false;
                while (true)
                {
                    conn.Write("\r\n" + Hostname + (priv ? "#" : ">"));
                    string cmd = conn.ReadLine().Trim();
                    lock (Commands) Commands.Add(cmd);
                    if (cmd.Length == 0) continue;
                    if (!priv)
                    {
                        if (cmd == "enable" || cmd == "en")
                        {
                            int tries = 0;
                            while (true)
                            {
                                conn.Write("Password: ");
                                conn.Echo = false;
                                string p = conn.ReadLine();
                                conn.Echo = true;
                                conn.Write("\r\n");
                                if (p == Enable) { priv = true; break; }
                                if (++tries >= 3) { conn.Write("% Bad secrets\r\n"); break; }
                            }
                        }
                        else conn.Write("% Invalid input detected at '^' marker.\r\n");
                        continue;
                    }
                    if (cmd == "write erase" || cmd == "wr er")
                    {
                        conn.Write("Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]");
                        string a = conn.ReadLine();
                        if (a.Length > 0) continue;   // anything but Enter cancels
                        conn.Write("[OK]\r\nErase of nvram: complete\r\n");
                        Erased = true;
                        modified = true;
                    }
                    else if (cmd == "delete vlan.dat")
                    {
                        conn.Write("Delete filename [vlan.dat]? ");
                        string a = conn.ReadLine();
                        if (a.Length > 0) continue;
                        if (!IsSwitch || !HasVlanDat) { conn.Write("%Error deleting flash:/vlan.dat (No such file or directory)\r\n"); continue; }
                        conn.Write("Delete flash:/vlan.dat? [confirm]");
                        a = conn.ReadLine();
                        if (a.Length > 0) continue;
                        HasVlanDat = false;
                        VlanDeleted = true;
                    }
                    else if (cmd == "reload")
                    {
                        if (modified)
                        {
                            conn.Write("\r\nSystem configuration has been modified. Save? [yes/no]: ");
                            string a = conn.ReadLine().Trim();
                            if (a == "yes" || a == "y") Saved = true;
                        }
                        conn.Write("Proceed with reload? [confirm]");
                        string b = conn.ReadLine();
                        if (b.Length > 0) continue;
                        Reloaded = true;
                        conn.Write("\r\n\r\n*Mar  1 00:10:00: %SYS-5-RELOAD: Reload requested\r\n");
                        return;
                    }
                    else conn.Write("% Invalid input detected at '^' marker.\r\n");
                }
            }
            catch { }
            finally { c.Close(); }
        }
    }
}
