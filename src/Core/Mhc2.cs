// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>
    /// Windows Advanced Color (HDR) display profile: ICC v4 + Microsoft "MHC2" tag.
    /// Layout mirrors the profiles written by the Windows HDR Calibration app, which the
    /// Settings app accepts: 3x4 matrix applied in XYZ, then per-channel 1D LUTs in the
    /// PQ-encoded output domain, plus min/max luminance reported to applications.
    /// </summary>
    public sealed class Mhc2Profile
    {
        public string Description = "HDRDisplayCalibrate";
        public M3 Matrix = M3.Identity;
        public double[][] Luts = { Ramp(1024), Ramp(1024), Ramp(1024) };
        public double MinNits = 0, MaxNits = 1000;
        /// <summary>Display primaries reported to applications (CIE xy). Default: Rec.709.</summary>
        public (double x, double y) Red = (0.64, 0.33), Green = (0.30, 0.60), Blue = (0.15, 0.06), White = (0.3127, 0.3290);

        public static double[] Ramp(int n) => Enumerable.Range(0, n).Select(i => i / (double)(n - 1)).ToArray();

        // ------------------------------------------------------------ binary helpers
        static void BE(List<byte> b, uint v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }
        static void S15(List<byte> b, double v) => BE(b, unchecked((uint)(int)Math.Round(v * 65536)));
        static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
        static byte[] Sig(string s) => Encoding.ASCII.GetBytes(s);

        static byte[] Mluc(string text)
        {
            var b = new List<byte>(); b.AddRange(Sig("mluc")); BE(b, 0); BE(b, 1); BE(b, 12);
            b.AddRange(Sig("enUS")); var s = Encoding.BigEndianUnicode.GetBytes(text); BE(b, (uint)s.Length); BE(b, 28); b.AddRange(s);
            return b.ToArray();
        }
        static byte[] Xyz(V3 v) { var b = new List<byte>(); b.AddRange(Sig("XYZ ")); BE(b, 0); S15(b, v.X); S15(b, v.Y); S15(b, v.Z); return b.ToArray(); }
        static byte[] Curv22() { var b = new List<byte>(); b.AddRange(Sig("curv")); BE(b, 0); BE(b, 1); U16(b, 0x0233); b.Add(0); b.Add(0); return b.ToArray(); }
        static byte[] Sf32(M3 m) { var b = new List<byte>(); b.AddRange(Sig("sf32")); BE(b, 0); foreach (var v in m.A) S15(b, v); return b.ToArray(); }
        static byte[] Text(string t) { var b = new List<byte>(); b.AddRange(Sig("text")); BE(b, 0); b.AddRange(Encoding.ASCII.GetBytes(t)); b.Add(0); return b.ToArray(); }

        byte[] Mhc2Tag()
        {
            int n = Luts[0].Length;
            var mat = new List<byte>();
            for (int r = 0; r < 3; r++) { S15(mat, Matrix[r, 0]); S15(mat, Matrix[r, 1]); S15(mat, Matrix[r, 2]); S15(mat, 0); }
            var luts = Luts.Select(l => { var b = new List<byte>(); b.AddRange(Sig("sf32")); BE(b, 0); foreach (var v in l) S15(b, v); return b.ToArray(); }).ToArray();
            uint matOff = 36, r0 = matOff + (uint)mat.Count, g0 = r0 + (uint)luts[0].Length, b0 = g0 + (uint)luts[1].Length;
            var t = new List<byte>(); t.AddRange(Sig("MHC2")); BE(t, 0); BE(t, (uint)n); S15(t, MinNits); S15(t, MaxNits);
            BE(t, matOff); BE(t, r0); BE(t, g0); BE(t, b0);
            t.AddRange(mat); foreach (var l in luts) t.AddRange(l);
            return t.ToArray();
        }

        /// <summary>Serialises the profile; the ICC profile ID (MD5) is filled in like Windows does.</summary>
        public byte[] ToBytes()
        {
            // primaries / white stay D65-relative, flagged by MSCA 'D65Adapted' like the Windows HDR Calibration app
            var w = Cm.XyToXyz(White.x, White.y);
            var prim = M3.FromColumns(Cm.XyToXyz(Red.x, Red.y), Cm.XyToXyz(Green.x, Green.y), Cm.XyToXyz(Blue.x, Blue.y));
            var k = prim.Inverse() * w;                       // scale primaries so they add up to the white
            var tags = new List<(string sig, byte[] data)>
            {
                ("cprt", Mluc("Created with HDRDisplayCalibrate · Apache-2.0 · Andrii Orlov (DeadSue)")),
                ("rTRC", Curv22()), ("gTRC", Curv22()), ("bTRC", Curv22()),
                ("chad", Sf32(M3.Identity)),
                ("rXYZ", Xyz(prim.Col(0) * k.X)), ("gXYZ", Xyz(prim.Col(1) * k.Y)), ("bXYZ", Xyz(prim.Col(2) * k.Z)),
                ("wtpt", Xyz(w)),
                ("MSCA", Text("{'Appversion':'HDRDisplayCalibrate','D65Adapted':True}")),
                ("lumi", Xyz(new V3(MaxNits * 0.9505, MaxNits, MaxNits * 1.0888))),
                ("MHC2", Mhc2Tag()),
                ("desc", Mluc(Description)),
            };
            var table = new List<byte>(); var body = new List<byte>();
            uint off = (uint)(128 + 4 + 12 * tags.Count);
            var shared = new Dictionary<string, uint>();
            foreach (var (sig, data) in tags)
            {
                string key = Convert.ToBase64String(data);
                uint at;
                if (sig.EndsWith("TRC") && shared.TryGetValue(key, out at)) { }   // r/g/bTRC share one curve, like Windows
                else
                {
                    while (off % 4 != 0) { body.Add(0); off++; }
                    at = off; body.AddRange(data); off += (uint)data.Length; shared[key] = at;
                }
                table.AddRange(Sig(sig)); BE(table, at); BE(table, (uint)data.Length);
            }
            while (off % 4 != 0) { body.Add(0); off++; }

            var h = new List<byte>();
            BE(h, off); h.AddRange(Sig("SICC")); BE(h, 0x04400000); h.AddRange(Sig("mntr")); h.AddRange(Sig("RGB ")); h.AddRange(Sig("XYZ "));
            var now = DateTime.UtcNow; foreach (var v in new[] { now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second }) U16(h, v);
            // 36 acsp, 40 platform, 44 flags, 48 manufacturer, 52 model, 56 attributes (8), 64 intent, 68 PCS illuminant, 80 creator
            // (same header values as the profiles Windows' own HDR Calibration app writes; a shifted illuminant makes Windows ignore the profile)
            h.AddRange(Sig("acsp")); h.AddRange(Sig("MSFT")); BE(h, 0); BE(h, 0); BE(h, 0); BE(h, 0); BE(h, 1); BE(h, 1);
            S15(h, Cm.D50.X); S15(h, Cm.D50.Y); S15(h, Cm.D50.Z); h.AddRange(Sig("MSFT"));
            while (h.Count < 128) h.Add(0);
            var all = new List<byte>(h); BE(all, (uint)tags.Count); all.AddRange(table); all.AddRange(body);
            var arr = all.ToArray();
            var tmp = (byte[])arr.Clone();
            Array.Clear(tmp, 44, 4); Array.Clear(tmp, 64, 4); Array.Clear(tmp, 84, 16);
            Array.Copy(MD5.HashData(tmp), 0, arr, 84, 16);
            return arr;
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        /// <summary>Reads the MHC2 tag of an existing profile (matrix, curves, luminance).</summary>
        public static Mhc2Profile Read(string path)
        {
            var d = File.ReadAllBytes(path);
            uint U(int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
            double S(int o) => unchecked((int)U(o)) / 65536.0;
            int n = (int)U(128);
            for (int i = 0; i < n; i++)
            {
                int e = 132 + 12 * i;
                if (Encoding.ASCII.GetString(d, e, 4) != "MHC2") continue;
                int o = (int)U(e + 4), cnt = (int)U(o + 8);
                var p = new Mhc2Profile { MinNits = S(o + 12), MaxNits = S(o + 16) };
                int mo = o + (int)U(o + 20);
                p.Matrix = new M3(S(mo), S(mo + 4), S(mo + 8), S(mo + 16), S(mo + 20), S(mo + 24), S(mo + 32), S(mo + 36), S(mo + 40));
                p.Luts = new double[3][];
                for (int c = 0; c < 3; c++)
                {
                    int lo = o + (int)U(o + 24 + 4 * c);
                    p.Luts[c] = Enumerable.Range(0, cnt).Select(k => S(lo + 8 + 4 * k)).ToArray();
                }
                return p;
            }
            throw new InvalidDataException("no MHC2 tag");
        }
    }
}
