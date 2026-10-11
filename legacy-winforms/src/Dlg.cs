using System;
using System.Drawing;
using System.Windows.Forms;

namespace Grobyq
{
    // Dark replacement for MessageBox (the system one is always light) and a one-line text prompt.
    internal static class Dlg
    {
        // Yes / No question. Enter = Yes, Esc = No.
        public static bool Ask(IWin32Window owner, string text, bool danger = false)
        {
            return Show(owner, text, danger, L.T("Yes"), L.T("No")) == 0;
        }

        public static void Info(IWin32Window owner, string text)
        {
            Show(owner, text, false, "OK");
        }

        // Returns the index of the pressed button, -1 when the window was closed.
        public static int Show(IWin32Window owner, string text, bool danger, params string[] buttons)
        {
            using (var f = new Form
            {
                Text = Brand.Name,
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, KeyPreview = true,
                BackColor = Theme.Bg, ForeColor = Theme.Fg, Font = Theme.F(9.5f), AutoScaleMode = AutoScaleMode.None,
            })
            {
                Theme.DarkForm(f);
                try { f.Icon = Brand.AppIcon; } catch { }
                int w = Theme.D(540), pad = Theme.D(22);
                var font = Theme.F(10f);
                var sz = TextRenderer.MeasureText(text ?? "", font, new Size(w - pad * 2, 4000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                var bar = new Panel { Dock = DockStyle.Top, Height = Theme.D(4), BackColor = danger ? Theme.Bad : Theme.Acc };
                var lbl = new Label { Text = text, Font = font, ForeColor = Theme.Fg, BackColor = Theme.Bg, AutoSize = false, UseMnemonic = false,
                                      Location = new Point(pad, pad + Theme.D(4)), Size = new Size(w - pad * 2, sz.Height + Theme.D(6)) };
                int by = lbl.Bottom + Theme.D(18);
                f.ClientSize = new Size(w, by + Theme.D(34) + pad);
                f.Controls.Add(bar); f.Controls.Add(lbl);
                int result = -1;
                int x = w - pad;
                for (int i = buttons.Length - 1; i >= 0; i--)
                {
                    int idx = i;
                    var b = new Btn(buttons[i], i == 0 ? (danger ? Btn.Style.Danger : Btn.Style.Accent) : Btn.Style.Normal, Theme.D(96)) { Height = Theme.D(34) };
                    x -= b.Width;
                    b.Location = new Point(x, by);
                    x -= Theme.D(10);
                    b.Click += (s, e) => { result = idx; f.Close(); };
                    f.Controls.Add(b);
                    if (i == 0) f.Shown += (s, e) => b.Focus();
                }
                f.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) { result = buttons.Length > 1 ? buttons.Length - 1 : 0; f.Close(); }
                    else if (e.KeyCode == Keys.Enter) { result = 0; f.Close(); }
                };
                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
                return result;
            }
        }

        // Single-line input; null = cancelled.
        public static string Prompt(IWin32Window owner, string title, string text, string def)
        {
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Bg, ForeColor = Theme.Fg,
                ClientSize = new Size(Theme.D(460), Theme.D(150)), Font = Theme.F(9.5f), ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.None,
            })
            {
                Theme.DarkForm(f);
                var l = Theme.Lbl(text, Theme.F(9.5f), Theme.Dim); l.Location = new Point(Theme.D(18), Theme.D(18));
                var tb = Theme.Box(Theme.D(424), def); tb.Location = new Point(Theme.D(18), Theme.D(46));
                var cancel = new Btn(L.T("Cancel"), Btn.Style.Normal, Theme.D(96)) { Height = Theme.D(34) };
                var ok = new Btn("OK", Btn.Style.Accent, Theme.D(96)) { Height = Theme.D(34) };
                cancel.Location = new Point(Theme.D(442) - cancel.Width, Theme.D(98));
                ok.Location = new Point(cancel.Left - Theme.D(10) - ok.Width, Theme.D(98));
                bool done = false;
                ok.Click += (s, e) => { done = true; f.Close(); };
                cancel.Click += (s, e) => f.Close();
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { done = true; f.Close(); } if (e.KeyCode == Keys.Escape) f.Close(); };
                f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
                if (owner != null) f.ShowDialog(owner); else f.ShowDialog();
                return done ? tb.Text : null;
            }
        }
    }
}
