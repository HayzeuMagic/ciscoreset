// Cisco Reset - one device in the grid: a switch/router picture, its name and IP, and a progress bar.
// Same look as Lab Reset's PC tiles. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CiscoReset
{
    public enum TileState { Idle, Waiting, Working, Done, Error, Skipped }

    public class DeviceTile : Control
    {
        static readonly Timer anim = new Timer { Interval = 40 };
        static int phase;
        static event EventHandler AnimTick;
        static DeviceTile()
        {
            anim.Tick += (s, e) =>
            {
                phase++;
                EventHandler h = AnimTick;
                if (h != null) h(null, EventArgs.Empty);
            };
            anim.Start();
        }

        static readonly Color CIdle = Color.FromArgb(214, 221, 230);
        static readonly Color CWait = Color.FromArgb(170, 200, 240);
        static readonly Color CWork = Color.FromArgb(37, 117, 230);
        static readonly Color CDone = Color.FromArgb(34, 150, 80);
        static readonly Color CError = Color.FromArgb(214, 64, 64);
        static readonly Color CSkip = Color.FromArgb(150, 158, 168);
        static readonly Color CFrame = Color.FromArgb(55, 62, 72);
        static readonly Color CTrack = Color.FromArgb(226, 230, 236);

        public readonly Device Device;
        readonly int step;
        string address = "";
        bool ticked = true, hover, clickable = true;
        TileState state = TileState.Idle;
        int percent;
        string detail = "Ready";
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 20000 };

        public event EventHandler TickToggled;

        public DeviceTile(Device device, int step)
        {
            Device = device;
            this.step = step;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(200, 196);
            Margin = new Padding(8);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            AnimTick += OnAnim;
            UpdateTip();
        }

        protected override void Dispose(bool disposing)
        {
            AnimTick -= OnAnim;
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        void OnAnim(object s, EventArgs e)
        {
            if (state == TileState.Working || state == TileState.Waiting) Invalidate();
        }

        void UpdateTip()
        {
            tip.SetToolTip(this, Device.Name + "  " + address + "\n" + Device.Role + "\n\n" + detail + "\n\nClick to tick or untick.");
        }

        public bool Ticked { get { return ticked; } set { if (ticked != value) { ticked = value; Invalidate(); } } }
        public bool Clickable { get { return clickable; } set { clickable = value; Cursor = value ? Cursors.Hand : Cursors.Default; } }
        public TileState State { get { return state; } }
        public string Address { get { return address; } set { address = value ?? ""; UpdateTip(); Invalidate(); } }

        public void SetState(TileState s, int pct, string text)
        {
            state = s;
            if (pct >= 0) percent = Math.Max(0, Math.Min(100, pct));
            if (text != null) detail = text;
            UpdateTip();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || !clickable) return;
            EventHandler h = TickToggled;
            if (h != null) h(this, EventArgs.Empty);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        Color StateColor()
        {
            switch (state)
            {
                case TileState.Waiting: return CWait;
                case TileState.Working: return CWork;
                case TileState.Done: return CDone;
                case TileState.Error: return CError;
                case TileState.Skipped: return CSkip;
                default: return CIdle;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = Width;
            Color col = StateColor();

            // card
            var card = new RectangleF(1, 1, w - 3, Height - 3);
            using (GraphicsPath cp = Round(card, 8))
            {
                using (var b = new SolidBrush(hover && clickable ? Color.FromArgb(240, 245, 252) : Color.White)) g.FillPath(b, cp);
                using (var pen = new Pen(hover && clickable ? Color.FromArgb(150, 180, 220) : Color.FromArgb(218, 222, 228))) g.DrawPath(pen, cp);
            }

            // device chassis with a coloured status panel and a row of ports
            var chassis = new RectangleF((w - 156) / 2f, 30, 156, 66);
            using (GraphicsPath sp = Round(chassis, 6))
            using (var b = new SolidBrush(CFrame)) g.FillPath(b, sp);
            var panel = new RectangleF(chassis.X + 6, chassis.Y + 6, chassis.Width - 12, 38);
            using (var b = new SolidBrush(col)) g.FillRectangle(b, panel);
            DrawPanel(g, panel);
            DrawPorts(g, new RectangleF(chassis.X + 6, panel.Bottom + 5, chassis.Width - 12, 10));

            // step badge
            using (var f = new Font(Font.FontFamily, 7.5f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(110, 118, 130)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString("STEP " + step, f, b, new RectangleF(0, 8, w - 10, 14), sf);

            // name and address
            using (var f = new Font(Font.FontFamily, 11f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.FromArgb(30, 34, 40)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(Device.Name, f, b, new RectangleF(0, 102, w, 22), sf);
            using (var f = new Font(Font.FontFamily, 9f))
            using (var b = new SolidBrush(Color.FromArgb(80, 88, 100)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(address, f, b, new RectangleF(0, 123, w, 18), sf);

            // progress bar
            var track = new RectangleF(14, 146, w - 28, 9);
            using (GraphicsPath tp = Round(track, 4.5f))
            using (var b = new SolidBrush(CTrack)) g.FillPath(b, tp);
            DrawBar(g, track, col);

            // status text (two lines)
            using (var f = new Font(Font.FontFamily, 7.75f))
            using (var b = new SolidBrush(state == TileState.Error ? Color.FromArgb(170, 40, 40) : Color.FromArgb(95, 102, 112)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
                g.DrawString(detail, f, b, new RectangleF(6, 159, w - 12, 32), sf);

            CheckBoxRenderer.DrawCheckBox(g, new Point(8, 7),
                ticked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);

            if (!ticked)
                using (GraphicsPath cp = Round(card, 8))
                using (var b = new SolidBrush(Color.FromArgb(150, 255, 255, 255))) g.FillPath(b, cp);
        }

        void DrawPorts(Graphics g, RectangleF r)
        {
            using (var b = new SolidBrush(Color.FromArgb(150, 160, 172)))
            {
                if (Device.Kind == DeviceKind.Switch)
                {
                    // a row of Ethernet ports
                    int n = 12;
                    float pw = 8, gap = (r.Width - n * pw) / (n + 1);
                    for (int i = 0; i < n; i++) g.FillRectangle(b, r.X + gap + i * (pw + gap), r.Y + 2, pw, 6);
                }
                else
                {
                    // router: a few ports and a phone handset (CallManager Express)
                    for (int i = 0; i < 3; i++) g.FillRectangle(b, r.X + 6 + i * 14, r.Y + 2, 9, 6);
                    using (var p = new Pen(Color.FromArgb(150, 160, 172), 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(p, r.Right - 30, r.Y - 4, 22, 14, 200, 140);
                    g.FillEllipse(b, r.Right - 32, r.Y + 1, 6, 6);
                    g.FillEllipse(b, r.Right - 12, r.Y + 1, 6, 6);
                }
            }
        }

        void DrawPanel(Graphics g, RectangleF s)
        {
            GraphicsState saved = g.Save();
            g.SetClip(s);
            float x = s.Left + s.Width / 2f, y = s.Top + s.Height / 2f;
            if (state == TileState.Working)
            {
                float off = (phase * 1.5f) % 16f;
                using (var b = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                    for (float sx = s.Left - s.Height - 16 + off; sx < s.Right; sx += 16)
                        g.FillPolygon(b, new[] { new PointF(sx, s.Bottom), new PointF(sx + 8, s.Bottom), new PointF(sx + 8 + s.Height, s.Top), new PointF(sx + s.Height, s.Top) });
                using (var f = new Font(Font.FontFamily, 11f, FontStyle.Bold))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(percent + "%", f, Brushes.White, s, sf);
            }
            else if (state == TileState.Waiting)
            {
                for (int i = 0; i < 3; i++)
                {
                    double t = Math.Sin((phase * 0.15) - i * 0.9);
                    int a = (int)(140 + 115 * t);
                    using (var b = new SolidBrush(Color.FromArgb(Math.Max(40, Math.Min(255, a)), 255, 255, 255)))
                        g.FillEllipse(b, x - 16 + i * 13, y - 4, 8, 8);
                }
            }
            else if (state == TileState.Done)
            {
                using (var p = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] { new PointF(x - 11, y), new PointF(x - 3, y + 8), new PointF(x + 12, y - 8) });
            }
            else if (state == TileState.Error)
            {
                using (var p = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(p, x - 8, y - 8, x + 8, y + 8);
                    g.DrawLine(p, x + 8, y - 8, x - 8, y + 8);
                }
            }
            else if (state == TileState.Skipped)
            {
                using (var p = new Pen(Color.White, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, x - 10, y, x + 10, y);
            }
            else
            {
                // idle: little status LEDs
                using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                    for (int i = 0; i < 4; i++) g.FillEllipse(b, s.Left + 8 + i * 10, s.Top + 8, 5, 5);
            }
            g.Restore(saved);
        }

        void DrawBar(Graphics g, RectangleF track, Color col)
        {
            RectangleF fill;
            if (state == TileState.Working && percent == 0)
            {
                float bw = track.Width * 0.3f;
                float t = (float)((Math.Sin(phase * 0.08) + 1) / 2);
                fill = new RectangleF(track.X + t * (track.Width - bw), track.Y, bw, track.Height);
            }
            else
            {
                float p = state == TileState.Done ? 100 : state == TileState.Idle || state == TileState.Waiting ? 0 : percent;
                if (state == TileState.Error || state == TileState.Skipped) p = Math.Max(p, 100);
                if (p <= 0) return;
                fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * p / 100f), track.Height);
            }
            using (GraphicsPath fp = Round(fill, 4.5f))
            using (var b = new SolidBrush(col)) g.FillPath(b, fp);
        }
    }
}
