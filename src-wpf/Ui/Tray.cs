using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;
using WF = System.Windows.Forms;

namespace Grobyq
{
    // Tray icon: the logo tile + a state dot (red = off, amber = armed, green = active), and the card-style dark menu of
    // the WinForms build (Open, Thread optimizer active, Exit). WPF has no tray control; the WinForms NotifyIcon runs on
    // the WPF dispatcher's message pump, so its events arrive on the UI thread.
    public sealed class Tray : IDisposable
    {
        private readonly WF.NotifyIcon _icon;
        private WF.ToolStripMenuItem _miActive;
        private readonly Dictionary<int, SD.Icon> _cache = new Dictionary<int, SD.Icon>();
        private static float _k = -1f;

        public event Action OpenRequested, ToggleRequested, ExitRequested, DoubleClicked;

        internal static readonly SD.Color CCard = SD.Color.FromArgb(16, 16, 20);
        internal static readonly SD.Color CCardHi = SD.Color.FromArgb(22, 22, 27);
        internal static readonly SD.Color CLine = SD.Color.FromArgb(28, 28, 34);
        internal static readonly SD.Color CFg = SD.Color.FromArgb(237, 237, 237);
        internal static readonly SD.Color CFaint = SD.Color.FromArgb(92, 92, 102);
        private static readonly SD.Color COk = SD.Color.FromArgb(76, 214, 138);
        private static readonly SD.Color CWarn = SD.Color.FromArgb(232, 180, 72);
        private static readonly SD.Color CBad = SD.Color.FromArgb(224, 90, 90);

        // DPI factor for the WinForms menu (pixel paddings); fonts in points scale on their own.
        internal static float K
        {
            get
            {
                if (_k > 0) return _k;
                _k = 1f;
                try { using (var g = SD.Graphics.FromHwnd(IntPtr.Zero)) _k = Math.Max(1f, g.DpiX / 96f); } catch { }
                return _k;
            }
        }
        internal static int D(int v) { return (int)Math.Round(v * K); }

        public Tray()
        {
            _icon = new WF.NotifyIcon { Text = Brand.Name };
            _icon.Icon = Make(CBad);
            _icon.DoubleClick += (s, e) => { var h = DoubleClicked; if (h != null) h(); };
            BuildMenu();
            _icon.Visible = true;
        }

        // Rebuilt after a language switch.
        public void BuildMenu()
        {
            var old = _icon.ContextMenuStrip;
            var m = new WF.ContextMenuStrip
            {
                Font = new SD.Font(L.Family, 9.75f),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                BackColor = CCard,
                ForeColor = CFg,
                Renderer = new TrayRenderer(),
                Padding = new WF.Padding(D(4), D(6), D(4), D(6)),
                DropShadowEnabled = true
            };
            m.HandleCreated += (s, e) =>
            {
                try
                {
                    int round = 3; DwmSetWindowAttribute(m.Handle, 33, ref round, 4);                         // DWMWCP_ROUNDSMALL (Win11)
                    int brd = CLine.R | (CLine.G << 8) | (CLine.B << 16); DwmSetWindowAttribute(m.Handle, 34, ref brd, 4);
                }
                catch { }
            };
            m.Items.Add(Item(L.T("Open grobyq"), (s, e) => { var h = OpenRequested; if (h != null) h(); }));
            m.Items.Add(new WF.ToolStripSeparator());
            bool was = _miActive != null && _miActive.Checked;
            _miActive = Item(L.T("Thread optimizer active"), (s, e) => { var h = ToggleRequested; if (h != null) h(); });
            _miActive.Checked = was;
            m.Items.Add(_miActive);
            m.Items.Add(new WF.ToolStripSeparator());
            m.Items.Add(Item(L.T("Exit"), (s, e) => { var h = ExitRequested; if (h != null) h(); }));
            _icon.ContextMenuStrip = m;
            if (old != null) { try { old.Dispose(); } catch { } }
        }

        private static WF.ToolStripMenuItem Item(string text, EventHandler click)
        {
            var i = new WF.ToolStripMenuItem(text) { ForeColor = CFg, Padding = new WF.Padding(D(4), D(7), D(18), D(7)) };
            i.Click += click;
            return i;
        }

        // state: OFF / ARMED / ACTIVE
        public void SetState(string state, string live, bool active)
        {
            SD.Color c = state == "ACTIVE" ? COk : state == "ARMED" ? CWarn : CBad;
            try { _icon.Icon = Make(c); } catch { }
            string text = state == "ACTIVE" ? Brand.Name + " - " + L.T("ACTIVE") + ": " + live
                        : state == "ARMED" ? Brand.Name + " - " + L.T("armed, waiting for a game")
                        : Brand.Name + " - " + L.T("off");
            try { _icon.Text = text.Length > 63 ? text.Substring(0, 60) + "..." : text; } catch { }
            if (_miActive != null) _miActive.Checked = active;
        }

        public void Balloon(string text)
        {
            try { _icon.ShowBalloonTip(3000, Brand.Name, text, WF.ToolTipIcon.Info); } catch { }
        }

        public void Dispose()
        {
            try { _icon.Visible = false; } catch { }
            try { var m = _icon.ContextMenuStrip; _icon.ContextMenuStrip = null; if (m != null) m.Dispose(); } catch { }
            try { _icon.Dispose(); } catch { }
        }

        // The logo tile + a status dot, drawn at the size the shell asks for.
        private SD.Icon Make(SD.Color dot)
        {
            int key = dot.ToArgb();
            SD.Icon ic;
            if (_cache.TryGetValue(key, out ic)) return ic;
            int sz = 16;
            try { sz = WF.SystemInformation.SmallIconSize.Width; } catch { }
            string asset = sz >= 30 ? "ico_32.png" : sz >= 23 ? "ico_24.png" : sz >= 19 ? "ico_20.png" : "ico_16.png";
            using (var bmp = new SD.Bitmap(sz, sz))
            {
                using (var g = SD.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
                    g.InterpolationMode = SD2.InterpolationMode.HighQualityBicubic;
                    g.Clear(SD.Color.Transparent);
                    using (var src = LoadPng(asset)) if (src != null) g.DrawImage(src, 0, 0, sz, sz);
                    float d = sz * 0.46f, x = sz - d, y = sz - d;
                    using (var ring = new SD.SolidBrush(SD.Color.FromArgb(255, 10, 10, 13))) g.FillEllipse(ring, x - 1, y - 1, d + 1, d + 1);
                    using (var br = new SD.SolidBrush(dot)) g.FillEllipse(br, x + 0.5f, y + 0.5f, d - 2, d - 2);
                }
                ic = SD.Icon.FromHandle(bmp.GetHicon());
            }
            _cache[key] = ic;
            return ic;
        }

        private static SD.Bitmap LoadPng(string name)
        {
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + name, UriKind.Absolute));
                if (info == null) return null;
                var ms = new System.IO.MemoryStream();
                using (var s = info.Stream) s.CopyTo(ms);
                ms.Position = 0;
                return new SD.Bitmap(ms);
            }
            catch { return null; }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        internal static SD2.GraphicsPath Round(SD.RectangleF r, float rad)
        {
            var p = new SD2.GraphicsPath();
            float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Card-style menu: #101014 fill, hairline, rounded hover, a check for the armed optimizer.
    internal sealed class TrayRenderer : WF.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(WF.ToolStripRenderEventArgs e)
        {
            using (var b = new SD.SolidBrush(Tray.CCard)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(WF.ToolStripRenderEventArgs e)
        {
            var r = e.ToolStrip.ClientRectangle;
            using (var p = new SD.Pen(Tray.CLine)) e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
        protected override void OnRenderImageMargin(WF.ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(WF.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics; g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
            var r = new SD.RectangleF(Tray.D(2), 1, e.Item.Width - Tray.D(4), e.Item.Height - 2);
            using (var path = Tray.Round(r, 6 * Tray.K)) using (var b = new SD.SolidBrush(Tray.CCardHi)) g.FillPath(b, path);
        }
        protected override void OnRenderItemText(WF.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Tray.CFg : Tray.CFaint;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(WF.ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new SD.Pen(Tray.CLine)) e.Graphics.DrawLine(p, Tray.D(10), y, e.Item.Width - Tray.D(10), y);
        }
        protected override void OnRenderItemCheck(WF.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle; var g = e.Graphics; g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, u = Math.Min(r.Width, r.Height) / 2.4f;
            using (var p = new SD.Pen(Tray.CFg, 1.8f * Tray.K) { StartCap = SD2.LineCap.Round, EndCap = SD2.LineCap.Round, LineJoin = SD2.LineJoin.Round })
                g.DrawLines(p, new[] { new SD.PointF(cx - u, cy), new SD.PointF(cx - u * 0.3f, cy + u * 0.7f), new SD.PointF(cx + u, cy - u * 0.7f) });
        }
    }
}
