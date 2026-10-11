using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Grobyq
{
    // Home: the product at a glance. Wordmark + version, the known system facts and the license sheet.
    // Centered column; community links live on About; the optimizer state is not a badge here.
    internal sealed class HomePage : Page
    {
        private readonly MainForm M;
        private FactGrid _facts;
        private SheetRow _hwid, _status, _source, _expires, _purchased, _list;
        private StatusDot _dot;

        public HomePage(MainForm m)
        {
            M = m;
            ColumnMax = 800;
            BuildHero();

            // ---- system overview: four facts the app already reads (WMI) ----
            Heading(L.T("System overview"), true);
            _facts = Box().Stack(new FactGrid());
            UpdateSys();

            // ---- license: same width as the overview, two columns so the card has no dead half ----
            Heading(L.T("License"), false);
            var lc = Box(8);
            var grid = lc.Stack(new SheetGrid());
            _hwid = grid.Add(new SheetRow("HWID", null, 30), true);
            _dot = new StatusDot();
            _status = grid.Add(new SheetRow(L.T("Status"), _dot, 30));
            _expires = grid.Add(new SheetRow(L.T("Expires"), null, 30));
            _source = grid.Add(new SheetRow(L.T("Source"), null, 30));
            _purchased = grid.Add(new SheetRow(L.T("Purchased"), null, 30));
            _list = grid.Add(new SheetRow(L.T("List"), null, 30));
            _list.Tip = Licensing.ListUrl;
            var st = new Strip(Theme.D(52));
            var copy = st.Add(new Btn(L.T("Copy HWID"), Btn.Style.Accent));
            copy.Click += (s, e) => CopyHwid(copy);
            st.Add(new Btn(L.T("Recheck license"), Btn.Style.Outline)).Click += (s, e) => Recheck();
            lc.Stack(st);
            Add(new TextBlock(L.T("Send the HWID to the developer and press Recheck license after activation. A signed offline token keeps grobyq working for about 48 hours without internet; a revoked license stays revoked."), Theme.Faint, 9f), 8)
                .Margin = new Padding(Theme.D(4), 0, Theme.D(4), Theme.D(8));

            M.SysLoaded += UpdateSys;
        }

        private void BuildHero()
        {
            var hero = Add(new Hero { Height = Theme.D(94) }, 2);
            var t = Theme.Lbl(Brand.Name, Theme.Bold(26f), Theme.Fg); t.BackColor = Color.Transparent;
            // version as its own accent tag beside the wordmark, so it is visible at a glance
            var tag = Theme.Lbl("v" + Brand.Version, Theme.Semi(10f), Theme.Acc); tag.BackColor = Color.Transparent;
            var v = Theme.Lbl(L.T("Core-aware latency tuning"), Theme.F(10.5f), Theme.Dim); v.BackColor = Color.Transparent;   // the version is the tag above
            hero.Controls.AddRange(new Control[] { t, tag, v });
            hero.Layout += (s, e) =>
            {
                t.Location = new Point(0, Theme.D(8));
                tag.Location = new Point(t.Right + Theme.D(8), t.Bottom - tag.Height - Theme.D(8));
                v.Location = new Point(Theme.D(3), t.Bottom + Theme.D(2));
            };
        }

        private void UpdateSys()
        {
            var s = M.Sys;
            if (s == null) { _facts.Set(new string[0][]); return; }
            _facts.Set(new[]
            {
                new[] { "chip", L.T("Processor"), s.Cpu },
                new[] { "gpu", L.T("Graphics"), s.Gpu },
                new[] { "ram", L.T("Memory"), s.Ram },
                new[] { "board", L.T("Motherboard"), s.Board },
            });
        }

        // ------------------------------------------------------------------ license (same data as before)
        private void FillLicense()
        {
            var r = Licensing.LastKnown();
            _hwid.Value = Licensing.Hwid();
            if (r == null) { _dot.Kind = 0; _status.Value = L.T("not checked yet"); }
            else if (r.Ok && string.Equals(r.Source, "offline token", StringComparison.OrdinalIgnoreCase)) { _dot.Kind = 2; _status.Value = L.T("licensed · offline cache"); }
            else if (r.Ok) { _dot.Kind = 1; _status.Value = L.T("licensed"); }
            else { _dot.Kind = 3; _status.Value = L.T("not licensed") + (string.IsNullOrEmpty(r.Message) ? "" : "  ·  " + L.T(r.Message)); }
            _source.Value = r == null ? "" : r.Source;
            _expires.Value = r == null ? "" : r.Expires.HasValue ? r.Expires.Value.ToString("yyyy-MM-dd") : r.Ok ? L.T("lifetime") : "";
            _purchased.Value = r == null || !r.Purchase.HasValue ? "" : r.Purchase.Value.ToString("yyyy-MM-dd");
            string host = Licensing.ListUrl;
            try { host = new Uri(Licensing.ListUrl).Host; } catch { }
            _list.Value = host;
        }

        private void CopyHwid(Btn b)
        {
            try { Clipboard.SetText(Licensing.Hwid()); M.Log("[LIC] HWID copied"); }
            catch { Dlg.Info(this, L.T("Clipboard is busy, try again.")); return; }
            Flash(b, L.T("Copied"));
        }

        private void Recheck()
        {
            M.Log("[LIC] checking...");
            _dot.Kind = 0; _status.Value = L.T("checking…");
            var th = new Thread(() =>
            {
                LicResult r; try { r = Licensing.Validate(); } catch (Exception ex) { r = new LicResult { Message = ex.Message }; }
                try { BeginInvoke((Action)(() => { M.Log("[LIC] " + r.Message); FillLicense(); })); } catch { }
            }) { IsBackground = true };
            th.Start();
        }

        public override void OnEnter() { FillLicense(); }
    }
}
