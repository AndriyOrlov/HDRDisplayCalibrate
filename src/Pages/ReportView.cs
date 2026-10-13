// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>Renders a verification <see cref="Report"/>: summary, grey table, tone curve, ΔE bars, chromaticity.</summary>
    public static class ReportView
    {
        static Brush Grade(double de) => de < 1 ? Ui.Good : de < 3 ? Ui.Warm : Ui.Bad;

        public static UIElement Build(Report r, string curve, (double x, double y) target, string title)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.H2(title));
            var tiles = new WrapPanel();
            void Tile(string label, string value, Brush b = null)
            {
                var t = Ui.Stack(Ui.Caption(label), new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = b ?? Brushes.White });
                tiles.Children.Add(new Border { Child = t, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(8), Background = Ui.CardBg, MinWidth = 150 });
            }
            Tile(S.T("White", "Білий"), $"{r.WhiteNits:F1} cd/m²");
            Tile(S.T("White point", "Точка білого"), $"{r.WhiteCct:F0} K · ΔE {r.WhiteDeltaE:F2}", Grade(r.WhiteDeltaE));
            Tile(S.T("Black / contrast", "Чорний / контраст"), $"{r.BlackNits:F3} · {r.Contrast:F0}:1");
            Tile(S.T("Gamma (mid)", "Гамма (середні)"), Ui.F(r.GammaMid));
            Tile(S.T("Grey ΔE avg / max", "Сірий ΔE сер. / макс."), $"{r.GreyMeanDE:F2} / {r.GreyMaxDE:F2}", Grade(r.GreyMeanDE));
            Tile(S.T("Colour ΔE avg / max", "Кольори ΔE сер. / макс."), $"{r.ColorMeanDE:F2} / {r.ColorMaxDE:F2}", Grade(r.ColorMeanDE));
            sp.Children.Add(tiles);

            var tone = new LineChart { XLabel = S.T("input level", "вхідний рівень"), YLabel = S.T("relative luminance", "відносна яскравість"), XMin = 0, XMax = 1, YMin = 0, YMax = 1 };
            var steps = Enumerable.Range(0, 101).Select(i => i / 100.0).ToList();
            double kRel = r.BlackNits / Math.Max(r.WhiteNits, 1e-6);
            tone.Data.Add(new LineChart.Series(S.T("target", "ціль") + $" ({curve})", steps.Select(v => (v, Cm.Eotf(curve, v, kRel))).ToList(), Ui.Muted, true));
            tone.Data.Add(new LineChart.Series(S.T("measured", "виміряно"), r.Greys.Select(g => (g.Level, (g.Nits - r.BlackNits) / (r.WhiteNits - r.BlackNits))).ToList(), Ui.Accent, false, true));
            var gamma = new LineChart { XLabel = S.T("input level", "вхідний рівень"), YLabel = S.T("effective gamma", "ефективна гамма"), XMin = 0, XMax = 1, YMin = 1.6, YMax = 2.8, Height = 220 };
            gamma.Data.Add(new LineChart.Series(S.T("measured", "виміряно"), r.Greys.Where(g => g.Gamma != null).Select(g => (g.Level, g.Gamma.Value)).ToList(), Ui.Warm, false, true));

            var de = new DeltaEChart();
            foreach (var p in r.Patches) de.Bars.Add((Color.FromRgb((byte)(p.P.R * 255), (byte)(p.P.G * 255), (byte)(p.P.B * 255)), p.DeltaE));

            var xy = new ChromaticityChart { Height = 380 };
            xy.Triangles.Add(new ChromaticityChart.Tri("sRGB", (0.64, 0.33), (0.30, 0.60), (0.15, 0.06), Ui.Muted, false, true));
            var prim = new[] { (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0) }.Select(c => r.Patches.FirstOrDefault(p => p.P.Is(c.Item1, c.Item2, c.Item3))).ToList();
            if (prim.All(p => p != null)) xy.Triangles.Add(new ChromaticityChart.Tri(S.T("measured", "виміряно"), prim[0].P.Xyz.Xy, prim[1].P.Xyz.Xy, prim[2].P.Xyz.Xy, Ui.Accent, true));
            xy.Points.Add((target, Brushes.White, S.T("target", "ціль")));
            xy.Points.Add((r.WhiteXy, Ui.Warm, S.T("white", "білий")));

            var charts = new Grid();
            charts.ColumnDefinitions.Add(new ColumnDefinition()); charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); charts.ColumnDefinitions.Add(new ColumnDefinition());
            var left = Ui.Stack(Ui.Caption(S.T("Tone response", "Тональна крива")), tone, Ui.Caption(S.T("Effective gamma per step", "Ефективна гамма по кроках")), gamma);
            var right = Ui.Stack(Ui.Caption(S.T("Primaries and white (CIE 1931 xy)", "Основні кольори і білий (CIE 1931 xy)")), xy);
            Grid.SetColumn(right, 2); charts.Children.Add(left); charts.Children.Add(right);
            sp.Children.Add(charts);
            sp.Children.Add(Ui.Caption(S.T("ΔE2000 per patch (bar colour = patch colour; dashed lines at 1, 2, 3)", "ΔE2000 по патчах (колір стовпця = колір патча; пунктир на 1, 2, 3)")));
            sp.Children.Add(de);

            sp.Children.Add(Ui.Caption(S.T("Grey scale", "Сіра шкала")));
            sp.Children.Add(Ui.Table(new[] { S.T("Level", "Рівень"), "cd/m²", S.T("Gamma", "Гамма"), "x", "y", "ΔE2000" },
                r.Greys.Select(g => new[] { $"{g.Level * 100:F0}%", Ui.F(g.Nits, 3), g.Gamma is double v ? Ui.F(v, 3) : "—", Ui.F(g.X, 4), Ui.F(g.Y, 4), Ui.F(g.DeltaE) })));
            return sp;
        }
    }
}
