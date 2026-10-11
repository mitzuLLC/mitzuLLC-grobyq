using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Grobyq
{
    // Names, links and the logo assets (embedded PNGs, see csproj).
    public static class Brand
    {
        public const string Name = "grobyq";
        public const string Tagline = "Core-aware latency tuning";   // shown through L.T()
        public const string Company = "mitzu LLC";
        public const string Credit = "by mitzu LLC  ·  grobPC";
        public const string XUrl = "https://x.com/grobPC";
        public const string SiteUrl = "https://grobpc.com";
        public const string TelegramUrl = "https://t.me/grobPC";
        public const string Mutex = "Global\\grobyq-mitzu";
        public const string TaskName = "grobyq-mitzu";
        public const string OldTaskName = "grobPC-mitzu";
        public const string SettingsFile = "grobyq.settings.txt";
        public const string OldSettingsFile = "grobPC.settings.txt";

        // Distribution: grobyq runs only on AerialOS unless the signed license carries "anyos" (see AerialGate.cs).
        public const bool AerialOnly = true;
        public const string AerialUrl = "https://aerialos.vercel.app";
        public const string AerialDiscord = "https://dsc.gg/aerialos";

        public static string Version
        {
            get { try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } catch { return "?"; } }
        }

        private static readonly Dictionary<string, Image> _img = new Dictionary<string, Image>();

        public static Image Img(string name)
        {
            Image im;
            if (_img.TryGetValue(name, out im)) return im;
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    var ms = new MemoryStream();
                    s.CopyTo(ms); ms.Position = 0;
                    im = Image.FromStream(ms);   // keep ms alive with the image
                }
            }
            catch { im = null; }
            _img[name] = im;
            return im;
        }

        private static Icon _app;
        public static Icon AppIcon
        {
            get
            {
                if (_app != null) return _app;
                try { _app = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                if (_app == null) _app = FromImage(Img("ico_32.png"), 32);
                return _app;
            }
        }

        // Tray: the logo tile + a status dot, drawn at the size the shell asks for.
        private static readonly Dictionary<int, Icon> _tray = new Dictionary<int, Icon>();
        public static Icon Tray(Color dot)
        {
            int key = dot.ToArgb();
            Icon ic;
            if (_tray.TryGetValue(key, out ic)) return ic;
            int sz = 16;
            try { sz = SystemInformation.SmallIconSize.Width; } catch { }
            string asset = sz >= 30 ? "ico_32.png" : sz >= 23 ? "ico_24.png" : sz >= 19 ? "ico_20.png" : "ico_16.png";
            using (var bmp = new Bitmap(sz, sz))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                var src = Img(asset);
                if (src != null) g.DrawImage(src, 0, 0, sz, sz);
                float d = sz * 0.46f, x = sz - d, y = sz - d;
                using (var ring = new SolidBrush(Color.FromArgb(255, 10, 10, 13))) g.FillEllipse(ring, x - 1, y - 1, d + 1, d + 1);
                using (var br = new SolidBrush(dot)) g.FillEllipse(br, x + 0.5f, y + 0.5f, d - 2, d - 2);
                ic = Icon.FromHandle(bmp.GetHicon());
            }
            _tray[key] = ic;
            return ic;
        }

        public static Icon FromImage(Image im, int size)
        {
            if (im == null) return SystemIcons.Application;
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                g.DrawImage(im, 0, 0, size, size);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static void Open(string url) { try { System.Diagnostics.Process.Start(url); } catch { } }
    }
}
