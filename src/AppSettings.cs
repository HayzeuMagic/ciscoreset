// Cisco Reset - saved settings, log file and where they are kept.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.IO;

namespace CiscoReset
{
    public class AppSettings
    {
        // Defaults from the lab guide: every device uses "pass".
        public string Username = "admin";
        public string Password = "pass";
        public string EnablePassword = "pass";
        public string CucmIp = "10.{m}.100.8";
        public string TaasIp = "10.{m}.1.2";
        public string BabaIp = "10.{m}.1.4";
        public int PcNumber;              // 0 = use the number from the Wi-Fi IP
        public bool CheckName = true;     // refuse a device whose name ends in another PC's number
        public bool TickCucm = true, TickTaas = true, TickBaba = true;

        public Credentials Credentials
        {
            get { return new Credentials { Username = Username, Password = Password, EnablePassword = EnablePassword }; }
        }

        public static AppSettings Load(string file)
        {
            var s = new AppSettings();
            if (!File.Exists(file)) return s;
            try
            {
                foreach (string raw in File.ReadAllLines(file))
                {
                    int i = raw.IndexOf('=');
                    if (i <= 0 || raw.TrimStart().StartsWith("#")) continue;
                    string k = raw.Substring(0, i).Trim().ToLowerInvariant(), v = raw.Substring(i + 1).Trim();
                    int n;
                    switch (k)
                    {
                        case "username": s.Username = v; break;
                        case "password": s.Password = v; break;
                        case "enablepassword": s.EnablePassword = v; break;
                        case "cucm": if (v.Length > 0) s.CucmIp = v; break;
                        case "coretaas": if (v.Length > 0) s.TaasIp = v; break;
                        case "corebaba": if (v.Length > 0) s.BabaIp = v; break;
                        case "pcnumber": s.PcNumber = int.TryParse(v, out n) && n > 0 && n < 255 ? n : 0; break;
                        case "checkname": s.CheckName = v != "0"; break;
                        case "tickcucm": s.TickCucm = v != "0"; break;
                        case "ticktaas": s.TickTaas = v != "0"; break;
                        case "tickbaba": s.TickBaba = v != "0"; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save(string file)
        {
            var lines = new List<string>
            {
                "# Cisco Reset settings. {m} is replaced with the PC number.",
                "# The passwords are the lab defaults - don't put a real password here.",
                "Username=" + Username,
                "Password=" + Password,
                "EnablePassword=" + EnablePassword,
                "CUCM=" + CucmIp,
                "coreTAAS=" + TaasIp,
                "coreBABA=" + BabaIp,
                "PcNumber=" + (PcNumber > 0 ? PcNumber.ToString() : ""),
                "CheckName=" + (CheckName ? "1" : "0"),
                "TickCUCM=" + (TickCucm ? "1" : "0"),
                "TickTAAS=" + (TickTaas ? "1" : "0"),
                "TickBABA=" + (TickBaba ? "1" : "0")
            };
            try { File.WriteAllLines(file, lines); } catch { }
        }
    }

    public static class Paths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        static string dataDir;

        /// <summary>Next to the .exe, or in %LOCALAPPDATA%\CiscoReset if that folder is read-only.</summary>
        public static string DataDir
        {
            get
            {
                if (dataDir != null) return dataDir;
                try
                {
                    string probe = Path.Combine(AppDir, ".write-test");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    dataDir = AppDir;
                }
                catch
                {
                    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CiscoReset");
                    Directory.CreateDirectory(dataDir);
                }
                return dataDir;
            }
        }

        public static string SettingsFile { get { return Path.Combine(DataDir, "CiscoReset.settings.ini"); } }
        public static string LogDir { get { return Path.Combine(DataDir, "Logs"); } }
    }

    public static class Logger
    {
        public static string Dir;
        public static event Action<string> Line;
        static readonly object lk = new object();

        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + msg;
            lock (lk)
            {
                try
                {
                    if (Dir != null)
                    {
                        Directory.CreateDirectory(Dir);
                        File.AppendAllText(Path.Combine(Dir, "CiscoReset-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log"), line + Environment.NewLine);
                    }
                }
                catch { }
            }
            Action<string> h = Line;
            if (h != null) h(line);
        }
    }
}
