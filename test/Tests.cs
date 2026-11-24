// Cisco Reset - automatic tests against pretend Cisco devices. Run test.bat.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.Threading;

namespace CiscoReset.Test
{
    static class Tests
    {
        static int failed, passed;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + "   -> " + detail); }
        }

        static ResetResult Reset(FakeIos f, DeviceKind kind, int pc, Credentials cred, bool checkName, List<string> log)
        {
            var dev = new Device { Key = "x", Name = f.Hostname, Kind = kind, Pattern = "127.0.0.1:" + f.Port };
            return Resetter.Run(dev, dev.Address(pc), pc, cred ?? new Credentials(), checkName,
                                (p, s) => { }, s => { lock (log) log.Add(s); }, CancellationToken.None);
        }

        static ResetResult Save(FakeIos f, DeviceKind kind, int pc, Credentials cred, bool checkName, List<string> log)
        {
            var dev = new Device { Key = "x", Name = f.Hostname, Kind = kind, Pattern = "127.0.0.1:" + f.Port };
            return Resetter.SaveConfig(dev, dev.Address(pc), pc, cred ?? new Credentials(), checkName,
                                       (p, s) => { }, s => { lock (log) log.Add(s); }, CancellationToken.None);
        }

        static int Main()
        {
            Console.WriteLine("Cisco Reset tests");

            // 1. switch, vty password login + enable
            {
                var f = new FakeIos { Hostname = "COREtaas-12", IsSwitch = true }.Start();
                var log = new List<string>();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, log);
                Check("switch (password login): result ok", r.Ok, r.Message);
                Check("switch: write erase done", f.Erased, string.Join(" / ", f.Commands));
                Check("switch: vlan.dat deleted", f.VlanDeleted, string.Join(" / ", f.Commands));
                Check("switch: reloaded", f.Reloaded, string.Join(" / ", f.Commands));
                Check("switch: config NOT saved", !f.Saved, "answered yes to Save?");
                Check("switch: hostname read", r.Hostname == "COREtaas-12", r.Hostname);
                Check("switch: password not in log", !string.Join("\n", log).Contains("pass\n") && !log.Exists(l => l.Trim() == "| pass"), string.Join("\n", log));
                f.Stop();
            }

            // 2. switch with "login local" (Username: admin, privilege 15)
            {
                var f = new FakeIos { Hostname = "COREbaba-12", IsSwitch = true, LocalLogin = true }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("switch (login local): ok", r.Ok && f.Erased && f.VlanDeleted && f.Reloaded && !f.Saved, r.Message);
                Check("switch (login local): no 'enable' needed", !f.Commands.Contains("enable"), string.Join(" / ", f.Commands));
                f.Stop();
            }

            // 3. CME router: no vlan.dat step
            {
                var f = new FakeIos { Hostname = "CUCM-12", IsSwitch = false }.Start();
                ResetResult r = Reset(f, DeviceKind.Router, 12, null, true, new List<string>());
                Check("CUCM router: ok", r.Ok && f.Erased && f.Reloaded && !f.Saved, r.Message);
                Check("CUCM router: vlan.dat not touched", !f.Commands.Contains("delete vlan.dat"), string.Join(" / ", f.Commands));
                f.Stop();
            }

            // 3b. EDGE router (Cisco ISR 4321, IOS-XE) at 10.m.m.1: login local, no vlan.dat
            {
                var f = new FakeIos { Hostname = "EDGE-12", IsSwitch = false, IosXe = true, LocalLogin = true }.Start();
                var log = new List<string>();
                ResetResult r = Reset(f, DeviceKind.Router, 12, null, true, log);
                Check("EDGE 4321: ok", r.Ok && f.Erased && f.Reloaded && !f.Saved, r.Message);
                Check("EDGE 4321: vlan.dat not touched", !f.Commands.Contains("delete vlan.dat"), string.Join(" / ", f.Commands));
                Check("EDGE 4321: hostname read", r.Hostname == "EDGE-12", r.Hostname);
                f.Stop();
            }

            // 3c. another PC's EDGE router: refused
            {
                var f = new FakeIos { Hostname = "EDGE-21", IsSwitch = false, IosXe = true }.Start();
                ResetResult r = Reset(f, DeviceKind.Router, 12, null, true, new List<string>());
                Check("EDGE of another PC: refused", !r.Ok && r.Message.Contains("PC 21") && !f.Erased, r.Message);
                f.Stop();
            }

            // 4. vlan.dat already gone: still fine
            {
                var f = new FakeIos { Hostname = "COREbaba-12", HasVlanDat = false }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("vlan.dat missing: still ok and reloaded", r.Ok && f.Erased && f.Reloaded, r.Message);
                f.Stop();
            }

            // 5. wrong Telnet password: nothing erased
            {
                var f = new FakeIos { Hostname = "COREbaba-12", Password = "student-changed-it" }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("wrong password: fails", !r.Ok && r.Message.Contains("Wrong Telnet password"), r.Message);
                Check("wrong password: nothing erased", !f.Erased && !f.Reloaded, "erased!");
                f.Stop();
            }

            // 6. wrong enable password
            {
                var f = new FakeIos { Hostname = "COREbaba-12", Enable = "other" }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("wrong enable password: fails, nothing erased", !r.Ok && r.Message.Contains("enable") && !f.Erased, r.Message);
                f.Stop();
            }

            // 7. wrong username (login local)
            {
                var f = new FakeIos { Hostname = "COREbaba-12", LocalLogin = true, Username = "namemo" }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("wrong username: fails, nothing erased", !r.Ok && !f.Erased, r.Message);
                f.Stop();
            }

            // 8. another PC's device (COREbaba-21 while this is PC 12): refused
            {
                var f = new FakeIos { Hostname = "COREbaba-21" }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("other PC's device: refused", !r.Ok && r.Message.Contains("PC 21") && !f.Erased, r.Message);
                f.Stop();
            }

            // 9. ...but allowed when the name check is off, and default names like "Switch" are always fine
            {
                var f = new FakeIos { Hostname = "Switch" }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("hostname without a number: ok", r.Ok && f.Erased, r.Message);
                f.Stop();
            }

            // 10. no vty password set
            {
                var f = new FakeIos { NoVtyPassword = true }.Start();
                ResetResult r = Reset(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("no vty password: clear message", !r.Ok && r.Message.Contains("isn't set up"), r.Message);
                f.Stop();
            }

            // 11. nothing listening
            {
                var dev = new Device { Name = "x", Kind = DeviceKind.Switch, Pattern = "127.0.0.1:1" };
                ResetResult r = Resetter.Run(dev, dev.Address(12), 12, new Credentials(), true, (p, s) => { }, s => { }, CancellationToken.None);
                Check("unreachable: clear message", !r.Ok && r.Message.Contains("Can't reach"), r.Message);
            }

            // 12. address patterns
            {
                var d = new Device { Pattern = "10.{m}.100.8" };
                Check("pattern 10.{m}.100.8 -> 10.12.100.8", d.Address(12) == "10.12.100.8", d.Address(12));
                var e = new Device { Pattern = new AppSettings().EdgeIp };
                Check("EDGE default 10.{m}.{m}.1 -> 10.12.12.1", e.Address(12) == "10.12.12.1", e.Address(12));
                Check("EDGE default on PC 7 -> 10.7.7.1", e.Address(7) == "10.7.7.1", e.Address(7));
                string h; int p;
                Device.SplitHostPort("10.12.1.4", out h, out p);
                Check("default port 23", h == "10.12.1.4" && p == 23, h + ":" + p);
                Device.SplitHostPort("127.0.0.1:2304", out h, out p);
                Check("host:port", h == "127.0.0.1" && p == 2304, h + ":" + p);
            }

            // 13. settings: an old settings file (no EDGE lines) still gets the EDGE default, and EDGE is saved
            {
                string file = System.IO.Path.GetTempFileName();
                System.IO.File.WriteAllLines(file, new[] { "CUCM=10.{m}.100.8", "coreTAAS=10.{m}.1.2", "coreBABA=10.{m}.1.4", "TickBABA=0" });
                AppSettings s = AppSettings.Load(file);
                Check("old settings: EDGE default + ticked", s.EdgeIp == "10.{m}.{m}.1" && s.TickedFor("edge") && !s.TickedFor("baba"), s.EdgeIp);
                s.EdgeIp = "10.{m}.{m}.254";
                s.SetTicked("edge", false);
                s.Save(file);
                AppSettings s2 = AppSettings.Load(file);
                Check("settings: EDGE address and tick saved", s2.PatternFor("edge") == "10.{m}.{m}.254" && !s2.TickEdge && s2.PatternFor("cucm") == "10.{m}.100.8", s2.EdgeIp);
                System.IO.File.Delete(file);
            }

            // 14. save config (wr) on a switch: wr typed, nothing erased or reloaded
            {
                var f = new FakeIos { Hostname = "COREbaba-12" }.Start();
                var log = new List<string>();
                ResetResult r = Save(f, DeviceKind.Switch, 12, null, true, log);
                Check("save (wr): ok", r.Ok && r.Message.Contains("[OK]"), r.Message);
                Check("save (wr): 'wr' typed", f.WroteMemory && f.Commands.Contains("wr"), string.Join(" / ", f.Commands));
                Check("save (wr): nothing erased, deleted or reloaded", !f.Erased && !f.VlanDeleted && !f.Reloaded, string.Join(" / ", f.Commands));
                f.Stop();
            }

            // 15. save config on the EDGE router (login local, IOS-XE)
            {
                var f = new FakeIos { Hostname = "EDGE-12", IsSwitch = false, IosXe = true, LocalLogin = true }.Start();
                ResetResult r = Save(f, DeviceKind.Router, 12, null, true, new List<string>());
                Check("save (wr) EDGE 4321: ok", r.Ok && f.WroteMemory && !f.Reloaded, r.Message);
                f.Stop();
            }

            // 16. save config: another PC's device is refused, wrong password says nothing was saved
            {
                var f = new FakeIos { Hostname = "COREbaba-21" }.Start();
                ResetResult r = Save(f, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("save (wr) other PC's device: refused", !r.Ok && r.Message.Contains("PC 21") && !f.WroteMemory, r.Message);
                f.Stop();
                var g = new FakeIos { Hostname = "COREbaba-12", Password = "changed" }.Start();
                r = Save(g, DeviceKind.Switch, 12, null, true, new List<string>());
                Check("save (wr) wrong password: 'Nothing was saved'", !r.Ok && r.Message.Contains("Nothing was saved") && !g.WroteMemory, r.Message);
                g.Stop();
            }

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }
    }
}
