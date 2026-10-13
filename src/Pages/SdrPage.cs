// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>SDR calibration (video LUT) + ICC profile + install + verification, like DisplayCAL.</summary>
    public sealed class SdrPage : Page
    {
        readonly WhitePicker _wp = new();
        readonly TextBox _nits = Ui.Box("120");
        readonly ComboBox _curve = Ui.Combo(new[] { S.T("Gamma 2.2 (PC, games)", "Гамма 2.2 (ПК, ігри)"), S.T("Gamma 2.4 (dark room)", "Гамма 2.4 (темна кімната)"), "BT.1886 (TV, video)", "sRGB", "L*" });
        readonly ComboBox _quality = Ui.Combo(new[] { S.T("Fast", "Швидко"), S.T("Medium", "Середньо"), S.T("High (slow)", "Висока (довго)") }, 2);
        readonly ComboBox _patches = Ui.Combo(new[] { S.T("No profile (calibration only)", "Без профілю (лише калібрування)"), "120", "250", "400", "600", "1000", "2000" }, 4);
        readonly ComboBox _ptype = Ui.Combo(new[] { S.T("XYZ LUT + matrix (best)", "XYZ LUT + матриця (найкраще)"), S.T("Matrix / shaper (simple)", "Матриця / шейпер (простий)") });
        readonly CheckBox _ambient = Ui.Check(S.T("Adjust tone curve to room light (CIECAM02, dispcal -a)", "Підлаштувати криву під освітлення кімнати (CIECAM02, dispcal -a)"));
        readonly TextBox _lux = Ui.Box("");
        readonly CheckBox _install = Ui.Check(S.T("Install the profile for this display", "Встановити профіль для цього дисплея"), true);
        readonly CheckBox _verify = Ui.Check(S.T("Verify afterwards (120 patches)", "Перевірити після (120 патчів)"), true);
        readonly TextBox _name = Ui.Box("", 360);
        readonly StackPanel _result = new();

        static readonly string[][] CurveArgs = { new[] { "-g2.2" }, new[] { "-g2.4" }, new[] { "-G2.4", "-f0" }, new[] { "-gs" }, new[] { "-gl" } };
        static readonly string[] CurveIds = { "2.2", "2.4", "bt1886", "srgb", "lstar" };

        public SdrPage() : base(S.T("SDR calibration", "SDR калібрування"),
            S.T("Grey scale and white point go into the video card LUT, the display's colours are described by an ICC profile. Run the live meter first if the monitor has RGB controls.",
                "Сіра шкала і точка білого записуються в LUT відеокарти, кольори дисплея описує ICC-профіль. Якщо в моніторі є RGB-регулятори, спершу «Живий замір»."))
        {
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Targets", "Цілі")),
                Ui.Field(S.T("White point", "Точка білого"), _wp.View),
                Ui.Field(S.T("White luminance, cd/m²", "Яскравість білого, кд/м²"), Ui.Row(_nits, Ui.Btn(S.T("From room light", "За освітленням кімнати"), FromRoom, false, "")), S.T("Empty = native (maximum)", "Порожньо = рідна (максимум)")),
                Ui.Field(S.T("Tone curve", "Тональна крива"), _curve),
                Ui.Field(S.T("Room light, lx", "Освітлення кімнати, лк"), Ui.Row(_lux), S.T("Measured with the diffuser; used for the suggestion and the optional adjustment", "Міряється з розсіювачем; для підказки і необов'язкової корекції")),
                _ambient));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Quality", "Якість")),
                Ui.Field(S.T("Calibration quality", "Якість калібрування"), _quality, S.T("High = more grey-scale iterations", "Висока = більше ітерацій сірої шкали")),
                Ui.Field(S.T("Profile patches", "Патчів у профілі"), _patches, S.T("~3–15 s per patch depending on the instrument and darkness", "~3–15 с на патч залежно від датчика й темряви")),
                Ui.Field(S.T("Profile type", "Тип профілю"), _ptype),
                Ui.Field(S.T("Name", "Назва"), _name, S.T("Empty = display name and date", "Порожньо = назва дисплея і дата")),
                _install, _verify,
                Ui.Row(Ui.Btn(S.T("Start calibration", "Почати калібрування"), Run, true, ""))));
            Body.Children.Add(_result);
            var rl = St.Cfg.Num("room_lux", double.NaN);
            if (!double.IsNaN(rl)) _lux.Text = rl.ToString("0.#", CultureInfo.InvariantCulture);
        }

        void FromRoom() => RunJob(async ct =>
        {
            var (lux, _) = await Jobs.Ambient(ct);
            _lux.Text = lux.ToString("0.#", CultureInfo.InvariantCulture);
            double sug = lux <= 2 ? 100 : lux >= 1000 ? 220 : Cm.Interp(Math.Log10(lux), new[] { Math.Log10(2), 1, Math.Log10(50), Math.Log10(150), Math.Log10(400), 3 }, new[] { 100.0, 120, 140, 160, 185, 220 });
            _nits.Text = sug.ToString("0", CultureInfo.InvariantCulture);
            St.Report(S.T($"Room {lux:F1} lx → {sug:F0} cd/m²", $"Кімната {lux:F1} лк → {sug:F0} кд/м²"), 1);
        });

        void Run()
        {
            var d = St.Display;
            if (d?.ArgyllNumber is not int disp) { MessageBox.Show(S.T("Select a display first.", "Спершу виберіть дисплей.")); return; }
            if (d.HdrOn && MessageBox.Show(S.T("HDR is on for this display. SDR calibration should be done with HDR off. Continue anyway?", "Для цього дисплея увімкнено HDR. SDR-калібрування роблять з вимкненим HDR. Продовжити?"), "HDRDisplayCalibrate", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var wp = _wp.Xy(); double nits = Ui.Num(_nits, double.NaN), lux = Ui.Num(_lux, double.NaN);
            int ci = Math.Max(0, _curve.SelectedIndex);
            string name = string.IsNullOrWhiteSpace(_name.Text) ? $"{d.Model}_{DateTime.Now:yyyy-MM-dd}" : _name.Text.Trim();
            name = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
            int npatch = _patches.SelectedIndex == 0 ? 0 : int.Parse((string)_patches.SelectedItem);
            RunJob(async ct =>
            {
                var dir = Paths.Session(name);
                // 1. calibration
                var a = St.Setup.Args(); a.Add("-v"); a.Add("-m"); a.Add("-q" + "lmh"[Math.Max(0, _quality.SelectedIndex)]);
                if (wp is (double x, double y)) a.Add(string.Format(CultureInfo.InvariantCulture, "-w{0:F4},{1:F4}", x, y));
                if (!double.IsNaN(nits)) a.Add(string.Format(CultureInfo.InvariantCulture, "-b{0:0.#}", nits));
                a.AddRange(CurveArgs[ci]);
                if (_ambient.IsChecked == true && !double.IsNaN(lux)) a.Add(string.Format(CultureInfo.InvariantCulture, "-a{0:0.#}", lux));
                a.Add(name);
                if (St.Setup.OwnWindow) Argyll.LutLinear(disp);
                var (code, outp) = await Jobs.RunMeasuring("dispcal", a, dir, S.T("Calibration", "Калібрування"), ct);
                if (code != 0) throw new Exception(Jobs.LastError(outp) ?? "dispcal failed");
                var cal = Path.Combine(dir, name + ".cal");
                Argyll.LutLoad(disp, cal);
                string icc = null;
                // 2. profile
                if (npatch > 0)
                {
                    var tg = new List<string> { "-v", "-d3", "-G", "-e4", "-B4", $"-s{Math.Clamp(npatch / 40, 5, 33)}", $"-g{Math.Clamp(npatch / 12, 9, 96)}", $"-f{npatch}", name };
                    var (tc, to) = await Argyll.RunAsync("targen", tg, dir, S.T("Patch set", "Набір патчів"), null, ct);
                    if (tc != 0) throw new Exception(Jobs.LastError(to) ?? "targen failed");
                    var ra = St.Setup.Args(); ra.Add("-v"); ra.Add(name);
                    var (rc, ro) = await Jobs.RunMeasuring("dispread", ra, dir, S.T("Profile", "Профіль"), ct);
                    if (rc != 0) throw new Exception(Jobs.LastError(ro) ?? "dispread failed");
                    var cp = new List<string> { "-v", "-qh", _ptype.SelectedIndex == 0 && npatch >= 250 ? "-aX" : "-aS", "-D", name, "-C", "HDRDisplayCalibrate, ArgyllCMS", name };
                    var (cc, co) = await Argyll.RunAsync("colprof", cp, dir, S.T("Building profile", "Побудова профілю"), new Progress<Progress>(p => St.Report(S.T("Building profile…", "Будую профіль…"))), ct);
                    if (cc != 0) throw new Exception(Jobs.LastError(co) ?? "colprof failed");
                    icc = Path.Combine(dir, name + ".icm");
                    if (_install.IsChecked == true) { Argyll.InstallProfile(disp, icc); Log.Write($"installed {icc}"); }
                }
                // 3. verification
                if (_verify.IsChecked == true)
                {
                    var m = await Jobs.Measure(Jobs.VerifyChart(), dir, "verify", S.T("Verification", "Перевірка"), ct);
                    var rep = Report.Build(m, CurveIds[ci], wp ?? m.First(p => p.Is(1, 1, 1)).Xyz.Xy);
                    Dispatcher.Invoke(() =>
                    {
                        _result.Children.Clear();
                        _result.Children.Add(Ui.Card(ReportView.Build(rep, CurveIds[ci], wp ?? rep.WhiteXy, S.T("Result", "Результат")),
                            Ui.Caption(S.T("Files: ", "Файли: ") + dir)));
                    });
                }
                St.Report(S.T("SDR calibration done", "SDR калібрування завершено") + (icc != null ? " · " + Path.GetFileName(icc) : ""), 1);
            });
        }
    }
}
