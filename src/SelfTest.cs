// Compares the HDR pipeline with reference data: --selftest <folder with pq_base.ti3 pq_probe8.ti3 pq_v5.ti3>
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate
{
    static class SelfTest
    {
        public static void Run(string[] a)
        {
            var dir = a[0]; var sb = new StringBuilder(); var inv = CultureInfo.InvariantCulture;
            var b = Cgats.ReadTi3(Path.Combine(dir, "pq_base.ti3"), out _);
            var pr = Cgats.ReadTi3(Path.Combine(dir, "pq_probe8.ti3"), out _);
            var r = HdrPipeline.MonitorResponse(b, pr, 480);
            sb.AppendLine(string.Format(inv, "black {0:F3} clip {1:F0} -> {2:F1}", r.Black, r.ClipReq, r.ClipOut));
            var m = HdrPipeline.MatrixFrom60(b, 480);
            sb.AppendLine("matrix " + string.Join(" ", m.A.Select(x => x.ToString("F4", inv))));
            var lut = HdrPipeline.PqCurve(r, 200, 335);
            foreach (var L in new[] { 1.0, 5, 20, 80, 120, 200, 300, 500, 1000 })
            {
                int i = (int)System.Math.Round(Cm.PqInv(L) * 1023);
                sb.AppendLine(string.Format(inv, "req {0} -> send {1:F1}", L, Cm.PqEotf(lut[i])));
            }
            var p = new Mhc2Profile { Matrix = m, Luts = new[] { lut, lut, lut }, MinNits = r.Black, MaxNits = 335, Description = "selftest" };
            p.Save(Path.Combine(dir, "selftest_v4.icc"));
            var back = Mhc2Profile.Read(Path.Combine(dir, "selftest_v4.icc"));
            sb.AppendLine(string.Format(inv, "roundtrip max {0:F1} m00 {1:F4} lut512 {2:F4}", back.MaxNits, back.Matrix[0, 0], back.Luts[1][512]));
            var v5 = Cgats.ReadTi3(Path.Combine(dir, "pq_v5.ti3"), out _);
            var gb = HdrPipeline.GreyBalance(Mhc2Profile.Read(Path.Combine(dir, "VG34VQL3A_HDR_PQ_v5.icc")), v5, r, 480, 280);
            foreach (var L in new[] { 5.0, 50, 150, 250, 400 })
            {
                int i = (int)System.Math.Round(Cm.PqInv(L) * 1023);
                sb.AppendLine(string.Format(inv, "gb req {0}: R {1:F1} G {2:F1} B {3:F1}", L, Cm.PqEotf(gb.Luts[0][i]), Cm.PqEotf(gb.Luts[1][i]), Cm.PqEotf(gb.Luts[2][i])));
            }
            var rep = Report.Build(Cgats.ReadTi3(Path.Combine(dir, "after.ti3"), out _), "2.2", (0.3127, 0.3290));
            sb.AppendLine(string.Format(inv, "report after: white {0:F1} dE {1:F2} grey {2:F2}/{3:F2} color {4:F2}/{5:F2} gamma {6:F3}", rep.WhiteNits, rep.WhiteDeltaE, rep.GreyMeanDE, rep.GreyMaxDE, rep.ColorMeanDE, rep.ColorMaxDE, rep.GammaMid));
            var disp = new Gamut.Space("display", (0.6806, 0.3150), (0.2597, 0.6584), (0.1506, 0.0646));
            foreach (var s in Gamut.References) { var (c, z) = Gamut.Compare(disp, s); sb.AppendLine(string.Format(inv, "gamut {0}: cover {1:F1}% size {2:F1}%", s.Name, c, z)); }
            File.WriteAllText(Path.Combine(dir, "selftest.txt"), sb.ToString());
        }
    }
}
