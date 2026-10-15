// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>One measured patch: device RGB (0..1) and absolute XYZ (cd/m²).</summary>
    public sealed record Patch(double R, double G, double B, V3 Xyz)
    {
        public bool IsGrey => Math.Abs(R - G) < 1e-6 && Math.Abs(G - B) < 1e-6;
        public bool Is(double r, double g, double b) => Math.Abs(R - r) < 1e-3 && Math.Abs(G - g) < 1e-3 && Math.Abs(B - b) < 1e-3;
    }

    /// <summary>Minimal reader/writer for ArgyllCMS CGATS files (.ti1 / .ti3 / .cal).</summary>
    public static class Cgats
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void WriteTi1(string path, IEnumerable<(double r, double g, double b)> rgb01, string descriptor = "HDRDisplayCalibrate")
        {
            var rows = rgb01.ToList();
            var sb = new StringBuilder();
            sb.Append("CTI1\n\n").Append($"DESCRIPTOR \"{descriptor}\"\nORIGINATOR \"HDRDisplayCalibrate\"\nCOLOR_REP \"RGB\"\n");
            sb.Append("NUMBER_OF_FIELDS 7\nBEGIN_DATA_FORMAT\nSAMPLE_ID RGB_R RGB_G RGB_B XYZ_X XYZ_Y XYZ_Z\nEND_DATA_FORMAT\n");
            sb.Append($"NUMBER_OF_SETS {rows.Count}\nBEGIN_DATA\n");
            for (int i = 0; i < rows.Count; i++)
                sb.Append(string.Format(Inv, "{0} {1:F4} {2:F4} {3:F4} 50 50 50\n", i + 1, rows[i].r * 100, rows[i].g * 100, rows[i].b * 100));
            sb.Append("END_DATA\n");
            File.WriteAllText(path, sb.ToString());
        }

        static (string[] fields, List<string[]> rows, string text) Table(string path)
        {
            var text = File.ReadAllText(path, Encoding.Latin1);
            var fields = Regex.Match(text, @"BEGIN_DATA_FORMAT\s+(.*?)\s+END_DATA_FORMAT", RegexOptions.Singleline).Groups[1].Value
                              .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var data = Regex.Match(text, @"BEGIN_DATA\s+(.*?)\s+END_DATA\b", RegexOptions.Singleline).Groups[1].Value;
            var rows = data.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
                           .Select(l => l.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToList();
            return (fields, rows, text);
        }

        /// <summary>Reads a .ti3 and scales XYZ (normalised to white Y = 100) to absolute cd/m².</summary>
        public static List<Patch> ReadTi3(string path, out double whiteNits)
        {
            var (f, rows, text) = Table(path);
            int I(string n) => Array.IndexOf(f, n);
            var m = Regex.Match(text, "LUMINANCE_XYZ_CDM2\\s+\"([^\"]+)\"");
            whiteNits = m.Success ? double.Parse(m.Groups[1].Value.Split(' ')[1], Inv) : 100;
            // all-black readings come back as NaN (normalised to a white of 0): the display was off or the sensor not on it
            if (whiteNits <= 0 || Regex.IsMatch(File.ReadAllText(path), @"\s-?nan\b"))
                throw new InvalidDataException(S.T("The sensor saw no light: the display went to sleep or the sensor is not on this screen. Measure again.",
                                                   "Датчик не побачив світла: екран заснув або датчик не на цьому екрані. Заміряйте ще раз."));
            double k = whiteNits / 100.0;
            int r = I("RGB_R"), g = I("RGB_G"), b = I("RGB_B"), x = I("XYZ_X"), y = I("XYZ_Y"), z = I("XYZ_Z");
            return rows.Select(c => new Patch(double.Parse(c[r], Inv) / 100, double.Parse(c[g], Inv) / 100, double.Parse(c[b], Inv) / 100,
                                              new V3(double.Parse(c[x], Inv) * k, double.Parse(c[y], Inv) * k, double.Parse(c[z], Inv) * k))).ToList();
        }

        /// <summary>Reads a .cal (video LUT) as input → (R, G, B) arrays.</summary>
        public static (double[] x, double[] r, double[] g, double[] b) ReadCal(string path)
        {
            var (f, rows, _) = Table(path);
            int I(string n) => Array.IndexOf(f, n);
            double[] Col(int i) => rows.Select(c => double.Parse(c[i], Inv)).ToArray();
            return (Col(I("RGB_I")), Col(I("RGB_R")), Col(I("RGB_G")), Col(I("RGB_B")));
        }
    }
}
