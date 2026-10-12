using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Grobyq.Pages
{
    // About: product seal. Large mark + wordmark, purpose, community links and the grobyq rights.
    // The license lives on Home. No third-party section: v4 ships no third-party components (the NVIDIA parts are gone).
    public sealed class AboutPage : PageBase
    {
        public AboutPage() : base(800)
        {
            BuildHero();

            // ---- purpose ----
            Heading(L.T("Purpose"), true);
            var pc = Box();
            var purpose = pc.Add(Ui.Text(L.T("grobyq lowers input latency and smooths frame-time by tuning thread priorities and CPU-core affinity - for the game and for the rest of the system (services, drivers, background apps), so the game's threads get the cores and CPU time first. It only changes thread priority, boost and affinity, and it is fully reversible: switching the optimizers off returns every change to its Windows default. No Realtime class, no freezing or killing of threads. Device and cleanup changes happen only through their own buttons."), 10, "Fg"));
            purpose.Margin = new Thickness(0, 6, 0, 8);
            purpose.LineHeight = Ui.Pt(10) * 1.45;

            // ---- community: four equal cards, AerialOS centered under them ----
            Heading(L.T("Community"), false);
            var grid = new CardGridPanel { MaxCols = 4 };
            grid.Children.Add(new LinkCard("x", "X", "grobPC", Brand.XUrl));
            grid.Children.Add(new LinkCard("plane", "Telegram", L.T("grobPC group"), Brand.TelegramUrl));
            grid.Children.Add(new LinkCard("img:grobpc_glyph_64.png", L.T("Website"), "grobpc.com", Brand.SiteUrl));
            grid.Children.Add(new LinkCard("img:aerialos_64.png", "Discord", "AerialOS", Brand.AerialDiscord));
            grid.Children.Add(new LinkCard("img:aerialos_64.png", "AerialOS", L.T("Project website"), Brand.AerialUrl));
            Add(grid, 4);

            // ---- rights ----
            Heading(L.T("Rights"), false);
            var nc = Box();
            var sub = nc.Add(Ui.Semi("grobyq", 9.5, "Dim"));
            sub.Margin = new Thickness(0, 8, 0, 4);
            var notice = nc.Add(Ui.Text(L.T("The grobyq and grobPC marks, the interface, binaries, presets and the source code of this program are © mitzu LLC; the AerialOS mark belongs to zero_unflesh & mitzu LLC. Copying, reverse engineering, republishing or reusing the software, its code or other intellectual property is not permitted. The license to run grobyq is personal and bound to the activated HWID."), 9.5, "Dim"));
            notice.Margin = new Thickness(0, 0, 0, 8);
            notice.LineHeight = Ui.Pt(9.5) * 1.45;

            var foot = Ui.Text(L.T("© mitzu LLC 2026. All rights reserved."), 9, "Faint", false);
            foot.HorizontalAlignment = HorizontalAlignment.Center;
            foot.Margin = new Thickness(0, 14, 0, 0);
            Add(foot, 8);
        }

        // Centered: the large G mark, the wordmark, version line and the author line.
        private void BuildHero()
        {
            var hero = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var mark = Ui.Picture("hero_logo.png", 104);
            mark.Opacity = 0.85;
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            hero.Children.Add(mark);

            var t = Ui.Bold(Brand.Name, 28, "Fg");
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 6, 0, 0);
            hero.Children.Add(t);

            var v = Ui.Text(L.F("version {0}", Brand.Version) + "  ·  " + L.T("Core-aware latency tuning"), 10.5, "Dim", false);
            v.HorizontalAlignment = HorizontalAlignment.Center;
            v.Margin = new Thickness(0, 2, 0, 0);
            hero.Children.Add(v);

            var c = Ui.Text(Brand.Credit, 9.5, "Faint", false);
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.Margin = new Thickness(0, 2, 0, 0);
            hero.Children.Add(c);
            Add(hero, 18);
        }
    }
}
