// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>
    /// Builds an accurate-PQ MHC2 profile from colorimeter readings, without HDR test patterns:
    /// SDR patches shown while Windows is in HDR mode arrive at the monitor as PQ signals of
    /// sdrWhite * sRGB_EOTF(v) nits, so a dense grey ramp traces the monitor's PQ response.
    /// A second pass through an x8 "probe" profile reaches signal levels above the SDR white
    /// to find the clipping point and real peak.
    /// </summary>
    public static class HdrPipeline
    {
        public const double ProbeGain = 8;

        /// <summary>Grey values whose requested luminance is evenly spaced in PQ, plus R/G/B/C/M/Y at 100% and 60%.</summary>
        public static List<(double r, double g, double b)> RampChart(double sdrWhite, int greys = 40)
        {
            var rows = new List<(double, double, double)>();
            double top = Cm.PqInv(sdrWhite);
            for (int i = 0; i < greys; i++)
            {
                double L = Cm.PqEotf(top * i / (greys - 1));
                double v = Cm.SrgbOetf(L / sdrWhite);
                rows.Add((v, v, v));
            }
            foreach (var lev in new[] { 1.0, 0.6 })
                rows.AddRange(new[] { (lev, 0.0, 0.0), (0.0, lev, 0.0), (0.0, 0.0, lev), (lev, lev, 0.0), (0.0, lev, lev), (lev, 0.0, lev) });
            rows.Add((1, 1, 1)); rows.Add((0, 0, 0));
            return rows;
        }

        public sealed class Response
        {
            public double[] Req, Out;   // requested nits → measured nits (monotonic)
            public double Black => Out[0];
            public double ClipReq, ClipOut;
            public double Forward(double req) => Cm.Interp(req, Req, Out);
            public double Inverse(double outNits)
            {
                int n = Array.FindIndex(Req, r => r >= ClipReq) + 1;
                return Cm.Interp(outNits, Out.Take(n).ToArray(), Req.Take(n).ToArray());
            }
        }

        static IEnumerable<(double req, double y)> Greys(List<Patch> p, double sdrWhite, double gain) =>
            p.Where(x => x.IsGrey).Select(x => (gain * sdrWhite * Cm.SrgbEotf(x.R), x.Xyz.Y));

        public static Response MonitorResponse(List<Patch> baseRamp, List<Patch> probeRamp, double sdrWhite)
        {
            var pts = Greys(baseRamp, sdrWhite, 1).Concat(Greys(probeRamp ?? new(), sdrWhite, ProbeGain)).OrderBy(t => t.req).ToList();
            var req = pts.Select(t => t.req).ToArray();
            var outp = pts.Select(t => t.y).ToArray();
            for (int i = 1; i < outp.Length; i++) outp[i] = Math.Max(outp[i], outp[i - 1]);   // monotonic
            double max = outp.Max();
            int clip = Array.FindIndex(outp, o => o >= max - 0.5);
            return new Response { Req = req, Out = outp, ClipReq = req[clip], ClipOut = outp[clip] };
        }

        /// <summary>Identity below the knee, exponential roll-off approaching the peak.</summary>
        public static double Target(double L, double knee, double peak)
        {
            double span = peak - knee;
            return L <= knee ? L : knee + span * (1 - Math.Exp(-(L - knee) / span));
        }

        /// <summary>XYZ correction so 60% R/G/B land on sRGB/D65 (normalised to keep white luminance).</summary>
        public static M3 MatrixFrom60(List<Patch> ramp, double sdrWhite)
        {
            V3 Pick(double r, double g, double b) => ramp.First(p => p.Is(r, g, b)).Xyz;
            var k = Pick(0, 0, 0);
            const double lev = 0.6;
            var mm = M3.FromColumns(Pick(lev, 0, 0) - k, Pick(0, lev, 0) - k, Pick(0, 0, lev) - k);
            var s = Cm.SrgbToXyz * (sdrWhite * Cm.SrgbEotf(lev));
            var d = mm * s.Inverse();
            var c = d.Inverse();
            return c * (1 / (c * Cm.D65).Y);
        }

        /// <summary>PQ-domain curves that make the monitor emit black + T(L) for requested L (same curve for R, G, B).</summary>
        public static double[] PqCurve(Response r, double knee, double peak, int n = 1024)
        {
            var lut = new double[n];
            for (int i = 0; i < n; i++)
            {
                double L = Cm.PqEotf(i / (double)(n - 1));
                double want = r.Black + Target(L, knee, peak) * (1 - r.Black / peak);
                lut[i] = i == 0 ? 0 : Math.Max(Cm.PqInv(r.Inverse(want)), lut[i - 1]);
            }
            return lut;
        }

        /// <summary>Measured primaries (xy) at a level, for the profile's reported gamut.</summary>
        public static ((double, double) r, (double, double) g, (double, double) b) Primaries(List<Patch> ramp, double lev = 1.0)
        {
            V3 Pick(double r, double g, double b) => ramp.First(p => p.Is(r, g, b)).Xyz - ramp.First(p => p.Is(0, 0, 0)).Xyz;
            return (Pick(lev, 0, 0).Xy, Pick(0, lev, 0).Xy, Pick(0, 0, lev).Xy);
        }

        /// <summary>
        /// Per-channel grey balance on top of <paramref name="prev"/>: for every grey level measured through it, find
        /// the R/G/B light ratios that turn that grey into D65 at the same luminance, then re-map each channel's curve.
        /// </summary>
        public static Mhc2Profile GreyBalance(Mhc2Profile prev, List<Patch> measured, Response r, double sdrWhite, double peak, double minNits = 0.8)
        {
            V3 Pick(double rr, double g, double b) => measured.First(p => p.Is(rr, g, b)).Xyz;
            var k = Pick(0, 0, 0);
            var p3 = M3.FromColumns(Pick(.6, 0, 0) - k, Pick(0, .6, 0) - k, Pick(0, 0, .6) - k);
            var rowY = new V3(p3[1, 0], p3[1, 1], p3[1, 2]);
            p3 = new M3(p3.A.Select((v, i) => v / new[] { rowY.X, rowY.Y, rowY.Z }[i % 3]).ToArray());   // per-channel XYZ per unit Y
            var levels = new List<(double L, V3 ratio)>();
            foreach (var g in measured.Where(x => x.IsGrey))
            {
                var m = g.Xyz - k; if (m.Y < minNits) continue;
                var now = p3.Inverse() * m; var tgt = p3.Inverse() * (Cm.D65 * m.Y);
                levels.Add((sdrWhite * Cm.SrgbEotf(g.R), new V3(tgt.X / now.X, tgt.Y / now.Y, tgt.Z / now.Z)));
            }
            levels = levels.OrderBy(l => l.L).ToList();
            for (int i = 0; i < levels.Count; i++)               // near the top only remove light (cannot add it)
                if (levels[i].L > 150) { var v = levels[i].ratio; levels[i] = (levels[i].L, v / v.Max); }
            var Ls = levels.Select(l => Math.Log10(l.L)).ToArray();
            double[] Smooth(Func<V3, double> f)
            {
                var a = levels.Select(l => f(l.ratio)).ToArray(); var o = new double[a.Length];
                for (int i = 0; i < a.Length; i++) o[i] = (a[Math.Max(i - 1, 0)] + a[i] + a[Math.Min(i + 1, a.Length - 1)]) / 3;
                return o;
            }
            var rc = new[] { Smooth(v => v.X), Smooth(v => v.Y), Smooth(v => v.Z) };
            var res = new Mhc2Profile { Matrix = prev.Matrix, MinNits = r.Black, MaxNits = peak, Description = prev.Description, Red = prev.Red, Green = prev.Green, Blue = prev.Blue };
            res.Luts = new double[3][];
            int n = prev.Luts[0].Length; double lmin = Math.Log10(minNits);
            for (int c = 0; c < 3; c++)
            {
                var lut = new double[n];
                double atMin = Cm.Interp(lmin, Ls, rc[c]);
                for (int i = 0; i < n; i++)
                {
                    double L = Cm.PqEotf(i / (double)(n - 1));
                    double ratio = L < minNits ? 1 + (atMin - 1) * Math.Clamp(L / minNits, 0, 1) : Cm.Interp(Math.Log10(Math.Max(L, 1e-4)), Ls, rc[c]);
                    double sent = Cm.PqEotf(prev.Luts[c][i]);
                    lut[i] = i == 0 ? 0 : Math.Max(Cm.PqInv(r.Inverse(r.Forward(sent) * ratio)), lut[i - 1]);
                }
                res.Luts[c] = lut;
            }
            return res;
        }

        /// <summary>Pre-compensates for the SDR video LUT that Windows keeps applying on top of the HDR signal.</summary>
        public static Mhc2Profile CompensateVcgt(Mhc2Profile p, (double[] x, double[] r, double[] g, double[] b) cal)
        {
            var outs = new[] { cal.r, cal.g, cal.b };
            var res = new Mhc2Profile { Matrix = p.Matrix, MinNits = p.MinNits, MaxNits = p.MaxNits, Description = p.Description, Red = p.Red, Green = p.Green, Blue = p.Blue, Luts = new double[3][] };
            for (int c = 0; c < 3; c++)
            {
                var y = (double[])outs[c].Clone();
                for (int i = 1; i < y.Length; i++) y[i] = Math.Max(y[i], y[i - 1] + 1e-9);
                res.Luts[c] = p.Luts[c].Select(v => Cm.Interp(v, y, cal.x)).ToArray();
            }
            return res;
        }

        /// <summary>One profile that multiplies every requested luminance by <see cref="ProbeGain"/> (for the peak probe).</summary>
        public static Mhc2Profile ProbeProfile(int n = 1024) => new()
        {
            Description = "HDRDisplayCalibrate PROBE x8 (temporary)",
            Luts = Enumerable.Range(0, 3).Select(_ => Enumerable.Range(0, n).Select(i => i == 0 ? 0 : Cm.PqInv(Math.Min(Cm.PqEotf(i / (double)(n - 1)) * ProbeGain, 10000))).ToArray()).ToArray(),
        };
    }
}
