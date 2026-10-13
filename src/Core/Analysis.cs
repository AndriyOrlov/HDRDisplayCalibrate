// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HDRDisplayCalibrate.Core
{
    public sealed record PatchResult(Patch P, V3 Target, double DeltaE);
    public sealed record GreyStep(double Level, double Nits, double? Gamma, double DeltaE, double X, double Y);

    /// <summary>Verification of measured patches against sRGB/Rec.709 primaries, a white point and a tone curve.</summary>
    public sealed class Report
    {
        public double WhiteNits, BlackNits, Contrast, WhiteCct, WhiteDeltaE, GammaMid;
        public (double x, double y) WhiteXy;
        public double MeanDE, MaxDE, P95DE, GreyMeanDE, GreyMaxDE, ColorMeanDE, ColorMaxDE;
        public List<PatchResult> Patches = new();
        public List<GreyStep> Greys = new();

        public static Report Build(List<Patch> m, string curve, (double x, double y) targetWhite)
        {
            var r = new Report();
            var whites = m.Where(p => p.Is(1, 1, 1)).ToList();
            var w = whites.Aggregate(new V3(0, 0, 0), (a, p) => a + p.Xyz) / whites.Count;
            var blacks = m.Where(p => p.Is(0, 0, 0)).ToList();
            var k = blacks.Count > 0 ? blacks.Aggregate(new V3(0, 0, 0), (a, p) => a + p.Xyz) / blacks.Count : new V3(0, 0, 0);
            r.WhiteNits = w.Y; r.BlackNits = k.Y; r.Contrast = k.Y > 0 ? w.Y / k.Y : double.PositiveInfinity;
            r.WhiteXy = w.Xy; r.WhiteCct = Cm.Cct(w);
            var tw = Cm.XyToXyz(targetWhite.x, targetWhite.y, w.Y);
            // sRGB primaries adapted to the target white
            var toTarget = Cm.Adapt(Cm.D65, Cm.XyToXyz(targetWhite.x, targetWhite.y));
            r.WhiteDeltaE = Cm.DeltaE2000(Cm.Lab(tw, tw), Cm.Lab(w, tw));
            foreach (var p in m)
            {
                double Ch(double v) => Cm.Eotf(curve, v, k.Y / w.Y);
                var t = toTarget * (Cm.SrgbToXyz * new V3(Ch(p.R), Ch(p.G), Ch(p.B))) * w.Y;
                r.Patches.Add(new PatchResult(p, t, Cm.DeltaE2000(Cm.Lab(t, tw), Cm.Lab(p.Xyz, tw))));
            }
            foreach (var pr in r.Patches.Where(x => x.P.IsGrey && x.P.R > 0.001).OrderBy(x => x.P.R))
            {
                double v = pr.P.R, rel = (pr.P.Xyz.Y - k.Y) / (w.Y - k.Y);
                double? g = v > 0.02 && v < 0.99 && rel > 0 ? Math.Log(rel) / Math.Log(v) : null;
                var (x, y) = pr.P.Xyz.Xy;
                r.Greys.Add(new GreyStep(v, pr.P.Xyz.Y, g, pr.DeltaE, x, y));
            }
            var de = r.Patches.Select(p => p.DeltaE).OrderBy(x => x).ToList();
            r.MeanDE = de.Average(); r.MaxDE = de.Max(); r.P95DE = de[(int)Math.Min(de.Count - 1, Math.Round(de.Count * 0.95))];
            var gr = r.Patches.Where(p => p.P.IsGrey).Select(p => p.DeltaE).ToList();
            var co = r.Patches.Where(p => !p.P.IsGrey).Select(p => p.DeltaE).ToList();
            if (gr.Count > 0) { r.GreyMeanDE = gr.Average(); r.GreyMaxDE = gr.Max(); }
            if (co.Count > 0) { r.ColorMeanDE = co.Average(); r.ColorMaxDE = co.Max(); }
            var mid = r.Greys.Where(g => g.Gamma != null && g.Level > 0.1 && g.Level < 0.9).Select(g => g.Gamma.Value).OrderBy(x => x).ToList();
            r.GammaMid = mid.Count > 0 ? mid[mid.Count / 2] : double.NaN;
            return r;
        }
    }

    /// <summary>Gamut coverage of a display triangle against reference colour spaces (CIE 1976 u'v' or 1931 xy).</summary>
    public static class Gamut
    {
        public sealed record Space(string Name, (double x, double y) R, (double x, double y) G, (double x, double y) B);
        public static readonly Space[] References =
        {
            new("sRGB / Rec.709", (0.64, 0.33), (0.30, 0.60), (0.15, 0.06)),
            new("Adobe RGB", (0.64, 0.33), (0.21, 0.71), (0.15, 0.06)),
            new("DCI-P3 / Display P3", (0.680, 0.320), (0.265, 0.690), (0.150, 0.060)),
            new("Rec.2020", (0.708, 0.292), (0.170, 0.797), (0.131, 0.046)),
        };

        public static (double u, double v) Uv((double x, double y) p) { double d = -2 * p.x + 12 * p.y + 3; return (4 * p.x / d, 9 * p.y / d); }

        static double Area(List<(double, double)> poly)
        {
            double a = 0;
            for (int i = 0; i < poly.Count; i++) { var (x1, y1) = poly[i]; var (x2, y2) = poly[(i + 1) % poly.Count]; a += x1 * y2 - x2 * y1; }
            return Math.Abs(a) / 2;
        }

        // Sutherland–Hodgman clip of a convex polygon by a convex polygon
        static List<(double, double)> Clip(List<(double, double)> subject, List<(double, double)> clip)
        {
            double Sign(List<(double, double)> p) { double a = 0; for (int i = 0; i < p.Count; i++) { var (x1, y1) = p[i]; var (x2, y2) = p[(i + 1) % p.Count]; a += x1 * y2 - x2 * y1; } return Math.Sign(a); }
            double s = Sign(clip);
            var output = subject;
            for (int i = 0; i < clip.Count && output.Count > 0; i++)
            {
                var (ax, ay) = clip[i]; var (bx, by) = clip[(i + 1) % clip.Count];
                bool Inside((double x, double y) p) => s * ((bx - ax) * (p.y - ay) - (by - ay) * (p.x - ax)) >= 0;
                (double, double) Cut((double x, double y) p, (double x, double y) q)
                {
                    double a1 = by - ay, b1 = ax - bx, c1 = a1 * ax + b1 * ay;
                    double a2 = q.y - p.y, b2 = p.x - q.x, c2 = a2 * p.x + b2 * p.y, det = a1 * b2 - a2 * b1;
                    return ((b2 * c1 - b1 * c2) / det, (a1 * c2 - a2 * c1) / det);
                }
                var input = output; output = new List<(double, double)>();
                for (int j = 0; j < input.Count; j++)
                {
                    var cur = input[j]; var prev = input[(j + input.Count - 1) % input.Count];
                    if (Inside(cur)) { if (!Inside(prev)) output.Add(Cut(prev, cur)); output.Add(cur); }
                    else if (Inside(prev)) output.Add(Cut(prev, cur));
                }
            }
            return output;
        }

        /// <summary>(coverage %, relative size %) of the display triangle versus a reference, in u'v'.</summary>
        public static (double coverage, double size) Compare(Space display, Space reference, bool uv = true)
        {
            List<(double, double)> T(Space s) => new[] { s.R, s.G, s.B }.Select(p => uv ? Uv(p) : p).ToList();
            var d = T(display); var r = T(reference);
            return (Area(Clip(d, r)) / Area(r) * 100, Area(d) / Area(r) * 100);
        }
    }
}
