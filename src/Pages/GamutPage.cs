// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HDRDisplayCalibrate.Core;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>Gamut coverage of the display versus sRGB, Adobe RGB, DCI-P3, Rec.2020: table, xy/u'v' triangles and a 3D Lab solid.</summary>
    public sealed class GamutPage : Page
    {
        readonly StackPanel _result = new();
        readonly CheckBox _uv = Ui.Check(S.T("CIE 1976 u'v' (perceptually more even)", "CIE 1976 u'v' (рівномірніше для ока)"), true);
        Gamut.Space _display;

        public GamutPage() : base(S.T("Gamut", "Гамут"),
            S.T("Which colours the display can show compared with the common standards. Coverage = how much of the standard's triangle the display reaches.",
                "Які кольори дисплей здатен показати порівняно зі стандартами. Покриття = яку частку трикутника стандарту досягає дисплей."))
        {
            _uv.Click += (_, _) => Render();
            Body.Children.Add(Ui.Card(Ui.Row(
                Ui.Btn(S.T("From the active profile", "З активного профілю"), FromProfile, true, ""),
                Ui.Btn(S.T("Measure now", "Заміряти зараз"), Measure, false, ""),
                Ui.Btn(S.T("Open ICC…", "Відкрити ICC…"), OpenIcc, false, "")), _uv));
            Body.Children.Add(_result);
        }

        public override void OnShow() { if (_display == null) FromProfile(); }

        void FromProfile()
        {
            var d = St.Display; var name = d == null ? null : ColorProfiles.Default(d, false);
            if (name == null) { _result.Children.Clear(); _result.Children.Add(Ui.Caption(S.T("No SDR profile for this display yet — measure instead.", "Для цього дисплея ще немає SDR-профілю — заміряйте."))); return; }
            Load(Path.Combine(ColorProfiles.ColorDir, name));
        }

        void OpenIcc()
        {
            var dlg = new OpenFileDialog { Filter = "ICC profiles (*.icc;*.icm)|*.icc;*.icm", InitialDirectory = ColorProfiles.ColorDir };
            if (dlg.ShowDialog() == true) Load(dlg.FileName);
        }

        void Load(string path)
        {
            try { _display = IccPrimaries(path, Path.GetFileNameWithoutExtension(path)); Render(); }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }

        void Measure()
        {
            var d = St.Display; if (d?.ArgyllNumber is not int disp) return;
            RunJob(async ct =>
            {
                Argyll.LutLinear(disp);
                try
                {
                    var m = await Jobs.Measure(new[] { (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (0.0, 0.0, 0.0) }, Paths.Session("gamut"), "gamut", S.T("Primaries", "Основні кольори"), ct);
                    var k = m[3].Xyz;
                    _display = new Gamut.Space(d.Name, (m[0].Xyz - k).Xy, (m[1].Xyz - k).Xy, (m[2].Xyz - k).Xy);
                    Dispatcher.Invoke(Render);
                }
                finally { Argyll.LoadInstalled(disp); }
            });
        }

        /// <summary>Absolute primaries from an ICC display profile's colorant tags (undoing 'chad' if present).</summary>
        public static Gamut.Space IccPrimaries(string path, string name)
        {
            var b = File.ReadAllBytes(path);
            uint U(int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
            double Sf(int o) => unchecked((int)U(o)) / 65536.0;
            var tags = new Dictionary<string, int>();
            for (int i = 0, n = (int)U(128); i < n; i++) tags[Encoding.ASCII.GetString(b, 132 + 12 * i, 4)] = (int)U(132 + 12 * i + 4);
            V3 X(string t) => tags.TryGetValue(t, out int o) ? new V3(Sf(o + 8), Sf(o + 12), Sf(o + 16)) : throw new InvalidDataException(S.T($"Profile has no {t} tag (not a matrix/LUT display profile)", $"У профілі немає тегу {t}"));
            var chad = tags.TryGetValue("chad", out int co) ? new M3(Enumerable.Range(0, 9).Select(i => Sf(co + 8 + 4 * i)).ToArray()) : M3.Identity;
            var inv = chad.Inverse();
            return new Gamut.Space(name, (inv * X("rXYZ")).Xy, (inv * X("gXYZ")).Xy, (inv * X("bXYZ")).Xy);
        }

        void Render()
        {
            _result.Children.Clear();
            if (_display == null) return;
            bool uv = _uv.IsChecked == true;
            var rows = Gamut.References.Select(r => { var (c, z) = Gamut.Compare(_display, r, uv); return new[] { r.Name, $"{c:F1} %", $"{z:F1} %" }; });
            var colors = new[] { "#9A9A9A", "#E879F9", "#F97316", "#22D3EE" };
            var chart = new ChromaticityChart { UV = uv, Height = 460 };
            for (int i = 0; i < Gamut.References.Length; i++)
            {
                var r = Gamut.References[i];
                chart.Triangles.Add(new ChromaticityChart.Tri(r.Name, r.R, r.G, r.B, Draw.Of(colors[i]), false, true));
            }
            chart.Triangles.Add(new ChromaticityChart.Tri(_display.Name, _display.R, _display.G, _display.B, Ui.Warm, true));
            chart.Points.Add(((0.3127, 0.3290), Brushes.White, "D65"));
            _result.Children.Add(Ui.Card(Ui.H2(_display.Name),
                Ui.Table(new[] { S.T("Standard", "Стандарт"), S.T("Coverage", "Покриття"), S.T("Relative size", "Відносний розмір") }, rows, new double[] { 260, 140, 160 }),
                Ui.Caption(S.T($"Primaries: R {_display.R.x:F4} {_display.R.y:F4} · G {_display.G.x:F4} {_display.G.y:F4} · B {_display.B.x:F4} {_display.B.y:F4}",
                               $"Основні: R {_display.R.x:F4} {_display.R.y:F4} · G {_display.G.x:F4} {_display.G.y:F4} · B {_display.B.x:F4} {_display.B.y:F4}")),
                chart));
            _result.Children.Add(Ui.Card(Ui.H2(S.T("3D gamut (CIE Lab, drag to rotate, wheel to zoom)", "3D-гамут (CIE Lab, тягніть — обертати, коліщатко — масштаб)")),
                Ui.Caption(S.T("Solid = display; wireframes = sRGB (grey), DCI-P3 (orange), Rec.2020 (cyan).", "Суцільне = дисплей; каркаси = sRGB (сірий), DCI-P3 (помаранчевий), Rec.2020 (блакитний).")),
                new Gamut3D(_display, new[] { (Gamut.References[0], Colors.Gray), (Gamut.References[2], Color.FromRgb(0xF9, 0x73, 0x16)), (Gamut.References[3], Color.FromRgb(0x22, 0xD3, 0xEE)) })));
        }
    }

    /// <summary>Rotatable Lab gamut solid of an RGB space (matrix model, D65, gamma 2.2) with reference wireframes.</summary>
    public sealed class Gamut3D : Border
    {
        readonly PerspectiveCamera _cam = new() { FieldOfView = 40 };
        double _az = 35, _el = 25, _dist = 420;
        Point? _drag;

        static M3 Matrix(Gamut.Space s)
        {
            var p = M3.FromColumns(Cm.XyToXyz(s.R.x, s.R.y), Cm.XyToXyz(s.G.x, s.G.y), Cm.XyToXyz(s.B.x, s.B.y));
            var k = p.Inverse() * Cm.D65;
            return M3.FromColumns(p.Col(0) * k.X, p.Col(1) * k.Y, p.Col(2) * k.Z);
        }

        static Point3D ToLab(M3 m, double r, double g, double b)
        {
            var lab = Cm.Lab(m * new V3(Math.Pow(r, 2.2), Math.Pow(g, 2.2), Math.Pow(b, 2.2)), Cm.D65);
            return new Point3D(lab.Y, lab.X - 50, -lab.Z);   // a → x, L → y (up), b → z
        }

        public Gamut3D(Gamut.Space display, (Gamut.Space s, Color c)[] refs)
        {
            Height = 460; ClipToBounds = true; Background = Brushes.Transparent;
            var vp = new Viewport3D { Camera = _cam }; Child = vp;
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Color.FromRgb(200, 200, 200)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(90, 90, 90), new Vector3D(-1, -1, -1)));
            group.Children.Add(Solid(Matrix(display)));
            foreach (var (s, c) in refs) group.Children.Add(Wire(Matrix(s), c));
            vp.Children.Add(new ModelVisual3D { Content = group });
            UpdateCam();
            MouseLeftButtonDown += (_, e) => { _drag = e.GetPosition(this); CaptureMouse(); };
            MouseLeftButtonUp += (_, _) => { _drag = null; ReleaseMouseCapture(); };
            MouseMove += (_, e) => { if (_drag is Point p) { var q = e.GetPosition(this); _az -= (q.X - p.X) * 0.5; _el = Math.Clamp(_el + (q.Y - p.Y) * 0.5, -85, 85); _drag = q; UpdateCam(); } };
            MouseWheel += (_, e) => { _dist = Math.Clamp(_dist - e.Delta * 0.3, 150, 900); UpdateCam(); };
        }

        void UpdateCam()
        {
            double a = _az * Math.PI / 180, e = _el * Math.PI / 180;
            var pos = new Point3D(_dist * Math.Cos(e) * Math.Sin(a), _dist * Math.Sin(e), _dist * Math.Cos(e) * Math.Cos(a));
            _cam.Position = pos; _cam.LookDirection = new Vector3D(-pos.X, -pos.Y, -pos.Z); _cam.UpDirection = new Vector3D(0, 1, 0);
        }

        // RGB cube surface: 6 faces, n×n grid, coloured with the patch colour
        static GeometryModel3D Solid(M3 m, int n = 16)
        {
            var mesh = new MeshGeometry3D();
            var tex = new List<Color>();
            void Face(Func<double, double, (double r, double g, double b)> f)
            {
                int start = mesh.Positions.Count;
                for (int i = 0; i <= n; i++) for (int j = 0; j <= n; j++)
                {
                    var (r, g, b) = f(i / (double)n, j / (double)n);
                    mesh.Positions.Add(ToLab(m, r, g, b));
                    mesh.TextureCoordinates.Add(new Point(tex.Count % 64 / 64.0 + 1 / 128.0, tex.Count / 64 / 64.0 + 1 / 128.0));
                    tex.Add(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
                }
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
                {
                    int a = start + i * (n + 1) + j, b2 = a + 1, c = a + n + 1, d = c + 1;
                    mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b2);
                    mesh.TriangleIndices.Add(b2); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
                }
            }
            Face((u, v) => (u, v, 0)); Face((u, v) => (u, v, 1)); Face((u, v) => (u, 0, v)); Face((u, v) => (u, 1, v)); Face((u, v) => (0, u, v)); Face((u, v) => (1, u, v));
            // colours packed into a small texture (one texel per vertex)
            int rows = (tex.Count + 63) / 64;
            var wb = new System.Windows.Media.Imaging.WriteableBitmap(64, Math.Max(rows, 1), 96, 96, PixelFormats.Bgr32, null);
            var px = new int[64 * Math.Max(rows, 1)];
            for (int i = 0; i < tex.Count; i++) px[i] = tex[i].R << 16 | tex[i].G << 8 | tex[i].B;
            wb.WritePixels(new Int32Rect(0, 0, 64, Math.Max(rows, 1)), px, 64 * 4, 0);
            var brush = new ImageBrush(wb) { ViewportUnits = BrushMappingMode.Absolute };
            var mat = new MaterialGroup(); mat.Children.Add(new DiffuseMaterial(brush)); mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255))));
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        // cube edges as thin tubes made of quads
        static GeometryModel3D Wire(M3 m, Color c, int n = 24)
        {
            var mesh = new MeshGeometry3D();
            var corners = new[] { (0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1), (1, 1, 0), (1, 0, 1), (0, 1, 1), (1, 1, 1) };
            foreach (var a in corners) foreach (var b in corners)
            {
                int diff = Math.Abs(a.Item1 - b.Item1) + Math.Abs(a.Item2 - b.Item2) + Math.Abs(a.Item3 - b.Item3);
                if (diff != 1 || a.CompareTo(b) > 0) continue;
                for (int i = 0; i < n; i++)
                {
                    double t0 = i / (double)n, t1 = (i + 1) / (double)n;
                    var p0 = ToLab(m, a.Item1 + (b.Item1 - a.Item1) * t0, a.Item2 + (b.Item2 - a.Item2) * t0, a.Item3 + (b.Item3 - a.Item3) * t0);
                    var p1 = ToLab(m, a.Item1 + (b.Item1 - a.Item1) * t1, a.Item2 + (b.Item2 - a.Item2) * t1, a.Item3 + (b.Item3 - a.Item3) * t1);
                    int s = mesh.Positions.Count; var o = new Vector3D(0, 0.9, 0); var o2 = new Vector3D(0.9, 0, 0);
                    mesh.Positions.Add(p0 - o); mesh.Positions.Add(p0 + o); mesh.Positions.Add(p1 - o); mesh.Positions.Add(p1 + o);
                    mesh.Positions.Add(p0 - o2); mesh.Positions.Add(p0 + o2); mesh.Positions.Add(p1 - o2); mesh.Positions.Add(p1 + o2);
                    foreach (var k in new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 }) mesh.TriangleIndices.Add(s + k);
                }
            }
            var mat = new EmissiveMaterial(new SolidColorBrush(c));
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }
    }
}
