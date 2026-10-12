using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Grobyq
{
    // Startup animation (the rotating logo of v3.6.8) on its own UI thread, so it keeps playing while the main thread
    // checks the license and builds the window. Frames: the embedded splash.bin ("GQSP": count, fps, w, h, then
    // length-prefixed JPEG frames). Panel start only - the logon start (/tray) stays silent.
    // The splash thread owns its own frozen brushes: it never touches the App resources (they belong to the main thread).
    internal sealed class SplashHost
    {
        private Dispatcher _disp;
        private SplashWindow _w;
        private readonly ManualResetEventSlim _shown = new ManualResetEventSlim(false);
        private readonly TaskCompletionSource<bool> _closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public static SplashHost Start()
        {
            var data = SplashData.Load();
            if (data == null) return null;
            var h = new SplashHost();
            var th = new Thread(() =>
            {
                try
                {
                    h._disp = Dispatcher.CurrentDispatcher;
                    var w = new SplashWindow(data);
                    w.ContentRendered += (s, e) => h._shown.Set();
                    w.Closed += (s, e) => { h._closed.TrySetResult(true); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); };
                    h._w = w;
                    w.Show();
                    Dispatcher.Run();
                }
                catch { }
                finally { h._shown.Set(); h._closed.TrySetResult(true); }
            }) { IsBackground = true, Name = "grobyq-splash" };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            h._shown.Wait(2000);
            return h;
        }

        private void Post(Action<SplashWindow> a)
        {
            var d = _disp; var w = _w;
            if (d == null || w == null) return;
            try { d.BeginInvoke((Action)(() => { try { a(w); } catch { } })); } catch { }
        }

        public void Status(string s) { Post(w => w.SetStatus(s)); }

        // The app is ready: the animation finishes, fades out and closes. Completes when closed (or after ms).
        public Task FinishAsync(int ms)
        {
            Post(w => w.Ready = true);
            return Task.WhenAny(_closed.Task, Task.Delay(ms));
        }

        // Close at once (the license / AerialOS window must come up). Blocks up to 2 s.
        public void CloseNow()
        {
            Post(w => { w.Ready = true; w.Skip(); });
            _closed.Task.Wait(2000);
        }
    }

    internal sealed class SplashData
    {
        public List<byte[]> Frames;
        public int Fps;

        public static SplashData Load()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("splash.bin"))
                {
                    if (s == null) return null;
                    var r = new BinaryReader(s);
                    if (new string(r.ReadChars(4)) != "GQSP") return null;
                    int n = r.ReadInt32(), fps = r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
                    if (n <= 0 || n > 1000) return null;
                    var list = new List<byte[]>(n);
                    for (int i = 0; i < n; i++)
                    {
                        int len = r.ReadInt32();
                        if (len <= 0 || len > 8 * 1024 * 1024) return null;
                        list.Add(r.ReadBytes(len));
                    }
                    return new SplashData { Frames = list, Fps = Math.Max(10, Math.Min(60, fps)) };
                }
            }
            catch { return null; }
        }

        public static BitmapSource Decode(byte[] jpeg)
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = new MemoryStream(jpeg, false);
            b.EndInit();
            b.Freeze();
            return b;
        }
    }

    internal sealed class SplashWindow : Window
    {
        private static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

        private readonly List<byte[]> _frames;
        private readonly Image _img;
        private readonly TextBlock _status;
        private readonly DispatcherTimer _t;
        private int _idx;
        private bool _fading;
        public bool Ready;

        public SplashWindow(SplashData d)
        {
            _frames = d.Frames;
            Title = Brand.Name;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;                 // rounded corners + the fade-out
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            Width = 400; Height = 460;
            UseLayoutRounding = true;
            FontFamily = new FontFamily(L.Family);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var root = new Border { Background = Brushes.Black, BorderBrush = Frozen(0x1C, 0x1C, 0x22), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
            var g = new Grid();
            _img = new Image { Width = 330, Height = 330, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 0, 0) };
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            g.Children.Add(_img);
            var name = new TextBlock { Text = Brand.Name, FontSize = 20 * 96.0 / 72.0, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 350, 0, 0) };
            g.Children.Add(name);
            var tag = new TextBlock { Text = L.T("Core-aware latency tuning"), FontSize = 9.5 * 96.0 / 72.0, Foreground = Frozen(0x8A, 0x8A, 0x94), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 390, 0, 0) };
            g.Children.Add(tag);
            _status = new TextBlock { Text = "", FontSize = 8.5 * 96.0 / 72.0, Foreground = Frozen(0x5C, 0x5C, 0x66), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 14) };
            g.Children.Add(_status);
            root.Child = g;
            Content = root;

            Frame(0);
            _t = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(Math.Max(10, 1000 / d.Fps)) };
            _t.Tick += Tick;
            ContentRendered += (s, e) => _t.Start();
            Closed += (s, e) => _t.Stop();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape || e.Key == Key.Space || e.Key == Key.Enter) Skip(); };
            MouseLeftButtonUp += (s, e) => Skip();
        }

        public void SetStatus(string s) { _status.Text = s ?? ""; }

        public void Skip()
        {
            if (_idx < _frames.Count - 1) { _idx = _frames.Count - 1; Frame(_idx); }
        }

        private void Frame(int i)
        {
            try { _img.Source = SplashData.Decode(_frames[i]); } catch { }
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
            if (_idx < _frames.Count - 1) { _idx++; Frame(_idx); return; }
            if (Ready) _fading = true;                 // hold the last frame until the app is ready
        }
    }
}
