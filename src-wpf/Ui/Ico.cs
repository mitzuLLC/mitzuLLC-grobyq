using System;
using System.Windows;
using System.Windows.Media;

namespace Grobyq
{
    // The small line icons of the WinForms build (Icons.Draw), drawn in the same 16 x 16 design box with round caps,
    // so every glyph keeps its exact shape: nav items, fact grid, link cards.
    public sealed class Ico : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            "Kind", typeof(string), typeof(Ico), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            "Stroke", typeof(Brush), typeof(Ico), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
            "StrokeWidth", typeof(double), typeof(Ico), new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Kind { get { return (string)GetValue(KindProperty); } set { SetValue(KindProperty, value); } }
        public Brush Stroke { get { return (Brush)GetValue(StrokeProperty); } set { SetValue(StrokeProperty, value); } }
        public double StrokeWidth { get { return (double)GetValue(StrokeWidthProperty); } set { SetValue(StrokeWidthProperty, value); } }

        public Ico() { SnapsToDevicePixels = false; IsHitTestVisible = false; }

        private DrawingContext _dc;
        private Pen _pen;
        private Brush _fill;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double k = Math.Max(0.01, w / 16.0);
            dc.PushTransform(new ScaleTransform(k, h / 16.0));
            _dc = dc;
            _fill = Stroke ?? Brushes.White;
            _pen = new Pen(_fill, StrokeWidth / k) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            try { Draw(Kind ?? ""); }
            finally { dc.Pop(); _dc = null; _pen = null; }
        }

        private void Draw(string kind)
        {
            switch (kind)
            {
                case "threads":
                    Ln(5.5, 4, 14, 4); Ln(5.5, 8, 12, 8); Ln(5.5, 12, 14, 12);
                    Dot(1.2, 2.8, 2.4, 2.4); Dot(1.2, 6.8, 2.4, 2.4); Dot(1.2, 10.8, 2.4, 2.4);
                    break;
                case "chip":
                case "affinity":
                    RRect(3.5, 3.5, 9, 9, 1.5);
                    Rect(6.2, 6.2, 3.6, 3.6);
                    foreach (double t in new[] { 6.0, 10.0 })
                    {
                        Ln(t, 1, t, 3.5); Ln(t, 12.5, t, 15);
                        Ln(1, t, 3.5, t); Ln(12.5, t, 15, t);
                    }
                    break;
                case "gpu":
                    RRect(1.5, 2.5, 13, 9, 1.2);
                    Ln(8, 11.5, 8, 14); Ln(5, 14, 11, 14);
                    break;
                case "about":
                    Ring(1.5, 1.5, 13, 13);
                    Ln(8, 7.2, 8, 11.5); Dot(7.1, 3.9, 1.8, 1.8);
                    break;
                case "ram":
                    RRect(1.5, 4.5, 13, 6.5, 1);
                    Rect(3.8, 6.6, 2, 2.4); Rect(7, 6.6, 2, 2.4); Rect(10.2, 6.6, 2, 2.4);
                    Ln(4, 11, 4, 13); Ln(8, 11, 8, 13); Ln(12, 11, 12, 13);
                    break;
                case "board":
                    RRect(2, 2, 12, 12, 1.5);
                    Rect(4.6, 4.6, 4, 4); Ring(10, 10, 1.8, 1.8); Ln(10.5, 4.6, 11.5, 4.6);
                    break;
                case "log":
                    RRect(2.5, 1.5, 11, 13, 1.2);
                    Ln(5, 5, 11, 5); Ln(5, 8, 11, 8); Ln(5, 11, 9, 11);
                    break;
                case "settings":
                    Ring(5.2, 5.2, 5.6, 5.6);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        Ln(8 + Math.Cos(a) * 4.6, 8 + Math.Sin(a) * 4.6, 8 + Math.Cos(a) * 6.6, 8 + Math.Sin(a) * 6.6);
                    }
                    break;
                case "clean":
                    Poly(true, 6.5, 1.5, 7.9, 5.1, 11.5, 6.5, 7.9, 7.9, 6.5, 11.5, 5.1, 7.9, 1.5, 6.5, 5.1, 5.1);
                    Poly(true, 12, 9.5, 12.7, 11.3, 14.5, 12, 12.7, 12.7, 12, 14.5, 11.3, 12.7, 9.5, 12, 11.3, 11.3);
                    break;
                case "x":
                    Ln(3, 2.5, 13, 13.5); Ln(13, 2.5, 3, 13.5);
                    break;
                case "chat":
                    RRect(1.8, 2.5, 12.4, 8.8, 2.4);
                    Ln(5, 11.3, 4.2, 14); Ln(4.2, 14, 8, 11.3);
                    break;
                case "window":
                    RRect(1.8, 2.5, 12.4, 11, 2);
                    Ln(1.8, 5.8, 14.2, 5.8);
                    break;
                case "globe":
                    Ring(1.5, 1.5, 13, 13);
                    Ring(5, 1.5, 6, 13);
                    Ln(1.5, 8, 14.5, 8);
                    break;
                case "home":
                    Poly(false, 1.8, 7.6, 8, 2, 14.2, 7.6);
                    Poly(false, 3.6, 6.2, 3.6, 14, 12.4, 14, 12.4, 6.2);
                    Poly(false, 6.6, 14, 6.6, 10, 9.4, 10, 9.4, 14);
                    break;
                case "plane":
                    Poly(true, 1.5, 7.4, 14.5, 2, 12, 14, 7.6, 10.4);
                    Ln(7.6, 10.4, 14.5, 2);
                    Poly(false, 7.6, 10.4, 6.8, 14, 9.4, 11.9);
                    break;
                case "ext":
                    Ln(5, 11, 11.5, 4.5);
                    Poly(false, 6.5, 4.5, 11.5, 4.5, 11.5, 9.5);
                    break;
                case "save":
                    RRect(2, 2, 12, 12, 1.5);
                    Poly(false, 5, 2, 5, 6, 11, 6, 11, 2);
                    Rect(5, 9, 6, 5);
                    break;
                case "undo":
                    Poly(false, 5, 3.5, 2.5, 6, 5, 8.5);
                    Arc(2.5, 6, 13.5, 6, 5.5, 13.5);
                    break;
                case "bolt-dot":
                    Dot(4, 4, 8, 8);
                    break;
                case "power":
                    OpenRing(8, 8.7, 5.6, 40);
                    Ln(8, 1.8, 8, 7.4);
                    break;
                default:
                    Ring(3, 3, 10, 10);
                    break;
            }
        }

        // ---- primitives in design units (GDI+ rectangle semantics: x, y, width, height) ----
        private void Ln(double x1, double y1, double x2, double y2) { _dc.DrawLine(_pen, new Point(x1, y1), new Point(x2, y2)); }
        private void Rect(double x, double y, double w, double h) { _dc.DrawRectangle(null, _pen, new System.Windows.Rect(x, y, w, h)); }
        private void RRect(double x, double y, double w, double h, double r) { _dc.DrawRoundedRectangle(null, _pen, new System.Windows.Rect(x, y, w, h), r, r); }
        private void Ring(double x, double y, double w, double h) { _dc.DrawEllipse(null, _pen, new Point(x + w / 2, y + h / 2), w / 2, h / 2); }
        private void Dot(double x, double y, double w, double h) { _dc.DrawEllipse(_fill, null, new Point(x + w / 2, y + h / 2), w / 2, h / 2); }

        private void Poly(bool closed, params double[] xy)
        {
            if (xy.Length < 4) return;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(xy[0], xy[1]), false, closed);
                for (int i = 2; i + 1 < xy.Length; i += 2) c.LineTo(new Point(xy[i], xy[i + 1]), true, true);
            }
            g.Freeze();
            _dc.DrawGeometry(null, _pen, g);
        }

        // a circle open at the top by +-gapDeg (the power glyph)
        private void OpenRing(double cx, double cy, double r, double gapDeg)
        {
            double a1 = (-90 + gapDeg) * Math.PI / 180, a2 = (-90 - gapDeg) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), false, false);
                c.ArcTo(new Point(cx + r * Math.Cos(a2), cy + r * Math.Sin(a2)), new Size(r, r), 0, true, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            _dc.DrawGeometry(null, _pen, g);
        }

        // quadratic curve start -> end through a control point (the "undo" hook)
        private void Arc(double x1, double y1, double cx, double cy, double x2, double y2)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x1, y1), false, false);
                c.QuadraticBezierTo(new Point(cx, cy), new Point(x2, y2), true, true);
            }
            g.Freeze();
            _dc.DrawGeometry(null, _pen, g);
        }
    }
}
