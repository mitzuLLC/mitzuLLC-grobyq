using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Grobyq
{
    // "For AerialOS users only": grobyq starts on AerialOS, or anywhere with a license signed with "anyos".
    // Detection reads markers the AerialOS build (or AerialOS-marker.reg) writes. Rules (3.6.3):
    //  - the marker key counts only with Product = "AerialOS" (REG_SZ) and, when present, a Version >= 6.0 - an empty
    //    or foreign key no longer passes; it is read from the 64-bit view and, if a 32-bit regedit imported it, from
    //    WOW6432Node;
    //  - OEM / registration strings count only as the exact product name "AerialOS" (a word, not the substring
    //    "aerial": "Aerial Photography LLC" as the registered organization does not pass), the support URL only with
    //    the AerialOS site.
    // Any offline marker can be copied by hand, so this is a distribution gate; the signed HWID license stays the lock.
    internal static class AerialOs
    {
        public const int MinMajor = 6;                        // the oldest AerialOS a marker may name
        public sealed class Check { public string Where = "", Value = "", Note = ""; public bool Hit; }

        private static bool? _cached;

        public static bool Detected()
        {
            if (!_cached.HasValue) { bool hit = false; foreach (var c in Checks()) if (c.Hit) { hit = true; break; } _cached = hit; }
            return _cached.Value;
        }

        // ---- pure rules (the tests call these) ----
        public static bool MarkerOk(object product, object version, out string note)
        {
            note = "";
            var ps = product as string;
            if (ps == null) { note = product == null ? "no Product value" : "Product is not a text value"; return false; }
            if (!string.Equals(ps.Trim(), "AerialOS", StringComparison.OrdinalIgnoreCase)) { note = "Product must be AerialOS"; return false; }
            if (version == null) return true;                 // older builds wrote Product only
            var vs = version as string;
            Version v;
            if (vs == null || !Version.TryParse(vs.Trim(), out v)) { note = "Version is not a version number"; return false; }
            if (v.Major < MinMajor) { note = "AerialOS {0}.0 or newer is required"; return false; }
            return true;
        }

        public static bool NameOk(string v)
        {
            return !string.IsNullOrEmpty(v) && System.Text.RegularExpressions.Regex.IsMatch(v, @"(?<![A-Za-z0-9])AerialOS(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static bool UrlOk(string v)
        {
            return !string.IsNullOrEmpty(v) && (v.IndexOf("aerialos.vercel.app", StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("dsc.gg/aerialos", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static List<Check> Checks()
        {
            var res = new List<Check>();
            // 1. The AerialOS marker key: 64-bit view first, then the 32-bit one (a 32-bit regedit writes WOW6432Node).
            var k = new Check { Where = @"HKLM\SOFTWARE\AerialOS" };
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var a = hk.OpenSubKey(@"SOFTWARE\AerialOS", false))
                    {
                        if (a == null) continue;
                        object prod = a.GetValue("Product"), ver = a.GetValue("Version");
                        string note;
                        bool ok = MarkerOk(prod, ver, out note);
                        k.Value = Convert.ToString(prod ?? "") + (ver != null ? " " + Convert.ToString(ver) : "") + (view == RegistryView.Registry32 ? "  (WOW6432Node)" : "");
                        k.Note = note;
                        if (ok) { k.Hit = true; break; }
                    }
                }
                catch { }
            }
            res.Add(k);

            RegistryKey hklm = null;
            try { hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); } catch { }
            if (hklm == null) return res;
            using (hklm)
            {
                // 2. OEM information (System > About), registration and edition name: the exact word AerialOS only.
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", "Manufacturer", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", "Model", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation", "SupportURL", true);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOrganization", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOwner", false);
                Add(res, hklm, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", false);
            }
            return res;
        }

        private static void Add(List<Check> res, RegistryKey hklm, string path, string name, bool url)
        {
            var c = new Check { Where = @"HKLM\" + path + @"\" + name };
            try
            {
                using (var k = hklm.OpenSubKey(path, false))
                    if (k != null) c.Value = Convert.ToString(k.GetValue(name) ?? "");
            }
            catch { }
            c.Hit = url ? UrlOk(c.Value) : NameOk(c.Value);
            res.Add(c);
        }

        // grobyq.exe /aerial : what the check sees on this PC, and what makes it pass.
        public static string Report()
        {
            var lines = new List<string>();
            lines.Add(Detected() ? L.T("AerialOS detected.") : L.T("AerialOS not detected."));
            lines.Add("");
            foreach (var c in Checks())
                lines.Add((c.Hit ? "✔  " : "·  ") + c.Where + " = " + (c.Value.Length == 0 ? "—" : c.Value) + (c.Note.Length > 0 ? "   (" + L.F(c.Note, MinMajor) + ")" : ""));
            if (!Detected())
            {
                lines.Add("");
                lines.Add(L.F("Needed: HKLM\\SOFTWARE\\AerialOS with Product = AerialOS and Version {0}.0 or newer (AerialOS-marker.reg, run as administrator), or a license issued for any Windows.", MinMajor));
            }
            return string.Join("\n", lines);
        }

        // One rule for every launch mode that uses grobyq (panel, tray, /check, /import): AerialOS, or an "anyos" license.
        public static bool Allowed(LicResult r) { return !Brand.AerialOnly || Detected() || (r != null && r.Ok && r.AnyOs); }
    }

    // Shown instead of the app on a PC without AerialOS (and without an "anyos" license).
    internal sealed class AerialOnlyForm : Form
    {
        private Label _hint;

        public static void ShowGate() { using (var f = new AerialOnlyForm()) f.ShowDialog(); }

        private AerialOnlyForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            string fam = L.Family;
            Text = Brand.Name + " - AerialOS";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 300);
            BackColor = LicenseForm.CBg; ForeColor = LicenseForm.CFg; Font = new Font(fam, 9f);
            try { Icon = Brand.AppIcon; } catch { }
            HandleCreated += (s, e) => Theme.DarkTitle(this);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            var top = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = LicenseForm.CBg2 };
            Controls.Add(top);
            var glyph = Brand.Img("glyph_40.png");
            if (glyph != null) top.Controls.Add(new PictureBox { Image = glyph, SizeMode = PictureBoxSizeMode.Zoom, BackColor = LicenseForm.CBg2, Location = new Point(16, 12), Size = new Size(30, 46) });
            top.Controls.Add(new Label { Text = Brand.Name, AutoSize = true, BackColor = LicenseForm.CBg2, ForeColor = LicenseForm.CFg, Font = new Font(fam, 17f, FontStyle.Bold), Location = new Point(52, 6) });
            top.Controls.Add(new Label { Text = "AerialOS EDITION", AutoSize = true, UseMnemonic = false, BackColor = LicenseForm.CBg2, ForeColor = LicenseForm.CAcc, Font = new Font(fam, 8.5f, FontStyle.Bold), Location = new Point(56, 44) });
            top.Controls.Add(new Label { Text = "by mitzu LLC", AutoSize = true, BackColor = LicenseForm.CBg2, ForeColor = LicenseForm.CDim, Location = new Point(440, 44) });

            Controls.Add(new Label { Text = "for AerialOS users only", AutoSize = true, UseMnemonic = false, ForeColor = LicenseForm.CFg, Font = new Font(fam, 15f, FontStyle.Bold), Location = new Point(20, 84) });
            Controls.Add(new Label
            {
                AutoSize = false, Size = new Size(500, 70), Location = new Point(21, 122), ForeColor = LicenseForm.CDim, UseMnemonic = false,
                Text = L.T("grobyq runs only on AerialOS - the gaming Windows build by zero_unflesh & mitzu LLC. This PC is not running AerialOS.") + "\n" +
                       L.T("Install AerialOS to use grobyq. Questions: the AerialOS Discord.")
            });

            var links = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = LicenseForm.CBg, Location = new Point(20, 196), Size = new Size(500, 24) };
            Controls.Add(links);
            links.Controls.Add(Link("aerialos.vercel.app", Brand.AerialUrl));
            links.Controls.Add(Link("dsc.gg/aerialos", Brand.AerialDiscord));

            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = LicenseForm.CBg, Location = new Point(20, 238), Size = new Size(500, 40) };
            Controls.Add(row);
            var close = new Btn(L.T("Close"), Btn.Style.Accent, 120) { BackColor = LicenseForm.CBg, Margin = new Padding(0, 0, 10, 0) }; close.Height = 36;
            close.Click += (s, e) => Close();
            row.Controls.Add(close);
            var copy = new Btn(L.T("Copy HWID"), Btn.Style.Ghost, 120) { BackColor = LicenseForm.CBg, Margin = new Padding(0, 0, 10, 0) }; copy.Height = 36;
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(Licensing.Hwid()); _hint.ForeColor = LicenseForm.COk; _hint.Text = L.T("Copied to the clipboard."); }
                catch { _hint.ForeColor = LicenseForm.CBad; _hint.Text = L.T("Clipboard is busy, try again."); }
            };
            row.Controls.Add(copy);
            _hint = new Label { AutoSize = true, ForeColor = LicenseForm.COk, Margin = new Padding(4, 10, 0, 0), UseMnemonic = false };
            row.Controls.Add(_hint);
            ActiveControl = close;
        }

        private static LinkLabel Link(string text, string url)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 18, 0), LinkColor = LicenseForm.CAcc, ActiveLinkColor = LicenseForm.CFg, VisitedLinkColor = LicenseForm.CAcc, LinkBehavior = LinkBehavior.HoverUnderline };
            l.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(url); } catch { } };
            return l;
        }
    }
}
