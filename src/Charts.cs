// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace HDRDisplayCalibrate
{
    static class Draw
    {
        public static readonly Typeface Face = new("Segoe UI");
        public static FormattedText Txt(string s, double size, Brush b) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, b, 1.25);
        public static readonly Brush Axis = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        public static readonly Brush Grid = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
        public static readonly Brush Label = new SolidColorBrush(Color.FromRgb(170, 170, 170));
        public static Brush Of(string hex) => (Brush)new BrushConverter().ConvertFromString(hex);
    }

    /// <summary>CIE 1931 xy (or 1976 u'v') chromaticity diagram with gamut triangles.</summary>
    public sealed class ChromaticityChart : FrameworkElement
    {
        public sealed record Tri(string Name, (double x, double y) R, (double x, double y) G, (double x, double y) B, Brush Stroke, bool Fill = false, bool Dashed = false);
        public List<Tri> Triangles = new();
        public List<((double x, double y) p, Brush b, string label)> Points = new();
        public bool UV;

        static readonly (double x, double y)[] Locus =
        {
            (0.1741,0.0050),(0.1738,0.0049),(0.1733,0.0048),(0.1726,0.0048),(0.1714,0.0051),(0.1689,0.0069),(0.1644,0.0109),(0.1566,0.0177),
            (0.1440,0.0297),(0.1241,0.0578),(0.0913,0.1327),(0.0687,0.2007),(0.0454,0.2950),(0.0235,0.4127),(0.0082,0.5384),(0.0039,0.6548),
            (0.0139,0.7502),(0.0389,0.8120),(0.0743,0.8338),(0.1142,0.8262),(0.1547,0.8059),(0.1929,0.7816),(0.2296,0.7543),(0.2658,0.7243),
            (0.3016,0.6923),(0.3373,0.6589),(0.3731,0.6245),(0.4087,0.5896),(0.4441,0.5547),(0.4788,0.5202),(0.5125,0.4866),(0.5448,0.4544),
            (0.5752,0.4242),(0.6029,0.3965),(0.6270,0.3725),(0.6482,0.3514),(0.6658,0.3340),(0.6801,0.3197),(0.6915,0.3083),(0.7006,0.2993),
            (0.7079,0.2920),(0.7140,0.2859),(0.7190,0.2809),(0.7260,0.2740),(0.7300,0.2700),(0.7347,0.2653),
        };

        public ChromaticityChart() { Height = 460; FlowDirection = FlowDirection.LeftToRight; }   // charts never mirror in right-to-left languages

        (double, double) Conv((double x, double y) p) => UV ? Core.Gamut.Uv(p) : p;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, pad = 34;
            double maxX = UV ? 0.65 : 0.8, maxY = UV ? 0.62 : 0.9;
            double s = Math.Min((w - pad - 24) / maxX, (h - pad - 24) / maxY);    // room on top / right for the axis names
            Point P((double x, double y) c) { var (x, y) = Conv(c); return new Point(pad + x * s, h - pad - y * s); }
            Point Raw(double x, double y) => new(pad + x * s, h - pad - y * s);
            for (double g = 0; g <= maxX + 1e-9; g += 0.1)
            {
                dc.DrawLine(new Pen(Draw.Grid, 1), Raw(g, 0), Raw(g, maxY));
                dc.DrawText(Draw.Txt(g.ToString("0.0", CultureInfo.InvariantCulture), 10, Draw.Label), Raw(g, 0) + new Vector(-8, 4));
            }
            for (double g = 0; g <= maxY + 1e-9; g += 0.1)
            {
                dc.DrawLine(new Pen(Draw.Grid, 1), Raw(0, g), Raw(maxX, g));
                dc.DrawText(Draw.Txt(g.ToString("0.0", CultureInfo.InvariantCulture), 10, Draw.Label), Raw(0, g) + new Vector(-28, -7));
            }
            dc.DrawText(Draw.Txt(UV ? "u'" : "x", 12, Draw.Label), Raw(maxX, 0) + new Vector(6, -8));
            dc.DrawText(Draw.Txt(UV ? "v'" : "y", 12, Draw.Label), Raw(0, maxY) + new Vector(-4, -20));

            var locus = new StreamGeometry();
            using (var ctx = locus.Open())
            {
                ctx.BeginFigure(P(Locus[0]), true, true);
                foreach (var p in Locus.Skip(1)) ctx.LineTo(P(p), true, true);
            }
            var lg = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(1, 0) };
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(55, 60, 80, 255), 0));
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(55, 80, 255, 120), 0.5));
            lg.GradientStops.Add(new GradientStop(Color.FromArgb(55, 255, 80, 60), 1));
            dc.DrawGeometry(lg, new Pen(Draw.Axis, 1.2), locus);

            foreach (var t in Triangles)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(P(t.R), t.Fill, true); ctx.LineTo(P(t.G), true, true); ctx.LineTo(P(t.B), true, true); }
                var pen = new Pen(t.Stroke, t.Fill ? 2.2 : 1.4);
                if (t.Dashed) pen.DashStyle = DashStyles.Dash;
                Brush fill = null;
                if (t.Fill && t.Stroke is SolidColorBrush sb) fill = new SolidColorBrush(Color.FromArgb(40, sb.Color.R, sb.Color.G, sb.Color.B));
                dc.DrawGeometry(fill, pen, g);
            }
            var labelled = new List<Point>();
            foreach (var (p, b, label) in Points)
            {
                var at = P(p);
                dc.DrawEllipse(b, new Pen(Brushes.Black, 1), at, 4, 4);
                if (label == null) continue;
                // points that sit on top of each other (measured white on the target) get their labels stacked
                int near = labelled.Count(q => (q - at).Length < 14);
                dc.DrawText(Draw.Txt(label, 11, b), at + new Vector(6, -8 + 13 * near));
                labelled.Add(at);
            }
            double ly = 8;
            foreach (var t in Triangles)
            {
                double lx = w - 190;
                dc.DrawLine(new Pen(t.Stroke, 2) { DashStyle = t.Dashed ? DashStyles.Dash : DashStyles.Solid }, new Point(lx, ly + 8), new Point(lx + 22, ly + 8));
                dc.DrawText(Draw.Txt(t.Name, 11, Draw.Label), new Point(lx + 28, ly));
                ly += 18;
            }
        }
    }

    /// <summary>Line/scatter chart with optional log axes and a legend.</summary>
    public sealed class LineChart : FrameworkElement
    {
        public sealed record Series(string Name, List<(double x, double y)> Pts, Brush Brush, bool Dashed = false, bool Markers = false);
        public List<Series> Data = new();
        public bool LogX, LogY;
        public double? XMin, XMax, YMin, YMax;
        public string XLabel = "", YLabel = "";

        public LineChart() { Height = 300; FlowDirection = FlowDirection.LeftToRight; }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, l = 52, b = 34, t = 10, r = 12;
            var all = Data.SelectMany(s => s.Pts).Where(p => (!LogX || p.x > 0) && (!LogY || p.y > 0)).ToList();
            if (all.Count == 0) return;
            double Tx(double v) => LogX ? Math.Log10(v) : v; double Ty(double v) => LogY ? Math.Log10(v) : v;
            double x0 = Tx(XMin ?? all.Min(p => p.x)), x1 = Tx(XMax ?? all.Max(p => p.x)), y0 = Ty(YMin ?? all.Min(p => p.y)), y1 = Ty(YMax ?? all.Max(p => p.y));
            if (x1 - x0 < 1e-9) x1 = x0 + 1; if (y1 - y0 < 1e-9) y1 = y0 + 1;
            Point P(double x, double y) => new(l + (Tx(x) - x0) / (x1 - x0) * (w - l - r), h - b - (Ty(y) - y0) / (y1 - y0) * (h - b - t));
            IEnumerable<double> Ticks(double a, double z, bool log)
            {
                if (log) { for (int e = (int)Math.Floor(a); e <= Math.Ceiling(z); e++) yield return Math.Pow(10, e); yield break; }
                double step = Math.Pow(10, Math.Floor(Math.Log10((z - a) / 5)));
                if ((z - a) / step > 10) step *= 2; if ((z - a) / step > 10) step *= 2.5;
                for (double v = Math.Ceiling(a / step) * step; v <= z + 1e-9; v += step) yield return v;
            }
            string Fmt(double v) => Math.Abs(v) >= 100 ? v.ToString("0", CultureInfo.InvariantCulture) : Math.Abs(v) >= 1 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0.###", CultureInfo.InvariantCulture);
            foreach (var v in Ticks(x0, x1, LogX)) { var p = P(v, LogY ? Math.Pow(10, y0) : y0); dc.DrawLine(new Pen(Draw.Grid, 1), p, new Point(p.X, t)); dc.DrawText(Draw.Txt(Fmt(v), 10, Draw.Label), p + new Vector(-10, 4)); }
            foreach (var v in Ticks(y0, y1, LogY)) { var p = P(LogX ? Math.Pow(10, x0) : x0, v); dc.DrawLine(new Pen(Draw.Grid, 1), p, new Point(w - r, p.Y)); dc.DrawText(Draw.Txt(Fmt(v), 10, Draw.Label), p + new Vector(-l + 6, -7)); }
            dc.DrawText(Draw.Txt(XLabel, 11, Draw.Label), new Point(w - r - 120, h - 16));
            dc.DrawText(Draw.Txt(YLabel, 11, Draw.Label), new Point(l + 4, t));
            dc.PushClip(new RectangleGeometry(new Rect(l, t, w - l - r, h - b - t)));
            foreach (var s in Data)
            {
                var pts = s.Pts.Where(p => (!LogX || p.x > 0) && (!LogY || p.y > 0)).Select(p => P(p.x, p.y)).ToList();
                var pen = new Pen(s.Brush, 2) { DashStyle = s.Dashed ? DashStyles.Dash : DashStyles.Solid };
                for (int i = 1; i < pts.Count; i++) dc.DrawLine(pen, pts[i - 1], pts[i]);
                if (s.Markers) foreach (var p in pts) dc.DrawEllipse(s.Brush, null, p, 3, 3);
            }
            dc.Pop();
            double ly = t + 4;
            foreach (var s in Data)
            {
                dc.DrawLine(new Pen(s.Brush, 2) { DashStyle = s.Dashed ? DashStyles.Dash : DashStyles.Solid }, new Point(w - 190, ly + 8), new Point(w - 168, ly + 8));
                dc.DrawText(Draw.Txt(s.Name, 11, Draw.Label), new Point(w - 162, ly)); ly += 17;
            }
        }
    }

    /// <summary>Per-patch ΔE bars, each bar tinted with the patch colour.</summary>
    public sealed class DeltaEChart : FrameworkElement
    {
        public List<(Color c, double de)> Bars = new();
        public DeltaEChart() { Height = 200; FlowDirection = FlowDirection.LeftToRight; }
        protected override void OnRender(DrawingContext dc)
        {
            if (Bars.Count == 0) return;
            double w = ActualWidth, h = ActualHeight, l = 34, b = 18;
            double max = Math.Max(3, Bars.Max(x => x.de) * 1.1), bw = (w - l) / Bars.Count;
            foreach (var lev in new[] { 1.0, 2.0, 3.0, 5.0 }.Where(v => v < max))
            {
                double y = h - b - lev / max * (h - b);
                dc.DrawLine(new Pen(lev <= 1 ? Ui.Good : lev <= 3 ? Ui.Warm : Ui.Bad, 0.8) { DashStyle = DashStyles.Dash }, new Point(l, y), new Point(w, y));
                dc.DrawText(Draw.Txt(lev.ToString("0", CultureInfo.InvariantCulture), 10, Draw.Label), new Point(6, y - 7));
            }
            for (int i = 0; i < Bars.Count; i++)
            {
                double bh = Bars[i].de / max * (h - b);
                dc.DrawRectangle(new SolidColorBrush(Bars[i].c), null, new Rect(l + i * bw + 0.5, h - b - bh, Math.Max(bw - 1, 1), bh));
            }
            dc.DrawText(Draw.Txt("ΔE2000", 10, Draw.Label), new Point(l, h - 14));
        }
    }
}
