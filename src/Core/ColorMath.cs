// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>Small immutable 3-vector.</summary>
    public readonly struct V3
    {
        public readonly double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double this[int i] => i == 0 ? X : i == 1 ? Y : Z;
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
        public static V3 operator /(V3 a, double k) => new(a.X / k, a.Y / k, a.Z / k);
        public double Sum => X + Y + Z;
        public double Max => Math.Max(X, Math.Max(Y, Z));
        public (double x, double y) Xy => (X / Sum, Y / Sum);
        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }

    /// <summary>Row-major 3x3 matrix.</summary>
    public readonly struct M3
    {
        public readonly double[] A;   // 9 values, row-major
        public M3(params double[] a) { A = a; }
        public double this[int r, int c] => A[r * 3 + c];
        public static M3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);
        public static M3 FromColumns(V3 c0, V3 c1, V3 c2) => new(c0.X, c1.X, c2.X, c0.Y, c1.Y, c2.Y, c0.Z, c1.Z, c2.Z);
        public V3 Col(int c) => new(this[0, c], this[1, c], this[2, c]);
        public static V3 operator *(M3 m, V3 v) =>
            new(m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z, m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z, m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z);
        public static M3 operator *(M3 a, M3 b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) r[i * 3 + j] += a[i, k] * b[k, j];
            return new M3(r);
        }
        public static M3 operator *(M3 a, double k) { var r = new double[9]; for (int i = 0; i < 9; i++) r[i] = a.A[i] * k; return new M3(r); }
        public M3 Inverse()
        {
            double a = this[0, 0], b = this[0, 1], c = this[0, 2], d = this[1, 0], e = this[1, 1], f = this[1, 2], g = this[2, 0], h = this[2, 1], i = this[2, 2];
            double A_ = e * i - f * h, B_ = -(d * i - f * g), C_ = d * h - e * g;
            double det = a * A_ + b * B_ + c * C_;
            if (Math.Abs(det) < 1e-15) throw new InvalidOperationException("singular matrix");
            return new M3(A_ / det, -(b * i - c * h) / det, (b * f - c * e) / det,
                          B_ / det, (a * i - c * g) / det, -(a * f - c * d) / det,
                          C_ / det, -(a * h - b * g) / det, (a * e - b * d) / det);
        }
    }

    public static class Cm
    {
        public static readonly V3 D65 = new(0.95047, 1.0, 1.08883);
        public static readonly V3 D50 = new(0.96422, 1.0, 0.82521);
        public static readonly M3 SrgbToXyz = new(0.4124564, 0.3575761, 0.1804375, 0.2126729, 0.7151522, 0.0721750, 0.0193339, 0.1191920, 0.9503041);
        static readonly M3 Bradford = new(0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296);

        public static V3 XyToXyz(double x, double y, double Y = 1) => new(x * Y / y, Y, (1 - x - y) * Y / y);

        /// <summary>CIE daylight locus chromaticity for a CCT (4000–25000 K).</summary>
        public static (double x, double y) DaylightXy(double t)
        {
            double x = t <= 7000 ? -4.6070e9 / (t * t * t) + 2.9678e6 / (t * t) + 0.09911e3 / t + 0.244063
                                 : -2.0064e9 / (t * t * t) + 1.9018e6 / (t * t) + 0.24748e3 / t + 0.237040;
            return (x, -3.0 * x * x + 2.87 * x - 0.275);
        }

        public static double Cct(V3 xyz)
        {
            var (x, y) = xyz.Xy; double n = (x - 0.3320) / (0.1858 - y);
            return 449 * n * n * n + 3525 * n * n + 6823.3 * n + 5520.33;
        }

        /// <summary>Bradford adaptation matrix from one white to another (XYZ).</summary>
        public static M3 Adapt(V3 from, V3 to)
        {
            var s = Bradford * from; var d = Bradford * to;
            var diag = new M3(d.X / s.X, 0, 0, 0, d.Y / s.Y, 0, 0, 0, d.Z / s.Z);
            return Bradford.Inverse() * diag * Bradford;
        }

        public static V3 Lab(V3 xyz, V3 white)
        {
            static double F(double t) => t > Math.Pow(6.0 / 29, 3) ? Math.Cbrt(t) : t / (3 * Math.Pow(6.0 / 29, 2)) + 4.0 / 29;
            double fx = F(xyz.X / white.X), fy = F(xyz.Y / white.Y), fz = F(xyz.Z / white.Z);
            return new V3(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
        }

        public static double DeltaE2000(V3 lab1, V3 lab2)
        {
            double L1 = lab1.X, a1 = lab1.Y, b1 = lab1.Z, L2 = lab2.X, a2 = lab2.Y, b2 = lab2.Z;
            double C1 = Math.Sqrt(a1 * a1 + b1 * b1), C2 = Math.Sqrt(a2 * a2 + b2 * b2), Cb = (C1 + C2) / 2;
            double G = 0.5 * (1 - Math.Sqrt(Math.Pow(Cb, 7) / (Math.Pow(Cb, 7) + Math.Pow(25, 7))));
            double a1p = (1 + G) * a1, a2p = (1 + G) * a2;
            double C1p = Math.Sqrt(a1p * a1p + b1 * b1), C2p = Math.Sqrt(a2p * a2p + b2 * b2);
            double h1p = (Math.Atan2(b1, a1p) * 180 / Math.PI + 360) % 360, h2p = (Math.Atan2(b2, a2p) * 180 / Math.PI + 360) % 360;
            double dLp = L2 - L1, dCp = C2p - C1p, dhp = h2p - h1p;
            if (C1p * C2p == 0) dhp = 0; else if (dhp > 180) dhp -= 360; else if (dhp < -180) dhp += 360;
            double dHp = 2 * Math.Sqrt(C1p * C2p) * Math.Sin(dhp * Math.PI / 360);
            double Lbp = (L1 + L2) / 2, Cbp = (C1p + C2p) / 2, hbp = h1p + h2p;
            if (C1p * C2p != 0) hbp = Math.Abs(h1p - h2p) > 180 ? (hbp + (hbp < 360 ? 360 : -360)) / 2 : hbp / 2;
            double T = 1 - 0.17 * Cos(hbp - 30) + 0.24 * Cos(2 * hbp) + 0.32 * Cos(3 * hbp + 6) - 0.20 * Cos(4 * hbp - 63);
            double dth = 30 * Math.Exp(-Math.Pow((hbp - 275) / 25, 2));
            double Rc = 2 * Math.Sqrt(Math.Pow(Cbp, 7) / (Math.Pow(Cbp, 7) + Math.Pow(25, 7)));
            double Sl = 1 + 0.015 * Math.Pow(Lbp - 50, 2) / Math.Sqrt(20 + Math.Pow(Lbp - 50, 2));
            double Sc = 1 + 0.045 * Cbp, Sh = 1 + 0.015 * Cbp * T, Rt = -Math.Sin(2 * dth * Math.PI / 180) * Rc;
            return Math.Sqrt(Math.Pow(dLp / Sl, 2) + Math.Pow(dCp / Sc, 2) + Math.Pow(dHp / Sh, 2) + Rt * (dCp / Sc) * (dHp / Sh));
            static double Cos(double deg) => Math.Cos(deg * Math.PI / 180);
        }

        // ------------------------------------------------------------ transfer functions
        public static double SrgbEotf(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        public static double SrgbOetf(double l) { l = Math.Clamp(l, 0, 1); return l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055; }

        const double M1 = 0.1593017578125, M2 = 78.84375, C1 = 0.8359375, C2 = 18.8515625, C3 = 18.6875;
        /// <summary>PQ code (0..1) → nits.</summary>
        public static double PqEotf(double p)
        {
            p = Math.Pow(Math.Clamp(p, 0, 1), 1 / M2);
            return 10000 * Math.Pow(Math.Max(p - C1, 0) / (C2 - C3 * p), 1 / M1);
        }
        /// <summary>Nits → PQ code (0..1).</summary>
        public static double PqInv(double nits)
        {
            double y = Math.Pow(Math.Clamp(nits, 0, 10000) / 10000, M1);
            return Math.Pow((C1 + C2 * y) / (1 + C3 * y), M2);
        }

        /// <summary>Target relative luminance for an encoded value under a named response curve.</summary>
        public static double Eotf(string curve, double v, double blackRel = 0)
        {
            switch (curve)
            {
                case "srgb": return SrgbEotf(v);
                case "bt1886":
                    {   // ITU-R BT.1886 with white = 1 and the measured relative black level
                        double lw = 1, wr = Math.Pow(lw, 1 / 2.4), br = Math.Pow(Math.Max(blackRel, 0), 1 / 2.4);
                        double a = Math.Pow(wr - br, 2.4), b = br / (wr - br);
                        return a * Math.Pow(Math.Max(v + b, 0), 2.4);
                    }
                case "lstar": return v <= 0.08 ? v * 100 / 903.3 : Math.Pow((v * 100 + 16) / 116, 3);
                default: return Math.Pow(v, double.TryParse(curve, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) ? g : 2.2);
            }
        }

        public static double Interp(double x, double[] xs, double[] ys)
        {
            if (x <= xs[0]) return ys[0];
            if (x >= xs[^1]) return ys[^1];
            int i = Array.BinarySearch(xs, x); if (i >= 0) return ys[i];
            i = ~i;
            double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[i - 1] + t * (ys[i] - ys[i - 1]);
        }
    }
}
