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
        readonly CheckBox _ambient = Ui.Check(S.T("Adjust tone curve to room light (CIECAM02, dispcal -a)", "Підлаштувати криву під освітлення кімнати (CIECAM02, dispcal -a)"), tip: S.T("Adapts the tone curve to how bright the room is (CIECAM02 viewing conditions). Useful when the room is much darker or brighter than a typical office.", "Підлаштовує тонову криву під яскравість кімнати (умови перегляду CIECAM02). Корисно, коли кімната значно темніша чи світліша за звичайний офіс."));
        readonly TextBox _lux = Ui.Box("");
        readonly CheckBox _install = Ui.Check(S.T("Install the profile for this display", "Встановити профіль для цього дисплея"), true, tip: S.T("When the profile is ready, installs it and makes it the default for this display.", "Коли профіль готовий, встановлює його й робить основним для цього дисплея."));
        readonly CheckBox _verify = Ui.Check(S.T("Verify afterwards (120 patches)", "Перевірити після (120 патчів)"), true, tip: S.T("Measures 120 patches with the new profile and shows the final accuracy.", "Міряє 120 патчів із новим профілем і показує підсумкову точність."));
        readonly TextBox _name = Ui.Box("", 360);
        readonly StackPanel _result = new();

        static readonly string[][] CurveArgs = { new[] { "-g2.2" }, new[] { "-g2.4" }, new[] { "-G2.4", "-f0" }, new[] { "-gs" }, new[] { "-gl" } };
        static readonly string[] CurveIds = { "2.2", "2.4", "bt1886", "srgb", "lstar" };

        public SdrPage() : base(S.T("SDR calibration", "SDR калібрування"),
            S.T("Grey scale and white point go into the video card LUT, the display's colours are described by an ICC profile. Run the live meter first if the monitor has RGB controls.",
                "Сіра шкала і точка білого записуються в LUT відеокарти, кольори дисплея описує ICC-профіль. Якщо в моніторі є RGB-регулятори, спершу «Живий замір»."))
        {
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Targets", "Цілі")),
                Ui.Field(S.T("White point", "Точка білого"), _wp.View, tip: S.T("Target white: D65 for sRGB / Rec.709 work and games, D50 to match prints, “Native” keeps the panel's own white.", "Цільовий білий: D65 для роботи в sRGB / Rec.709 та ігор, D50 — під друк, «Рідна» лишає власний білий панелі.")),
                Ui.Field(S.T("White luminance, cd/m²", "Яскравість білого, кд/м²"), Ui.Row(_nits, Ui.Btn(S.T("From room light", "За освітленням кімнати"), FromRoom, false, "", tip: S.T("Measures the room with the diffuser and fills in a suitable white luminance.", "Міряє кімнату з розсіювачем і підставляє відповідну яскравість білого."))), S.T("Empty = native (maximum)", "Порожньо = рідна (максимум)"), tip: S.T("Target brightness of white: around 120 for a normal room, 80–100 for a dark room.", "Цільова яскравість білого: близько 120 для звичайної кімнати, 80–100 для темної.")),
                Ui.Field(S.T("Tone curve", "Тональна крива"), _curve, tip: S.T("How greys rise from black to white: 2.2 for PC and games, 2.4 or BT.1886 for video in a dark room, sRGB for the exact sRGB curve.", "Як сірі зростають від чорного до білого: 2.2 для ПК та ігор, 2.4 або BT.1886 для відео в темній кімнаті, sRGB — точна крива sRGB.")),
                Ui.Field(S.T("Room light, lx", "Освітлення кімнати, лк"), Ui.Row(_lux), S.T("Measured with the diffuser; used for the suggestion and the optional adjustment", "Міряється з розсіювачем; для підказки і необов'язкової корекції"), tip: S.T("Room illuminance used to adapt the tone curve. “From room light” fills it in.", "Освітленість кімнати для підлаштування тонової кривої. Її заповнює кнопка «З освітлення кімнати».")),
                _ambient));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Quality", "Якість")),
                Ui.Field(S.T("Calibration quality", "Якість калібрування"), _quality, S.T("High = more grey-scale iterations", "Висока = більше ітерацій сірої шкали"), tip: S.T("How many measurements the grey-scale calibration takes. Higher is smoother but slower.", "Скільки замірів робить калібрування сірої шкали. Вища якість — плавніше, але довше.")),
                Ui.Field(S.T("Profile patches", "Патчів у профілі"), _patches, S.T("~3–15 s per patch depending on the instrument and darkness", "~3–15 с на патч залежно від датчика й темряви"), tip: S.T("Number of colour patches for the ICC profile. More patches describe the display more precisely but take longer (~3–15 s each).", "Кількість кольорових патчів для ICC-профілю. Більше патчів — точніший опис дисплея, але довше (~3–15 с кожен).")),
                Ui.Field(S.T("Profile type", "Тип профілю"), _ptype, tip: S.T("XYZ LUT + matrix describes the display most precisely; matrix / shaper is simpler and works with more apps.", "XYZ LUT + матриця описує дисплей найточніше; матриця / шейпер простіший і працює з більшою кількістю програм.")),
                Ui.Field(S.T("Name", "Назва"), _name, S.T("Empty = display name and date", "Порожньо = назва дисплея і дата"), tip: S.T("File name for the calibration and the profile.", "Назва файлів калібрування та профілю.")),
                _install, _verify,
                Ui.Row(Ui.Btn(S.T("Start calibration", "Почати калібрування"), Run, true, "", tip: S.T("Runs the calibration and profiling. Keep the sensor on the screen; Esc cancels.", "Запускає калібрування і профілювання. Датчик має бути на екрані; Esc скасовує.")))));
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
            St.Report(S.F("Room {0:F1} lx → {1:F0} cd/m²", "Кімната {0:F1} лк → {1:F0} кд/м²", lux, sug), 1);
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
                // the white point correction costs some light: check the target luminance can be reached at all,
                // otherwise dispcal keeps chasing it for hours and the white point suffers
                if (!double.IsNaN(nits) && St.Setup.OwnWindow)
                {
                    double native;
                    using (var rd = new Jobs.LiveReader(fast: true)) native = (await rd.Read(1, 1, 1, ct)).Y;
                    double reach = native * 0.95;
                    if (nits > reach)
                    {
                        var ans = MessageBox.Show(S.F("At the current monitor brightness white reaches {0:F0} cd/m²; after the white point correction about {1:F0} cd/m² remain, below the target of {2:F0} cd/m².\n\nYes: raise the monitor's own brightness over DDC/CI\nNo: calibrate to {1:F0} cd/m²\nCancel: stop",
                                                      "На поточній яскравості монітора білий дає {0:F0} кд/м²; після корекції точки білого залишиться близько {1:F0} кд/м², менше за ціль {2:F0} кд/м².\n\nТак: підняти яскравість самого монітора через DDC/CI\nНі: калібрувати на {1:F0} кд/м²\nСкасувати: зупинити", native, reach, nits),
                                                  "HDRDisplayCalibrate", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                        if (ans == MessageBoxResult.Cancel) throw new OperationCanceledException();
                        if (ans == MessageBoxResult.Yes)
                        {
                            St.Report(S.T("Raising monitor brightness…", "Піднімаю яскравість монітора…"));
                            var res = await Jobs.AutoOsd(d, null, nits / 0.95, dir, ct);
                            Log.Write("brightness for calibration: " + res);
                            if (St.Setup.OwnWindow) Argyll.LutLinear(disp);
                        }
                        else
                        {
                            int bi = a.FindIndex(x => x.StartsWith("-b"));
                            a[bi] = string.Format(CultureInfo.InvariantCulture, "-b{0:0}", Math.Floor(reach));
                        }
                    }
                }
                var (code, outp) = await Jobs.RunMeasuring("dispcal", a, dir, S.T("Calibration", "Калібрування"), ct);
                if (code != 0) throw new Exception(Jobs.LastError(outp) ?? "dispcal failed");
                var cal = Path.Combine(dir, name + ".cal");
                // with our own patch window dispcal has no VideoLUT access and marks the curves as "not for the LUT";
                // we load them into the LUT ourselves, so mark them as LUT curves, otherwise colprof leaves vcgt out of the profile
                File.WriteAllText(cal, File.ReadAllText(cal).Replace("VIDEO_LUT_CALIBRATION_POSSIBLE \"NO\"", "VIDEO_LUT_CALIBRATION_POSSIBLE \"YES\""));
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
                    // the patches were measured through these curves: attach them so the profile carries its calibration (vcgt)
                    File.AppendAllText(Path.Combine(dir, name + ".ti3"), Environment.NewLine + File.ReadAllText(cal));
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
