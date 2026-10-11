using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Grobyq
{
    // About: product seal. Large mark + wordmark, purpose, community links, third-party notice and the
    // grobyq rights. The license lives on Home; the fingerprint is not shown here.
    internal sealed class AboutPage : Page
    {
        public AboutPage(MainForm m)
        {
            ColumnMax = 800;
            BuildHero();

            // ---- purpose ----
            Heading(L.T("Purpose"), true);
            Box().Stack(new TextBlock(L.T("grobyq lowers input latency and smooths frame-time by tuning thread priorities and CPU-core affinity - for the game and for the rest of the system (services, drivers, background apps), so the game's threads get the cores and CPU time first. It only changes thread priority, boost and affinity, and it is fully reversible: DISABLE or Reset returns Windows to its defaults. No Realtime class, no freezing or killing of threads. Device and cleanup changes happen only through their own buttons."), Theme.Fg, 10f));

            // ---- community: four equal cards, AerialOS centered under them ----
            Heading(L.T("Community"), false);
            var grid = Add(new CardGrid { MaxCols = 4 }, 4);
            grid.Add(new LinkCard("x", "X", "grobPC", Brand.XUrl));
            grid.Add(new LinkCard("plane", "Telegram", L.T("grobPC group"), Brand.TelegramUrl));
            grid.Add(new LinkCard("img:grobpc_glyph_64.png", L.T("Website"), "grobpc.com", Brand.SiteUrl));
            grid.Add(new LinkCard("img:aerialos_64.png", "Discord", "AerialOS", Brand.AerialDiscord));
            grid.Add(new LinkCard("img:aerialos_64.png", "AerialOS", L.T("Project website"), Brand.AerialUrl));

            // ---- notice: third-party (allowed) + grobyq (proprietary) ----
            Heading(L.T("Third-party components & notice"), false);
            var nc = Box();
            nc.Stack(new SubHead(L.T("Third-party (MIT)")));
            nc.Stack(new TextBlock("NVIDIA NVAPI SDK — MIT\nNVIDIA Profile Inspector (Orbmu2k) — MIT", Theme.Fg, 10f));
            nc.Stack(new TextBlock(L.T("Driver setting names and IDs come from these projects. Both licenses allow use with attribution; the license texts ship with the program and its sources."), Theme.Dim, 9.5f));
            var ls = new Strip(Theme.D(48));
            ls.Add(new Btn(L.T("License texts"), Btn.Style.Outline)).Click += (s, e) => ShowLicenseTexts();
            nc.Stack(ls);
            nc.Stack(new Hairline());
            nc.Stack(new SubHead("grobyq"));
            nc.Stack(new TextBlock(L.T("The grobyq and grobPC marks, the interface, binaries, presets and the source code of this program are © mitzu LLC; the AerialOS mark belongs to zero_unflesh & mitzu LLC. Copying, reverse engineering, republishing or reusing the software, its code or other intellectual property is not permitted, except for the third-party components above under their own licenses. The license to run grobyq is personal and bound to the activated HWID."), Theme.Dim, 9.5f));

            var foot = Add(new Label { Text = L.T("© mitzu LLC 2026. All rights reserved."), Font = Theme.F(9f), ForeColor = Theme.Faint, BackColor = Theme.Bg, AutoSize = false, Height = Theme.D(34), TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false }, 0);
            foot.Margin = new Padding(0, Theme.D(8), 0, 0);
        }

        // Centered: the large G mark, the wordmark, version line and the author line.
        private void BuildHero()
        {
            var hero = Add(new Hero { MarkHeight = Theme.D(104), MarkTop = Theme.D(6), MarkCenter = true, Height = Theme.D(218) }, 4);
            var t = Center(Brand.Name, Theme.Bold(28f), Theme.Fg);
            var v = Center(L.F("version {0}", Brand.Version) + "  ·  " + L.T("Core-aware latency tuning"), Theme.F(10.5f), Theme.Dim);
            var c = Center(Brand.Credit, Theme.F(9.5f), Theme.Faint);
            hero.Controls.AddRange(new Control[] { t, v, c });
            hero.Layout += (s, e) =>
            {
                int y = hero.MarkRect.Bottom + Theme.D(10);
                foreach (var l in new[] { t, v, c }) { l.SetBounds(0, y, hero.Width, l.PreferredHeight); y = l.Bottom + Theme.D(2); }
            };
        }

        private static Label Center(string text, Font f, Color c)
        {
            return new Label { Text = text, Font = f, ForeColor = c, BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, UseMnemonic = false, AutoEllipsis = true };
        }

        // In-app modal over the window: NVAPI · Profile Inspector, monospace, Close / Esc.
        private void ShowLicenseTexts()
        {
            TextOverlay.Show(FindForm(), L.T("License texts"), new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("NVAPI", ReadRes("LICENSE-NVAPI.txt")),
                new KeyValuePair<string, string>("Profile Inspector", ReadRes("LICENSE-nvidiaProfileInspector.txt")),
            });
        }

        private static string ReadRes(string name)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (s == null) return name + ": not embedded";
                    using (var r = new StreamReader(s)) return r.ReadToEnd();
                }
            }
            catch (Exception ex) { return name + ": " + ex.Message; }
        }
    }
}
