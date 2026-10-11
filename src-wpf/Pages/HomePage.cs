using System;
using System.Windows;
using System.Windows.Controls;

namespace Grobyq.Pages
{
    // Home: the product at a glance. Wordmark + version tag, the known system facts and the license sheet
    // (the v3.6.8 Home). Centered column; community links live on About.
    public sealed class HomePage : PageBase
    {
        private readonly FactGrid _facts;
        private readonly SheetRow _hwid, _status, _expires, _source, _purchased, _list;
        private readonly Button _copy;

        public HomePage() : base(800)
        {
            BuildHero();

            // ---- system overview: four facts the app reads once (WMI, on a worker) ----
            Heading(L.T("System overview"), true);
            var sc = Box();
            _facts = sc.Add(new FactGrid { Margin = new Thickness(0, 6, 0, 4) });
            UpdateSys();

            // ---- license: same width as the overview, two columns so the card has no dead half ----
            Heading(L.T("License"), false);
            var lc = Box(8);
            var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _hwid = Put(g, new SheetRow("HWID"), 0, 0, 3);
            _status = Put(g, new SheetRow(L.T("Status"), true), 1, 0, 1);
            _expires = Put(g, new SheetRow(L.T("Expires")), 1, 2, 1);
            _source = Put(g, new SheetRow(L.T("Source")), 2, 0, 1);
            _purchased = Put(g, new SheetRow(L.T("Purchased")), 2, 2, 1);
            _list = Put(g, new SheetRow(L.T("List")), 3, 0, 1);
            _list.Tip = Licensing.ListUrl;
            lc.Add(g);

            _copy = Ui.Btn(L.T("Copy HWID"), "BtnAccent");
            _copy.Click += (s, e) => CopyHwid();
            var recheck = Ui.Btn(L.T("Recheck license"), "BtnOutline");
            recheck.Click += (s, e) => Shell.RecheckLicense(true);
            var strip = Ui.Strip(52, _copy, recheck);
            strip.Margin = new Thickness(0, 10, 0, 0);
            lc.Add(strip);

            var hint = Ui.Text(L.T("Send the HWID to the developer and press Recheck license after activation. A signed offline token keeps grobyq working for about 48 hours without internet; a revoked license stays revoked."), 9, "Faint");
            hint.Margin = new Thickness(4, 0, 4, 0);
            Add(hint, 8);

            Loaded += (s, e) => { Shell.SysLoaded += UpdateSys; Shell.LicenseChanged += FillLicense; UpdateSys(); FillLicense(); };
            Unloaded += (s, e) => { Shell.SysLoaded -= UpdateSys; Shell.LicenseChanged -= FillLicense; };
        }

        private static SheetRow Put(Grid g, SheetRow r, int row, int col, int span)
        {
            Grid.SetRow(r, row); Grid.SetColumn(r, col); Grid.SetColumnSpan(r, span);
            g.Children.Add(r);
            return r;
        }

        // Wordmark + the version as its own accent tag beside it, the tagline below.
        private void BuildHero()
        {
            var hero = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Height = 94 };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var t = Ui.Bold(Brand.Name, 26, "Fg");
            var tag = Ui.Semi("v" + Brand.Version, 10, "Acc");
            tag.VerticalAlignment = VerticalAlignment.Bottom;
            tag.Margin = new Thickness(8, 0, 0, 9);
            line.Children.Add(t);
            line.Children.Add(tag);
            hero.Children.Add(line);
            var v = Ui.Text(L.T("Core-aware latency tuning"), 10.5, "Dim", false);
            v.Margin = new Thickness(3, 0, 0, 0);
            hero.Children.Add(v);
            Add(hero, 2);
        }

        private void UpdateSys()
        {
            var s = Shell.Sys;
            if (s == null) { _facts.Set(new string[0][]); return; }
            _facts.Set(new[]
            {
                new[] { "chip", L.T("Processor"), s.Cpu },
                new[] { "gpu", L.T("Graphics"), s.Gpu },
                new[] { "ram", L.T("Memory"), s.Ram },
                new[] { "board", L.T("Motherboard"), s.Board },
            });
        }

        // ------------------------------------------------------------------ license (same data as v3)
        private void FillLicense()
        {
            var r = Licensing.LastKnown();
            _hwid.Value = GrobLic.HwidIfReady ?? L.T("reading…");
            if (Shell.LicenseChecking) { _status.Dot = 0; _status.Value = L.T("checking…"); }
            else if (r == null) { _status.Dot = 0; _status.Value = L.T("not checked yet"); }
            else if (r.Ok && string.Equals(r.Source, "offline token", StringComparison.OrdinalIgnoreCase)) { _status.Dot = 2; _status.Value = L.T("licensed · offline cache"); }
            else if (r.Ok) { _status.Dot = 1; _status.Value = L.T("licensed"); }
            else { _status.Dot = 3; _status.Value = L.T("not licensed") + (string.IsNullOrEmpty(r.Message) ? "" : "  ·  " + L.T(r.Message)); }
            _source.Value = r == null ? "" : r.Source;
            _expires.Value = r == null ? "" : r.Expires.HasValue ? r.Expires.Value.ToString("yyyy-MM-dd") : r.Ok ? L.T("lifetime") : "";
            _purchased.Value = r == null || !r.Purchase.HasValue ? "" : r.Purchase.Value.ToString("yyyy-MM-dd");
            string host = Licensing.ListUrl;
            try { host = new Uri(Licensing.ListUrl).Host; } catch { }
            _list.Value = host;
        }

        private void CopyHwid()
        {
            try { Clipboard.SetText(GrobLic.HwidIfReady ?? Licensing.Hwid()); AppLog.Add("[LIC] HWID copied"); }
            catch { Dlg.Info(null, L.T("Clipboard is busy, try again.")); return; }
            Ui.Flash(_copy, L.T("Copied"));
        }

        public override void OnEnter() { UpdateSys(); FillLicense(); }
    }
}
