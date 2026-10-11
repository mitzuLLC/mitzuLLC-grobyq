using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Grobyq
{
    internal sealed class LicenseForm : Form
    {
        internal static readonly Color CBg   = Color.FromArgb(16, 16, 19);
        internal static readonly Color CBg2  = Color.FromArgb(26, 26, 31);
        internal static readonly Color CBg3  = Color.FromArgb(34, 34, 40);
        internal static readonly Color CFg   = Color.FromArgb(230, 230, 235);
        internal static readonly Color CDim  = Color.FromArgb(132, 132, 145);
        internal static readonly Color CAcc  = Color.FromArgb(88, 176, 255);
        internal static readonly Color COk   = Color.FromArgb(84, 214, 138);
        internal static readonly Color CBad  = Color.FromArgb(218, 92, 92);
        internal static readonly Color CLine = Color.FromArgb(58, 58, 68);

        private readonly bool _hwidOnly;
        private Label _status, _info, _hint;
        private Label _hwidBox;
        private Btn _copy, _retry, _exit;
        private System.Windows.Forms.Timer _closeTimer;
        private bool _licensed;

        private LicenseForm(bool hwidOnly) { _hwidOnly = hwidOnly; Build(); }

        public static bool Gate(bool tray)
        {
            if (tray)
            {
                // Network is often late at logon: wait quietly up to ~60s (spec A6),
                // show the window only if still not licensed.
                for (int i = 0; i < 7; i++)
                {
                    var r = Licensing.Validate();
                    if (r.Ok) return true;
                    if (!r.Net) break;
                    if (i < 6) Thread.Sleep(9000);
                }
            }
            using (var f = new LicenseForm(false)) { f.ShowDialog(); return f._licensed; }
        }

        public static void ShowHwidOnly() { using (var f = new LicenseForm(true)) f.ShowDialog(); }

        private void Build()
        {
            // Fixed 96-DPI layout; WinForms scales it on 125 / 150 % displays. Texts wrap for longer translations.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            string fam = L.Family;
            Text = _hwidOnly ? Brand.Name + " - HWID" : Brand.Name + " - " + L.T("license");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, _hwidOnly ? 276 : 338);
            BackColor = CBg; ForeColor = CFg; Font = new Font(fam, 9f);
            try { Icon = Brand.AppIcon; } catch { }
            HandleCreated += (s, e) => Theme.DarkTitle(this);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { _licensed = false; Close(); } };

            var top = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = CBg2 };
            Controls.Add(top);
            var glyph = Brand.Img("glyph_40.png");
            if (glyph != null) top.Controls.Add(new PictureBox { Image = glyph, SizeMode = PictureBoxSizeMode.Zoom, BackColor = CBg2, Location = new Point(16, 12), Size = new Size(30, 46) });
            top.Controls.Add(new Label { Text = Brand.Name, AutoSize = true, BackColor = CBg2, ForeColor = CFg,
                Font = new Font(fam, 17f, FontStyle.Bold), Location = new Point(52, 6) });
            top.Controls.Add(new Label { Text = _hwidOnly ? L.T("THIS PC - HWID") : L.T("LICENSE CHECK - HWID"), AutoSize = true, UseMnemonic = false,
                BackColor = CBg2, ForeColor = CAcc, Font = new Font(fam, 8.5f, FontStyle.Bold), Location = new Point(56, 44) });
            top.Controls.Add(new Label { Text = "by mitzu LLC", AutoSize = true, BackColor = CBg2, ForeColor = CDim, Location = new Point(440, 44) });

            int y = 84;
            if (!_hwidOnly)
            {
                _status = new Label { AutoSize = true, ForeColor = CDim, Location = new Point(20, y), UseMnemonic = false,
                    Font = new Font(fam, 12f, FontStyle.Bold), Text = L.T("Checking the license list...") };
                Controls.Add(_status);
                _info = new Label { AutoSize = false, Size = new Size(500, 62), Location = new Point(21, y + 30), ForeColor = CDim, UseMnemonic = false };
                Controls.Add(_info);
                y += 100;
            }
            else
            {
                Controls.Add(new Label { AutoSize = false, Size = new Size(500, 58), Location = new Point(21, y), ForeColor = CDim, UseMnemonic = false,
                    Text = L.F("Send this HWID to the developer to activate {0} on this PC. After activation press Retry in the license window.", Brand.Name) });
                y += 64;
            }

            Controls.Add(new Label { Text = L.T("YOUR HWID"), AutoSize = true, ForeColor = CDim, UseMnemonic = false,
                Font = new Font(fam, 8f, FontStyle.Bold), Location = new Point(20, y) });
            // static value: no caret, no typing; copying goes through the Copy HWID button
            _hwidBox = new Label { Text = Licensing.Hwid(), TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, UseMnemonic = false,
                AutoEllipsis = true, Font = new Font("Consolas", 12f), BackColor = CBg3, ForeColor = CFg, BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(20, y + 20), Size = new Size(500, 28) };
            Controls.Add(_hwidBox);

            _hint = new Label { AutoSize = true, ForeColor = COk, Location = new Point(20, y + 54), Text = "", UseMnemonic = false };
            Controls.Add(_hint);

            // Buttons in a row that grows with the translated text.
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = CBg,
                Location = new Point(20, y + 84), Size = new Size(500, 40), Padding = Padding.Empty, Margin = Padding.Empty };
            Controls.Add(row);
            _copy = MakeBtn(L.T("Copy HWID"), 150, Btn.Style.Accent); _copy.Click += (s, e) => Copy(); row.Controls.Add(_copy);
            if (_hwidOnly)
            {
                _exit = MakeBtn(L.T("Close"), 110, Btn.Style.Ghost); _exit.Click += (s, e) => Close(); row.Controls.Add(_exit);
            }
            else
            {
                _retry = MakeBtn(L.T("Retry"), 110, Btn.Style.Normal); _retry.Click += (s, e) => Check(); row.Controls.Add(_retry);
                _exit = MakeBtn(L.T("Exit"), 100, Btn.Style.Ghost); _exit.Click += (s, e) => { _licensed = false; Close(); }; row.Controls.Add(_exit);
                _closeTimer = new System.Windows.Forms.Timer { Interval = 900 };
                _closeTimer.Tick += (s, e) => { _closeTimer.Stop(); _licensed = true; DialogResult = DialogResult.OK; Close(); };
                Shown += (s, e) => Check();
            }
            var lnk = new LinkLabel { Text = "x.com/grobPC", AutoSize = true, Margin = new Padding(6, 10, 0, 0),
                LinkColor = CDim, ActiveLinkColor = CAcc, VisitedLinkColor = CDim, LinkBehavior = LinkBehavior.HoverUnderline };
            lnk.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start("https://x.com/grobPC"); } catch { } };
            row.Controls.Add(lnk);
        }

        private static Btn MakeBtn(string t, int w, Btn.Style look)
        {
            var b = new Btn(t, look, w) { BackColor = CBg, Margin = new Padding(0, 0, 10, 0) };
            b.Height = 36;
            return b;
        }

        private void Copy()
        {
            try { Clipboard.SetText(_hwidBox.Text); _hint.ForeColor = COk; _hint.Text = L.T("Copied to the clipboard."); }
            catch { _hint.ForeColor = CBad; _hint.Text = L.T("Clipboard is busy, try again."); }
        }

        private void Check()
        {
            _retry.Enabled = false; _status.ForeColor = CDim; _status.Text = L.T("Checking the license list..."); _info.Text = ""; Refresh();
            var th = new Thread(() =>
            {
                LicResult r;
                try { r = Licensing.Validate(); }
                catch (Exception ex) { r = new LicResult { Ok = false, Message = L.T("License check error:") + " " + ex.Message }; }
                try { BeginInvoke((Action)(() => Show(r))); } catch { }
            });
            th.IsBackground = true; th.Start();
        }

        private void Show(LicResult r)
        {
            _retry.Enabled = true;
            if (r.Ok)
            {
                _status.ForeColor = COk; _status.Text = L.T("License valid.");
                _info.ForeColor = CDim; _info.Text = L.T(r.Message) + Program.Dates(r, true);
                _copy.Enabled = false; _retry.Enabled = false; _exit.Enabled = false; _closeTimer.Start();
            }
            else
            {
                _status.ForeColor = CBad; _status.Text = L.T("License check failed.");
                _info.ForeColor = CFg; _info.Text = L.T(r.Message);
            }
        }

        internal static Icon Dot(Color c)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                using (var halo = new SolidBrush(Color.FromArgb(70, c))) g.FillEllipse(halo, 0, 0, 16, 16);
                using (var br = new SolidBrush(c)) g.FillEllipse(br, 3, 3, 10, 10);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
