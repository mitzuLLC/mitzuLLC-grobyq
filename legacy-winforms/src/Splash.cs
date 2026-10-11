using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Grobyq
{
    // Startup animation (the rotating logo) on its own UI thread, so it keeps playing while the
    // main thread checks the license and builds the window. Frames: Assets/splash.bin (tools/make_splash.py).
    internal sealed class SplashHost
    {
        private Thread _th;
        private SplashForm _form;
        private readonly ManualResetEvent _shown = new ManualResetEvent(false), _closed = new ManualResetEvent(false);

        public static SplashHost Start()
        {
            var frames = SplashForm.LoadFrames();
            if (frames == null) return null;
            var h = new SplashHost();
            h._th = new Thread(() =>
            {
                try
                {
                    h._form = new SplashForm(frames);
                    h._form.Shown += (s, e) => h._shown.Set();
                    Application.Run(h._form);
                }
                catch { }
                finally { h._shown.Set(); h._closed.Set(); }
            });
            h._th.SetApartmentState(ApartmentState.STA);
            h._th.IsBackground = true;
            h._th.Start();
            h._shown.WaitOne(2000);
            return h;
        }

        private void Post(Action<SplashForm> a)
        {
            var f = _form;
            if (f == null) return;
            try { if (f.IsHandleCreated && !f.IsDisposed) f.BeginInvoke((Action)(() => a(f))); } catch { }
        }

        public void Status(string s) { Post(f => f.Status = s); }

        // The app is ready: finish the animation, fade out, close. Blocks until closed (max ms).
        public void FinishAndWait(int ms) { Post(f => f.Ready = true); _closed.WaitOne(ms); }

        // Close at once (e.g. the license window must come up).
        public void CloseNow() { Post(f => { f.Ready = true; f.Skip(); }); _closed.WaitOne(2000); }
    }

    internal sealed class SplashForm : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        private readonly List<byte[]> _frames;
        private readonly int _fps;
        private int _idx;
        private Bitmap _bmp;
        private readonly System.Windows.Forms.Timer _t;
        private bool _fading;
        private string _status = "";
        public bool Ready;

        public string Status { get { return _status; } set { _status = value ?? ""; Invalidate(); } }

        public static SplashData LoadFrames()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("splash.bin"))
                {
                    if (s == null) return null;
                    var r = new BinaryReader(s);
                    if (new string(r.ReadChars(4)) != "GQSP") return null;
                    int n = r.ReadInt32(), fps = r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
                    var list = new List<byte[]>(n);
                    for (int i = 0; i < n; i++) { int len = r.ReadInt32(); list.Add(r.ReadBytes(len)); }
                    return new SplashData { Frames = list, Fps = Math.Max(10, Math.Min(60, fps)) };
                }
            }
            catch { return null; }
        }

        public SplashForm(SplashData d)
        {
            _frames = d.Frames; _fps = d.Fps;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Text = Brand.Name;
            BackColor = Color.Black;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.D(400), Theme.D(460));
            try { Icon = Brand.AppIcon; } catch { }
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) Skip(); };
            MouseClick += (s, e) => Skip();
            HandleCreated += (s, e) =>
            {
                try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }   // Win11 rounded corners
            };
            Decode(0);
            _t = new System.Windows.Forms.Timer { Interval = Math.Max(10, 1000 / _fps) };
            _t.Tick += Tick;
            Shown += (s, e) => _t.Start();
        }

        public void Skip() { if (_idx < _frames.Count - 1) { _idx = _frames.Count - 1; Decode(_idx); Invalidate(); } }

        private void Decode(int i)
        {
            try
            {
                using (var ms = new MemoryStream(_frames[i]))
                using (var im = Image.FromStream(ms))
                {
                    var b = new Bitmap(im);
                    var old = _bmp; _bmp = b;
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        private void Tick(object sender, EventArgs e)
        {
            if (_fading)
            {
                double o = Opacity - 0.12;
                if (o <= 0.02) { _t.Stop(); Close(); return; }
                Opacity = o;
                return;
            }
            if (_idx < _frames.Count - 1) { _idx++; Decode(_idx); Invalidate(); return; }
            if (Ready) _fading = true;          // hold the last frame until the app is ready
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int sz = Theme.D(330);
            var r = new Rectangle((ClientSize.Width - sz) / 2, Theme.D(18), sz, sz);
            if (_bmp != null) g.DrawImage(_bmp, r);
            TextRenderer.DrawText(g, Brand.Name, Theme.Semi(20f), new Rectangle(0, r.Bottom + Theme.D(2), ClientSize.Width, Theme.D(40)), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, L.T("Core-aware latency tuning"), Theme.F(9.5f), new Rectangle(0, r.Bottom + Theme.D(42), ClientSize.Width, Theme.D(22)), Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            if (_status.Length > 0)
                TextRenderer.DrawText(g, _status, Theme.F(8.5f), new Rectangle(0, ClientSize.Height - Theme.D(30), ClientSize.Width, Theme.D(20)), Theme.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            using (var p = new Pen(Color.FromArgb(28, 28, 34))) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { _t.Dispose(); } catch { } if (_bmp != null) _bmp.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class SplashData { public List<byte[]> Frames; public int Fps; }
}
