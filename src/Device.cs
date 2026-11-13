// Cisco Reset - the three lab devices and how to find this PC's number.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CiscoReset
{
    public enum DeviceKind { Switch, Router }

    public class Device
    {
        public string Key;          // settings key: cucm, taas, baba
        public string Name;         // shown on the tile
        public string Role;         // short description
        public DeviceKind Kind;
        public string Pattern;      // e.g. 10.{m}.1.4  (may end in :port)

        /// <summary>The address for PC number m, e.g. 10.12.1.4.</summary>
        public string Address(int m)
        {
            return (Pattern ?? "").Trim().Replace("{m}", m.ToString()).Replace("{M}", m.ToString());
        }

        /// <summary>Splits "host" or "host:port" (port 23 by default).</summary>
        public static void SplitHostPort(string address, out string host, out int port)
        {
            host = address;
            port = 23;
            int c = address.LastIndexOf(':');
            int p;
            if (c > 0 && int.TryParse(address.Substring(c + 1), out p) && p > 0 && p < 65536)
            {
                host = address.Substring(0, c);
                port = p;
            }
        }
    }

    public class PcNumberInfo
    {
        public int Number;      // 0 = not found
        public string Ip = "";
        public string Adapter = "";
    }

    public static class PcNumber
    {
        /// <summary>The last 2 digits of this PC's Wi-Fi IP address (10.3.3.112 = PC 12).</summary>
        public static PcNumberInfo Detect()
        {
            var info = new PcNumberInfo();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        IPAddress a = ua.Address;
                        if (a.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = a.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;   // no DHCP address yet
                        info.Ip = a.ToString();
                        info.Adapter = ni.Name;
                        info.Number = b[3] % 100;
                        return info;
                    }
                }
            }
            catch { }
            return info;
        }
    }
}
