// Cisco Reset - a small Telnet client: connects, answers option negotiation, and waits for prompts.
// C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CiscoReset
{
    public class TelnetSession : IDisposable
    {
        const byte IAC = 255, DONT = 254, DO = 253, WONT = 252, WILL = 251, SB = 250, SE = 240;
        const byte OPT_ECHO = 1, OPT_SGA = 3;

        public const int Timeout = -1, Closed = -2;

        readonly TcpClient tcp;
        readonly NetworkStream ns;
        readonly StringBuilder buf = new StringBuilder();   // text received but not yet matched
        readonly object lk = new object();
        readonly HashSet<int> answered = new HashSet<int>();
        bool closed;
        int iacState;   // 0 data, 1 after IAC, 2 after IAC+verb, 3 inside SB, 4 IAC inside SB
        byte verb;

        /// <summary>Raw text as it arrives (for the log).</summary>
        public event Action<string> Received;

        /// <summary>The text that matched in the last successful Expect, and the text before it.</summary>
        public Match LastMatch { get; private set; }
        public string LastBefore { get; private set; }

        TelnetSession(TcpClient tcp)
        {
            this.tcp = tcp;
            ns = tcp.GetStream();
            var t = new Thread(ReadLoop) { IsBackground = true, Name = "telnet-read" };
            t.Start();
        }

        public static TelnetSession Connect(string host, int port, int timeoutMs, CancellationToken ct)
        {
            var tcp = new TcpClient();
            try
            {
                IAsyncResult ar = tcp.BeginConnect(host, port, null, null);
                int waited = 0;
                while (!ar.AsyncWaitHandle.WaitOne(100))
                {
                    waited += 100;
                    if (ct.IsCancellationRequested) throw new OperationCanceledException();
                    if (waited >= timeoutMs) throw new SocketException((int)SocketError.TimedOut);
                }
                tcp.EndConnect(ar);
                tcp.NoDelay = true;
                return new TelnetSession(tcp);
            }
            catch
            {
                tcp.Close();
                throw;
            }
        }

        /// <summary>True if something answers on the Telnet port within the timeout.</summary>
        public static bool CanConnect(string host, int port, int timeoutMs)
        {
            using (var tcp = new TcpClient())
            {
                try
                {
                    IAsyncResult ar = tcp.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    tcp.EndConnect(ar);
                    return true;
                }
                catch { return false; }
            }
        }

        public bool IsClosed { get { lock (lk) return closed; } }

        void ReadLoop()
        {
            var data = new byte[4096];
            try
            {
                while (true)
                {
                    int n = ns.Read(data, 0, data.Length);
                    if (n <= 0) break;
                    var text = new StringBuilder();
                    for (int i = 0; i < n; i++) Feed(data[i], text);
                    if (text.Length > 0)
                    {
                        string s = text.ToString();
                        lock (lk) { buf.Append(s); Monitor.PulseAll(lk); }
                        Action<string> h = Received;
                        if (h != null) h(s);
                    }
                }
            }
            catch { }
            lock (lk) { closed = true; Monitor.PulseAll(lk); }
        }

        // Telnet protocol: pull out IAC commands, keep the text.
        void Feed(byte b, StringBuilder text)
        {
            switch (iacState)
            {
                case 0:
                    if (b == IAC) iacState = 1;
                    else if (b == 0 || b == 7) { }                       // NUL, bell
                    else if (b == 8) { if (text.Length > 0) text.Length--; } // backspace
                    else text.Append((char)b);
                    break;
                case 1:
                    if (b == IAC) { text.Append((char)255); iacState = 0; }
                    else if (b == DO || b == DONT || b == WILL || b == WONT) { verb = b; iacState = 2; }
                    else if (b == SB) iacState = 3;
                    else iacState = 0;
                    break;
                case 2:
                    Negotiate(verb, b);
                    iacState = 0;
                    break;
                case 3:
                    if (b == IAC) iacState = 4;
                    break;
                case 4:
                    iacState = b == SE ? 0 : 3;
                    break;
            }
        }

        // We let the device echo and suppress go-ahead (like any normal Telnet client) and refuse everything else.
        void Negotiate(byte v, byte opt)
        {
            byte reply;
            if (v == WILL) reply = (opt == OPT_ECHO || opt == OPT_SGA) ? DO : DONT;
            else if (v == DO) reply = opt == OPT_SGA ? WILL : WONT;
            else return;   // WONT / DONT: nothing to answer
            if (!answered.Add(v * 256 + opt)) return;   // answer each request once
            SendRaw(new byte[] { IAC, reply, opt });
        }

        void SendRaw(byte[] b)
        {
            try { lock (ns) ns.Write(b, 0, b.Length); } catch { }
        }

        /// <summary>Sends a line followed by Enter (CR LF).</summary>
        public void Send(string line)
        {
            var bytes = new List<byte>();
            foreach (char c in line)
            {
                if (c == (char)255) bytes.Add(IAC);
                bytes.Add((byte)(c > 255 ? '?' : c));
            }
            bytes.Add(13);
            bytes.Add(10);
            SendRaw(bytes.ToArray());
        }

        /// <summary>
        /// Waits until one of the patterns matches the received text. Returns the index of the pattern that matched
        /// earliest in the text, Timeout or Closed. The text up to the end of the match is used up.
        /// </summary>
        public int Expect(Regex[] patterns, int timeoutMs, CancellationToken ct)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            lock (lk)
            {
                while (true)
                {
                    string s = buf.ToString();
                    int best = -1;
                    Match bestM = null;
                    for (int i = 0; i < patterns.Length; i++)
                    {
                        Match m = patterns[i].Match(s);
                        if (m.Success && (bestM == null || m.Index < bestM.Index)) { best = i; bestM = m; }
                    }
                    if (bestM != null)
                    {
                        LastMatch = bestM;
                        LastBefore = s.Substring(0, bestM.Index);
                        buf.Remove(0, bestM.Index + bestM.Length);
                        return best;
                    }
                    if (closed) { LastBefore = s; buf.Length = 0; return Closed; }
                    ct.ThrowIfCancellationRequested();
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) { LastBefore = s; return Timeout; }
                    Monitor.Wait(lk, Math.Min(left, 200));
                }
            }
        }

        /// <summary>Waits for the device to drop the connection (after reload). True if it did.</summary>
        public bool WaitClosed(int timeoutMs, CancellationToken ct)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            lock (lk)
            {
                while (!closed)
                {
                    ct.ThrowIfCancellationRequested();
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return false;
                    Monitor.Wait(lk, Math.Min(left, 200));
                }
                return true;
            }
        }

        public void Dispose()
        {
            try { tcp.Close(); } catch { }
            lock (lk) { closed = true; Monitor.PulseAll(lk); }
        }
    }
}
