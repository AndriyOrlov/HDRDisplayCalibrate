// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HDRDisplayCalibrate.Core;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>3D LUT export (ArgyllCMS collink): madVR .3dlut, Resolve/OBS .cube, eeColor, ReShade PNG.</summary>
    public sealed class LutPage : Page
    {
        static readonly (string name, string file)[] Sources =
        {
            ("Rec.709 / BT.709", "Rec709.icm"), ("sRGB", "sRGB.icm"), ("Display P3", "DisplayP3.icm"),
            ("DCI-P3 (SMPTE 431)", "SMPTE431_P3.icm"), ("Rec.2020", "Rec2020.icm"), ("Adobe RGB (Clay RGB 1998)", "ClayRGB1998.icm"),
        };
        readonly ComboBox _src = Ui.Combo(Sources.Select(s => s.name), 0);
        readonly ComboBox _eotf = Ui.Combo(new[] { "BT.1886 (video)", S.T("Gamma 2.2", "Гамма 2.2"), S.T("Gamma 2.4", "Гамма 2.4"), S.T("Source profile curve (e.g. sRGB)", "Крива профілю джерела (напр. sRGB)") }, 0);
        readonly ComboBox _intent = Ui.Combo(new[] { S.T("Relative colorimetric (clip)", "Відносний колориметричний (обрізати)"), S.T("Perceptual with gamut mapping", "Перцептивний з відображенням гамуту") }, 0);
        readonly ComboBox _size = Ui.Combo(new[] { "17³", "33³", "65³" }, 1);
        readonly ComboBox _fmt = Ui.Combo(new[] { "IRIDAS .cube (DaVinci Resolve, OBS, games)", "madVR .3dlut", "eeColor .txt", "ReShade LUT .png (from .cube)" }, 0);
        readonly CheckBox _cal = Ui.Check(S.T("Include the display calibration (vcgt) in the LUT", "Включити калібрування дисплея (vcgt) у LUT"), true, tip: S.T("Bakes the profile's calibration curve into the LUT, so the LUT alone gives the calibrated picture. The video card curve should then be linear while the LUT is used.", "Вбудовує калібрувальну криву профілю в LUT, тож один LUT дає відкаліброване зображення. Тоді крива відеокарти має бути лінійною, поки використовується LUT."));
        readonly TextBox _dest = Ui.Box("", 520);
        readonly TextBlock _out = Ui.Caption("");

        public LutPage() : base("3D LUT", S.T("Turns the display profile into a 3D LUT for players and apps that apply their own LUT (madVR, Resolve, OBS, ReShade).",
                                             "Перетворює профіль дисплея на 3D-LUT для програм, що застосовують свій LUT (madVR, Resolve, OBS, ReShade)."))
        {
            Body.Children.Add(Ui.Card(
                Ui.Field(S.T("Source colour space", "Колірний простір джерела"), _src, tip: S.T("Colour space of the content: Rec.709 / sRGB for most video, games and the web; DCI-P3 or Rec.2020 for wide-gamut content.", "Колірний простір вмісту: Rec.709 / sRGB для більшості відео, ігор і вебу; DCI-P3 або Rec.2020 для широкого гамуту.")),
                Ui.Field(S.T("Source tone curve", "Крива джерела"), _eotf, tip: S.T("The tone curve the content was mastered for: BT.1886 for video, gamma 2.2 for PC content and games.", "Тонова крива, під яку зроблено вміст: BT.1886 для відео, гамма 2.2 для ПК-вмісту та ігор.")),
                Ui.Field(S.T("Rendering", "Відтворення"), _intent, tip: S.T("What happens to colours the display cannot show: clipped exactly (relative colorimetric) or compressed smoothly (perceptual).", "Що робити з кольорами, яких дисплей не може показати: точно обрізати (відносний колориметричний) або плавно стиснути (перцептивний).")),
                Ui.Field(S.T("LUT size", "Розмір LUT"), _size, tip: S.T("Grid points per axis. 33³ is the usual choice; 65³ is slightly more precise and much larger.", "Кількість вузлів на вісь. 33³ — звичайний вибір; 65³ трохи точніший і значно більший.")),
                Ui.Field(S.T("Format", "Формат"), _fmt, tip: S.T("File type for your app: .cube for DaVinci Resolve, OBS and many games, .3dlut for madVR, eeColor box, or a ReShade PNG.", "Тип файлу для вашої програми: .cube для DaVinci Resolve, OBS і багатьох ігор, .3dlut для madVR, коробка eeColor або PNG для ReShade.")),
                Ui.Field(S.T("Display profile", "Профіль дисплея"), Ui.Row(_dest, Ui.Btn("…", Pick, tip: S.T("Choose another ICC/ICM profile.", "Вибрати інший профіль ICC/ICM."))), tip: S.T("The calibrated ICC profile of the display the LUT is made for.", "Відкалібрований ICC-профіль дисплея, для якого робиться LUT.")),
                _cal,
                Ui.Row(Ui.Btn(S.T("Create 3D LUT", "Створити 3D-LUT"), Run, true, "", tip: S.T("Builds the LUT with ArgyllCMS collink and saves it to the measurements folder.", "Будує LUT через ArgyllCMS collink і зберігає його в теці замірів."))), _out));
        }

        public override void OnShow()
        {
            if (string.IsNullOrWhiteSpace(_dest.Text) && St.Display != null && ColorProfiles.Default(St.Display, false) is string n)
                _dest.Text = Path.Combine(ColorProfiles.ColorDir, n);
        }

        void Pick()
        {
            var dlg = new OpenFileDialog { Filter = "ICC profiles (*.icc;*.icm)|*.icc;*.icm", InitialDirectory = ColorProfiles.ColorDir };
            if (dlg.ShowDialog() == true) _dest.Text = dlg.FileName;
        }

        void Run()
        {
            var dest = _dest.Text.Trim();
            if (!File.Exists(dest)) { MessageBox.Show(S.T("Choose the display's ICC profile.", "Виберіть ICC-профіль дисплея.")); return; }
            var src = Path.GetFullPath(Path.Combine(Argyll.Bin, "..", "ref", Sources[_src.SelectedIndex].file));
            int res = new[] { 17, 33, 65 }[_size.SelectedIndex]; int fmt = _fmt.SelectedIndex;
            int eotf = _eotf.SelectedIndex, intent = _intent.SelectedIndex; bool withCal = _cal.IsChecked == true;
            RunJob(async ct =>
            {
                var dir = Paths.Session("3DLUT");
                var name = $"{Path.GetFileNameWithoutExtension(dest)}_{Path.GetFileNameWithoutExtension(src)}_{res}";
                var a = new List<string> { "-v", "-qh", $"-r{res}", "-3", fmt == 1 ? "m" : fmt == 2 ? "e" : "c" };
                if (eotf == 0) { a.Add("-I"); a.Add("B"); } else if (eotf is 1 or 2) { a.Add("-I"); a.Add(eotf == 1 ? "g:2.2" : "g:2.4"); }
                if (intent == 1) { a.Add("-G"); a.Add("-ip"); } else a.Add("-ir");
                if (withCal)
                {
                    var cal = Path.Combine(dir, "display.cal");
                    var (cc, _) = await Argyll.RunAsync("iccvcgt", new[] { "-x", dest, cal }, dir, "vcgt", null, ct);
                    if (cc == 0 && File.Exists(cal)) { a.Add("-a"); a.Add(cal); }
                }
                a.Add(src); a.Add(dest); a.Add(name + ".icm");
                var (code, outp) = await Argyll.RunAsync("collink", a, dir, "collink", new Progress<Progress>(_ => St.Report(S.T("Building 3D LUT…", "Будую 3D-LUT…"))), ct);
                if (code != 0) throw new Exception(Jobs.LastError(outp) ?? "collink failed");
                var made = Directory.GetFiles(dir, name + ".*").Where(f => !f.EndsWith(".icm")).ToList();
                if (fmt == 3)
                {
                    var cube = made.FirstOrDefault(f => f.EndsWith(".cube", StringComparison.OrdinalIgnoreCase));
                    if (cube != null) made.Add(CubeToReShadePng(cube));
                }
                Dispatcher.Invoke(() => _out.Text = S.T("Created:\n", "Створено:\n") + string.Join("\n", made));
                St.Report(S.T("3D LUT ready", "3D-LUT готовий"), 1);
            });
        }

        /// <summary>ReShade LUT.fx strip: N tiles of N×N, red → x, green → y, blue → tile.</summary>
        static string CubeToReShadePng(string cube)
        {
            int n = 0; var vals = new List<(double, double, double)>();
            foreach (var line in File.ReadLines(cube))
            {
                var t = line.Trim();
                if (t.StartsWith("LUT_3D_SIZE")) n = int.Parse(t.Split()[1]);
                else if (t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '-' || t[0] == '.'))
                {
                    var p = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                    if (p.Length == 3) vals.Add((p[0], p[1], p[2]));
                }
            }
            var px = new byte[n * n * n * 3];
            for (int b = 0; b < n; b++) for (int g = 0; g < n; g++) for (int r = 0; r < n; r++)
            {
                var (R, G, B) = vals[r + g * n + b * n * n];       // .cube: red fastest
                int x = b * n + r, y = g, o = (y * n * n + x) * 3;
                px[o] = (byte)Math.Round(Math.Clamp(R, 0, 1) * 255); px[o + 1] = (byte)Math.Round(Math.Clamp(G, 0, 1) * 255); px[o + 2] = (byte)Math.Round(Math.Clamp(B, 0, 1) * 255);
            }
            var bmp = BitmapSource.Create(n * n, n, 96, 96, PixelFormats.Rgb24, null, px, n * n * 3);
            var path = Path.ChangeExtension(cube, ".png");
            using var fs = File.Create(path);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(fs);
            return path;
        }
    }
}
