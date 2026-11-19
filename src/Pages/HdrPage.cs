// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Modified 2026 by DMD61 (Victory61): guided sensor dialogs before/between/after the steps (ProbeGuide);
// short "Before you start" box, HDR switched on by itself, OSD read over DDC/CI, scroll to the next step,
// name of the final profile, closing window (SDR slider back, test profiles deleted), report export.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>
    /// HDR calibration wizard (Windows Advanced Color, MHC2 profile) with an ordinary colorimeter:
    /// neutral profile → PQ response → peak probe → matrix + PQ curve → per-channel grey balance →
    /// compensation for the SDR video LUT → verification.
    /// </summary>
    public sealed class HdrPage : Page
    {
        readonly StackPanel _pre = new(), _steps = new(), _out = new();
        readonly TextBox _peak = Ui.Box(""), _knee = Ui.Box("");
        readonly List<Border> _cards = new();
        readonly List<TextBlock> _banners = new();
        readonly List<string> _tests = new();   // test profiles installed by steps 1–4 of this calibration
        bool _switching;
        string _dir;
        double _sdrWhite;
        List<Patch> _base, _probe, _v1m, _v2m;
        HdrPipeline.Response _resp;
        Mhc2Profile _v1, _v2, _final;

        public HdrPage() : base(S.T("HDR calibration", "HDR калібрування"),
            S.T("Builds a native Windows HDR (MHC2) profile from colorimeter readings: correct PQ tracking up to your monitor's real peak, D65 white at every level, accurate colours, and the real peak/black reported to games. No background app and no HDR test patterns needed.",
                "Будує рідний HDR-профіль Windows (MHC2) за замірами колориметра: правильне відстеження PQ до реального піку монітора, білий D65 на всіх рівнях, точні кольори і реальний пік/чорний для ігор. Без програм у фоні і без HDR-патернів."))
        {
            // only what everybody needs, in plain words
            var before = Ui.Guide(S.T("Before you start", "Перед початком"),
                S.T("Press “Open HDR settings”, move “SDR content brightness” all the way to the right (100), then come back here.", "Натисніть «Відкрити параметри HDR», посуньте «Яскравість SDR-вмісту» до кінця вправо (100) і поверніться сюди."),
                S.T("Set the monitor's own brightness (OSD) to 100: press “OSD brightness 100” or use the monitor's buttons.", "Поставте власну яскравість монітора (OSD) на 100: натисніть «Яскравість OSD 100» або скористайтеся кнопками монітора."),
                S.T("Press “Refresh”: the rows below get a ✓. The sensor row stays “!”: that is normal.", "Натисніть «Оновити»: рядки нижче отримають ✓. Рядок датчика лишається «!»: це нормально."),
                S.T("In step 1 press “Run”: a window then shows how to place the sensor.", "У кроці 1 натисніть «Виконати»: потім вікно покаже, як поставити датчик."));
            var sp = (StackPanel)before.Child;
            // the four points a little larger than normal text, so they are read first
            if (sp.Children.Count > 1 && sp.Children[1] is StackPanel list)
                foreach (var g in list.Children.OfType<Grid>())
                    foreach (var t in g.Children.OfType<TextBlock>()) t.FontSize = 15;
            sp.Children.Add(_pre);
            var nothing = Ui.Text(S.T("Nothing else needs to be touched.", "Більше нічого чіпати не треба."));
            nothing.FontWeight = FontWeights.SemiBold; nothing.FontSize = 15; nothing.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(nothing);
            Body.Children.Add(before);
            Body.Children.Add(_steps);
            Body.Children.Add(Ui.Row(Ui.Btn(S.T("Export report", "Експортувати звіт"), Export, false, "\uE74E", tip: S.T("Saves the results below (values, tables and charts) as a PDF or HTML file.", "Зберігає результати нижче (значення, таблиці й графіки) у файл PDF або HTML."))));
            Body.Children.Add(_out);
            BuildSteps();
        }

        /// <summary>On an HDR monitor the page switches Windows HDR on by itself; nothing else is changed.</summary>
        public override void OnShow()
        {
            var d = St.Display;
            if (d != null && St.Job == null && !_switching)
            {
                Displays.RefreshColor(d);
                if (d.HdrSupported && !d.HdrOn) { TurnHdrOn(d); return; }
            }
            Prereq();
        }

        async void TurnHdrOn(DisplayTarget d)
        {
            _switching = true;
            _pre.Children.Clear();
            _pre.Children.Add(Ui.Text(S.T("Turning Windows HDR on… (a few seconds)", "Вмикаю Windows HDR… (кілька секунд)")));
            St.Report(S.T("Turning Windows HDR on…", "Вмикаю Windows HDR…"));
            try { Displays.SetHdr(d, true); await Task.Delay(7000); St.RefreshDisplays(); }
            catch (Exception e) { Log.Write("HDR on: " + e.Message); }
            finally { _switching = false; }
            St.Report(S.T("Windows HDR is on", "Windows HDR увімкнено"), 0);
            Prereq();
        }

        /// <summary>Key under which the "SDR content brightness" found before the calibration is kept, per monitor.</summary>
        static string SdrKey(DisplayTarget d) => "sdr_before_" + d.Model;

        static string NoDdc => S.T("not readable over DDC/CI: use the monitor's buttons", "не читається через DDC/CI: скористайтеся кнопками монітора");

        void Prereq()
        {
            _pre.Children.Clear();
            var d = St.Display; if (d == null) return;
            Displays.RefreshColor(d);
            // remember the slider as the user had it (anything below 100), to put it back at the end
            if (d.HdrOn && St.Job == null && FinishDialog.Slider(d.SdrWhiteNits) < 100) { St.Cfg.Set(SdrKey(d), d.SdrWhiteNits); try { St.Cfg.Save(); } catch { } }
            bool sdrOk = FinishDialog.Slider(d.SdrWhiteNits) >= 100;
            string osdMark = "!", osdText = NoDdc;
            try
            {
                using var x = Ddc.Open(d);
                if (x?.Get(Ddc.Brightness) is { } b)
                {
                    osdMark = b.cur >= b.max ? "✓" : "!";
                    osdText = b.cur >= b.max ? S.F("{0} (maximum)", "{0} (максимум)", b.cur) : S.F("now {0}: set it to {1}", "зараз {0}: поставте {1}", b.cur, b.max);
                }
            }
            catch { }
            _pre.Children.Add(Ui.Table(new[] { "", "", "" }, new[]
            {
                new[] { d.HdrOn ? "✓" : "✗", S.T("Windows HDR is on for this display", "Windows HDR увімкнено для цього дисплея"), d.HdrOn ? "" : S.T("required", "обов'язково") },
                new[] { sdrOk ? "✓" : "!", S.F("“SDR content brightness” = {0} ({1:F0} nits)", "«Яскравість SDR-вмісту» = {0} ({1:F0} ніт)", FinishDialog.Slider(d.SdrWhiteNits), d.SdrWhiteNits), sdrOk ? "" : S.T("set it to 100 (point 1)", "поставте 100 (пункт 1)") },
                new[] { osdMark, S.T("Monitor OSD brightness", "Яскравість OSD монітора"), osdText },
                new[] { "!", S.T("Sensor", "Датчик"), S.T("the window of step 1 shows how to place it", "вікно кроку 1 покаже, як його поставити") },
            }, new double[] { 30, 330, 520 }));
            _pre.Children.Add(Ui.Row(
                Ui.Btn(S.T("Open HDR settings", "Відкрити параметри HDR"), ColorProfiles.OpenHdrSettings, false, "", tip: S.T("Opens Windows Settings → Display → HDR, where the “SDR content brightness” slider is.", "Відкриває Параметри Windows → Дисплей → HDR, де є повзунок «Яскравість SDR-вмісту».")),
                Ui.Btn(S.T("OSD brightness 100", "Яскравість OSD 100"), () => { try { using var x = Ddc.Open(d); if (x?.Get(Ddc.Brightness) is { } b) x.Set(Ddc.Brightness, b.max); } catch { } Prereq(); }, false, "", tip: S.T("Sets the monitor's own brightness to maximum over DDC/CI so it follows the PQ signal as closely as it can.", "Ставить власну яскравість монітора на максимум через DDC/CI, щоб він якомога точніше відстежував PQ-сигнал.")),
                Ui.Btn(S.T("Refresh", "Оновити"), Prereq, false, "", tip: S.T("Checks the HDR prerequisites again.", "Ще раз перевіряє умови для HDR."))));
        }

        void BuildSteps()
        {
            _steps.Children.Clear(); _cards.Clear(); _banners.Clear();
            _steps.Children.Add(Step(1, S.T("Measure the monitor as it is", "Заміряти монітор як є"),
                S.T("Installs a neutral HDR profile and measures 40 grey levels spread evenly in PQ plus primaries (~5 min).", "Встановлює нейтральний HDR-профіль і міряє 40 рівнів сірого, рівномірно по PQ, плюс основні кольори (~5 хв)."), Step1));
            _steps.Children.Add(Step(2, S.T("Find the real peak", "Знайти реальний пік"),
                S.T("A temporary ×8 probe profile pushes the same patches up to ~3800 nits of signal to find where the monitor clips (~4 min).", "Тимчасовий профіль-зонд ×8 доводить ті самі патчі до ~3800 ніт сигналу, щоб знайти, де монітор обрізає (~4 хв)."), Step2));
            _steps.Children.Add(Step(3, S.T("Build and check the first profile", "Побудувати й перевірити перший профіль"),
                S.T("Matrix for D65 and sRGB/Rec.2020 primaries, PQ curve with a soft roll-off to the peak.", "Матриця для D65 і основних кольорів, PQ-крива з плавним зрізом до піку."), Step3,
                Ui.Row(Ui.Text(S.T("Peak, nits", "Пік, ніт")), Ui.Tip(_peak, S.T("Peak luminance written into the profile, cd/m². Step 2 fills it in; a lower value keeps the brightest highlights more neutral.", "Пікова яскравість, яка записується в профіль, кд/м². Крок 2 заповнює її сам; менше значення робить найяскравіші світла нейтральнішими.")),
                       Ui.Text(S.T("roll-off from, nits", "зріз від, ніт")), Ui.Tip(_knee, S.T("Where the soft roll-off toward the peak starts, cd/m². Brighter signals above it are compressed smoothly instead of clipped.", "Звідки починається плавний зріз до піку, кд/м². Яскравіші сигнали вище нього плавно стискаються, а не обрізаються.")))));
            _steps.Children.Add(Step(4, S.T("Grey balance per channel", "Баланс сірого по каналах"),
                S.T("Separate R/G/B curves keep the white D65 from shadows to highlights.", "Окремі криві R/G/B тримають білий D65 від тіней до світел."), Step4));
            _steps.Children.Add(Step(5, S.T("Finalize and verify", "Завершити й перевірити"),
                S.T("Compensates for the SDR calibration curve Windows also applies in HDR, installs the final profile and verifies it, greys and 24 colours (skin, sky, foliage, saturated colours).", "Компенсує SDR-криву, яку Windows застосовує і в HDR, встановлює фінальний профіль і перевіряє його: сірі та 24 кольори (шкіра, небо, зелень, насичені кольори)."), Step5));
        }

        UIElement Step(int n, string title, string text, Action run, UIElement extra = null)
        {
            var banner = new TextBlock { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
            var card = Ui.Card(banner, Ui.Row(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Accent, Child = new TextBlock { Text = n.ToString(), Foreground = Brushes.Black, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                           Ui.H2(title)), Ui.Caption(text), extra, Ui.Row(Ui.Btn(S.T("Run", "Виконати"), run, true, "", tip: text)));
            _cards.Add(card); _banners.Add(banner);
            return card;
        }

        /// <summary>After a step: scroll to the next one, mark it and say what to press.</summary>
        void Next(int done) => Dispatcher.Invoke(() =>
        {
            for (int i = 0; i < _cards.Count; i++) { _cards[i].BorderBrush = Ui.CardStroke; _cards[i].BorderThickness = new Thickness(1); _banners[i].Visibility = Visibility.Collapsed; }
            if (done < 1 || done >= _cards.Count) return;
            var c = _cards[done];
            c.BorderBrush = Ui.Accent; c.BorderThickness = new Thickness(2);
            _banners[done].Text = S.F("Step {0} completed — now press “{1}” in step {2}.", "Крок {0} виконано — тепер натисніть «{1}» у кроці {2}.", done, S.T("Run", "Виконати"), done + 1);
            _banners[done].Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(() => c.BringIntoView(new Rect(0, 0, c.ActualWidth, c.ActualHeight))), System.Windows.Threading.DispatcherPriority.Loaded);
        });

        // ------------------------------------------------------------ helpers
        bool Ready()
        {
            var d = St.Display;
            if (d == null || d.ArgyllNumber == null) { MessageBox.Show(S.T("Select a display first.", "Спершу виберіть дисплей.")); return false; }
            Displays.RefreshColor(d);
            if (!d.HdrOn) { MessageBox.Show(S.T("Turn Windows HDR on for this display first.", "Спершу увімкніть Windows HDR для цього дисплея.")); return false; }
            _sdrWhite = d.SdrWhiteNits;
            _dir ??= Paths.Session("HDR_" + d.Model);
            return true;
        }

        /// <summary>Installs an HDR profile; Windows only applies it once it is picked in Settings, so ask the user to do that.</summary>
        async Task<string> Activate(Mhc2Profile p, string file)
        {
            var path = Path.Combine(_dir, file);
            p.Save(path);
            var name = ColorProfiles.Install(St.Display, path, true);
            var installed = Path.Combine(ColorProfiles.ColorDir, name);
            await Dispatcher.InvokeAsync(() =>
            {
                ColorProfiles.OpenDisplaySettings();
                new ProfilePrompt(p.Description, installed).ShowDialog();
            });
            await Task.Delay(1500);
            return name;
        }

        async Task<List<Patch>> Ramp(string name, CancellationToken ct)
        {
            Argyll.LutLinear(St.Display.ArgyllNumber.Value);
            return await Jobs.Measure(HdrPipeline.RampChart(_sdrWhite), _dir, name, name, ct);
        }

        Mhc2Profile Named(Mhc2Profile p, string tag)
        {
            p.Description = $"HDRDisplayCalibrate {St.Display.Model} {tag} {DateTime.Now:yyyy-MM-dd HH:mm}";
            return p;
        }

        // ------------------------------------------------------------ steps
        void Step1() { if (!Ready()) return; if (!ProbeGuide.Ask(ProbeGuide.Mode.Place)) return; Next(0); lock (_tests) _tests.Clear(); RunJob(async ct =>
        {
            Track(await Activate(Named(new Mhc2Profile(), "neutral"), $"HDC_{St.Display.Model}_neutral_{DateTime.Now:HHmmss}.icc"));
            _base = await Ramp("hdr_base", ct);
            var w = _base.First(p => p.Is(1, 1, 1)).Xyz;
            Show(S.T("Monitor as it is", "Монітор як є"), _base, null, S.F("White {0:F0} cd/m² at {1:F0} nits of signal ({2:F0} %), {3:F0} K", "Білий {0:F0} кд/м² на {1:F0} ніт сигналу ({2:F0} %), {3:F0} K", w.Y, _sdrWhite, w.Y / _sdrWhite * 100, Cm.Cct(w)));
            St.Report(S.T("Step 1 done", "Крок 1 виконано"), 1);
            Next(1);
        }); }

        void Step2() { if (!Ready() || _base == null) { Need(1); return; }
            if (!ProbeGuide.Ask(ProbeGuide.Mode.Check, S.T("In this step the screen turns almost completely white, and icons and text cannot be read. That is normal and lasts a few minutes.",
                                                          "На цьому кроці екран стає майже повністю білим, а значки й текст не можна прочитати. Це нормально і триває кілька хвилин."))) return;
            Next(0); RunJob(async ct =>
        {
            Track(await Activate(Named(HdrPipeline.ProbeProfile(), "probe x8"), $"HDC_{St.Display.Model}_probe_{DateTime.Now:HHmmss}.icc"));
            _probe = await Ramp("hdr_probe", ct);
            _resp = HdrPipeline.MonitorResponse(_base, _probe, _sdrWhite);
            double peak = Math.Round(_resp.ClipOut * 0.85 / 5) * 5;
            Dispatcher.Invoke(() => { _peak.Text = peak.ToString(CultureInfo.InvariantCulture); _knee.Text = Math.Round(peak * 0.62 / 5 * 5).ToString(CultureInfo.InvariantCulture); });
            var chart = new LineChart { LogX = true, LogY = true, XLabel = S.T("signal, nits", "сигнал, ніт"), YLabel = S.T("measured, cd/m²", "виміряно, кд/м²"), XMin = 0.1, YMin = 0.05 };
            chart.Data.Add(new LineChart.Series(S.T("ideal", "ідеал"), new List<(double, double)> { (0.1, 0.1), (10000, 10000) }, Ui.Muted, true));
            chart.Data.Add(new LineChart.Series(S.T("monitor", "монітор"), _resp.Req.Zip(_resp.Out).Where(t => t.First > 0).ToList(), Ui.Accent, false, true));
            ShowChart(S.F("Black {0:F3} cd/m², clips at {1:F0} nits of signal → {2:F0} cd/m². Suggested peak {3:F0} cd/m² (headroom for neutral highlights).",
                          "Чорний {0:F3} кд/м², обрізає на {1:F0} ніт сигналу → {2:F0} кд/м². Рекомендований пік {3:F0} кд/м² (запас для нейтральних світел).", _resp.Black, _resp.ClipReq, _resp.ClipOut, peak), chart);
            St.Report(S.T("Step 2 done", "Крок 2 виконано"), 1);
            Next(2);
        }); }

        void Step3() { if (!Ready() || _resp == null) { Need(2); return; } if (!ProbeGuide.Ask(ProbeGuide.Mode.Check)) return; Next(0); RunJob(async ct =>
        {
            double peak = 0, knee = 0;
            Dispatcher.Invoke(() => { peak = Ui.Num(_peak, _resp.ClipOut * 0.85); knee = Ui.Num(_knee, peak * 0.62); });
            var m = HdrPipeline.MatrixFrom60(_base, _sdrWhite);
            var curve = HdrPipeline.PqCurve(_resp, knee, peak);
            var (r, g, b) = HdrPipeline.Primaries(_base, 1.0);
            _v1 = Named(new Mhc2Profile { Matrix = m, Luts = new[] { curve, curve, curve }, MinNits = _resp.Black, MaxNits = peak, Red = r, Green = g, Blue = b }, "v1");
            Track(await Activate(_v1, $"HDC_{St.Display.Model}_v1_{DateTime.Now:HHmmss}.icc"));
            _v1m = await Ramp("hdr_v1", ct);
            Show(S.T("Profile v1", "Профіль v1"), _v1m, (knee, peak), null);
            St.Report(S.T("Step 3 done", "Крок 3 виконано"), 1);
            Next(3);
        }); }

        void Step4() { if (!Ready() || _v1m == null) { Need(3); return; } if (!ProbeGuide.Ask(ProbeGuide.Mode.Check)) return; Next(0); RunJob(async ct =>
        {
            _v2 = Named(HdrPipeline.GreyBalance(_v1, _v1m, _resp, _sdrWhite, _v1.MaxNits), "v2");
            Track(await Activate(_v2, $"HDC_{St.Display.Model}_v2_{DateTime.Now:HHmmss}.icc"));
            _v2m = await Ramp("hdr_v2", ct);
            Show(S.T("Profile v2 (grey balanced)", "Профіль v2 (баланс сірого)"), _v2m, (Ui.Num(_knee, 0), _v2.MaxNits), null);
            St.Report(S.T("Step 4 done", "Крок 4 виконано"), 1);
            Next(4);
        }); }

        void Step5() { if (!Ready() || _v2 == null) { Need(4); return; } if (!ProbeGuide.Ask(ProbeGuide.Mode.Check)) return;
            var (finalName, reportDir, reportHtml) = ProfileNameDialog.Ask($"HDRDisplayCalibrate_{St.Display.Model}_HDR_{DateTime.Now:yyyyMMdd_HHmm}");   // DMD61: the default name recalls the program
            Next(0); RunJob(async ct =>
        {
            var d = St.Display; int disp = d.ArgyllNumber.Value;
            var sdr = ColorProfiles.Default(d, false);
            Mhc2Profile final = _v2;
            if (sdr != null)
            {
                var cal = Path.Combine(_dir, "sdr_vcgt.cal");
                var (code, _) = await Argyll.RunAsync("iccvcgt", new[] { "-x", Path.Combine(ColorProfiles.ColorDir, sdr), cal }, _dir, "vcgt", null, ct);
                if (code == 0 && File.Exists(cal)) final = HdrPipeline.CompensateVcgt(_v2, Cgats.ReadCal(cal));
            }
            _final = Named(final, "final");
            _final.Description = finalName;          // the name the user sees under "Color profile"
            await Activate(_final, finalName + ".icc");
            if (sdr != null) Argyll.LutLoad(disp, Path.Combine(ColorProfiles.ColorDir, sdr));   // real-world state: SDR LUT on top of HDR
            // final verification: the grey ramp plus the colour check, in one measurement
            var ramp = HdrPipeline.RampChart(_sdrWhite);
            var colourChart = ColourChart(_sdrWhite);
            var all = await Jobs.Measure(ramp.Concat(colourChart).ToList(), _dir, "hdr_final", S.T("Verification", "Перевірка"), ct);
            var m = all.Take(ramp.Count).ToList();
            Show(S.T("Final HDR profile", "Фінальний HDR-профіль"), m, (Ui.Num(_knee, 0), _final.MaxNits),
                 S.F("Installed and active. Games and apps now see peak {0:F0} cd/m² and black {1:F3} cd/m².",
                     "Встановлено й активно. Ігри та програми тепер бачать пік {0:F0} кд/м² і чорний {1:F3} кд/м².", _final.MaxNits, _final.MinNits));
            if (all.Count == ramp.Count + colourChart.Count) ShowColours(all.Skip(ramp.Count).ToList());
            St.Report(S.T("HDR calibration done", "HDR калібрування завершено"), 1);
            double before = St.Cfg.Num(SdrKey(d), double.NaN);
            List<string> tests; lock (_tests) { tests = _tests.ToList(); _tests.Clear(); }
            Dispatcher.Invoke(() =>
            {
                // the report (values and charts) is kept as a PDF outside the work folder, which can then be emptied
                var file = Path.Combine(reportDir, finalName + "_report" + (reportHtml ? ".html" : ".pdf"));
                string saved = ReportExport.SaveAuto(file, reportHtml, S.T("HDR calibration", "HDR калібрування") + " — " + finalName, _out.Children.Cast<UIElement>().ToList()) ? file : null;
                var work = _dir; _dir = null;     // the next calibration starts a new folder
                FinishDialog.Run(St.Display ?? d, finalName, double.IsNaN(before) ? (double?)null : before, tests, saved, work);
                Prereq();
            });
        }); }

        void Track(string profile) { lock (_tests) _tests.Add(profile); }

        // ------------------------------------------------------------ colour check (DMD61)
        const double CheckNits = 100;   // colours are shown at a 100-nit white, below the roll-off, so tone mapping does not hide colour errors

        static readonly (double r, double g, double b)[] CheckColours =
        {
            // ColorChecker-like skin, sky, foliage and mid tones (sRGB)
            (0.451, 0.322, 0.267), (0.761, 0.588, 0.510), (0.384, 0.478, 0.616), (0.341, 0.424, 0.263), (0.522, 0.502, 0.694), (0.404, 0.741, 0.667),
            (0.839, 0.494, 0.173), (0.314, 0.357, 0.651), (0.757, 0.353, 0.388), (0.369, 0.235, 0.424), (0.616, 0.737, 0.251), (0.878, 0.639, 0.180),
            (0.220, 0.239, 0.588), (0.275, 0.580, 0.286), (0.686, 0.212, 0.235), (0.906, 0.780, 0.122), (0.733, 0.337, 0.584), (0.031, 0.522, 0.631),
            // saturated colours
            (1, 0, 0), (0, 1, 0), (0, 0, 1), (1, 1, 0), (0, 1, 1), (1, 0, 1),
        };

        /// <summary>The check colours scaled so that their white would be <see cref="CheckNits"/>, as SDR patch values.</summary>
        static List<(double, double, double)> ColourChart(double sdrWhite)
        {
            double k = Math.Min(CheckNits, sdrWhite) / sdrWhite;
            double Enc(double v) => Cm.SrgbOetf(Cm.SrgbEotf(v) * k);
            return CheckColours.Select(c => (Enc(c.r), Enc(c.g), Enc(c.b))).ToList();
        }

        /// <summary>ΔE2000 of each check colour against what an exact display would show (Windows draws SDR content at the SDR white level).</summary>
        void ShowColours(List<Patch> cm)
        {
            double refNits = Math.Min(CheckNits, _sdrWhite), k = refNits / _sdrWhite;
            var white = Cm.D65 * refNits;
            var res = cm.Select(p =>
            {
                var lin = new V3(Cm.SrgbEotf(p.R), Cm.SrgbEotf(p.G), Cm.SrgbEotf(p.B));
                var target = Cm.SrgbToXyz * lin * _sdrWhite;
                double de = Cm.DeltaE2000(Cm.Lab(target, white), Cm.Lab(p.Xyz, white));
                byte B(double v) => (byte)Math.Round(Cm.SrgbOetf(v / k) * 255);   // the colour as it looks at full level
                return (c: Color.FromRgb(B(lin.X), B(lin.Y), B(lin.Z)), target, meas: p.Xyz, de);
            }).ToList();
            double avg = res.Average(r => r.de), max = res.Max(r => r.de);
            var bars = new DeltaEChart();
            foreach (var r in res) bars.Bars.Add((r.c, r.de));
            var rows = res.Select(r => new[] { $"#{r.c.R:X2}{r.c.G:X2}{r.c.B:X2}", Ui.F(r.target.Y, 1), Ui.F(r.meas.Y, 1), Ui.F(r.de, 2) });
            var sum = new TextBlock { Text = S.F("ΔE2000 average {0:F2} · maximum {1:F2}", "ΔE2000 середнє {0:F2} · максимум {1:F2}", avg, max), FontSize = 20, FontWeight = FontWeights.SemiBold,
                                      Foreground = avg < 1 ? Ui.Good : avg < 3 ? Ui.Warm : Ui.Bad, Margin = new Thickness(0, 4, 0, 6) };
            Dispatcher.Invoke(() => _out.Children.Insert(Math.Min(1, _out.Children.Count), Ui.Card(
                Ui.H2(S.T("Colour check", "Перевірка кольорів")),
                Ui.Caption(S.F("24 colours (skin, sky, foliage, saturated colours) shown at a {0:F0} cd/m² white and compared with the exact values. Below 1 the difference cannot be seen, below 3 it is hard to see.",
                               "24 кольори (шкіра, небо, зелень, насичені кольори) показано при білому {0:F0} кд/м² і порівняно з точними значеннями. Менше 1 — різниці не видно, менше 3 — її важко помітити.", refNits)),
                sum, bars,
                Ui.Table(new[] { S.T("Colour", "Колір"), S.T("target, cd/m²", "ціль, кд/м²"), S.T("measured, cd/m²", "виміряно, кд/м²"), "ΔE2000" }, rows))));
        }

        void Export() => ReportExport.Export(S.T("HDR calibration", "HDR калібрування"),
            $"HDR_report_{St.Display?.Model}_{DateTime.Now:yyyyMMdd_HHmm}", _out.Children.Cast<UIElement>().ToList());

        static void Need(int step) => MessageBox.Show(S.F("Run step {0} first.", "Спершу виконайте крок {0}.", step));

        // ------------------------------------------------------------ results
        void ShowChart(string text, UIElement chart) => Dispatcher.Invoke(() => _out.Children.Insert(0, Ui.Card(Ui.Caption(text), chart)));

        /// <summary>PQ tracking (measured vs target) and white-point error per grey level.</summary>
        void Show(string title, List<Patch> m, (double knee, double peak)? tgt, string note)
        {
            var k = m.First(p => p.Is(0, 0, 0)).Xyz;
            var greys = m.Where(p => p.IsGrey).OrderBy(p => p.R).Select(p => (req: _sdrWhite * Cm.SrgbEotf(p.R), xyz: p.Xyz)).Where(t => t.req > 0.05).ToList();
            var track = new LineChart { LogX = true, LogY = true, XLabel = S.T("signal, nits", "сигнал, ніт"), YLabel = "cd/m²", XMin = 0.1, YMin = 0.05 };
            track.Data.Add(new LineChart.Series(S.T("ideal PQ", "ідеальний PQ"), greys.Select(t => (t.req, tgt is { } q ? k.Y + HdrPipeline.Target(t.req, q.knee, q.peak) * (1 - k.Y / q.peak) : t.req)).ToList(), Ui.Muted, true));
            track.Data.Add(new LineChart.Series(S.T("measured", "виміряно"), greys.Select(t => (t.req, t.xyz.Y)).ToList(), Ui.Accent, false, true));
            var wde = new LineChart { LogX = true, XLabel = S.T("signal, nits", "сигнал, ніт"), YLabel = S.T("white ΔE2000 vs D65", "ΔE2000 білого від D65"), XMin = 0.5, YMin = 0, Height = 220 };
            wde.Data.Add(new LineChart.Series("ΔE", greys.Where(t => t.xyz.Y > 0.5).Select(t => (t.req, Cm.DeltaE2000(Cm.Lab(Cm.D65, Cm.D65), Cm.Lab(t.xyz / t.xyz.Y, Cm.D65)))).ToList(), Ui.Warm, false, true));
            var rows = greys.Where((_, i) => i % 3 == 0).Select(t =>
            {
                var (x, y) = t.xyz.Xy;
                double want = tgt is { } q ? k.Y + HdrPipeline.Target(t.req, q.knee, q.peak) * (1 - k.Y / q.peak) : t.req;
                return new[] { Ui.F(t.req, 2), Ui.F(t.xyz.Y, 2), Ui.F(t.xyz.Y / want * 100, 0) + " %", Ui.F(x, 4), Ui.F(y, 4), Ui.F(Cm.DeltaE2000(Cm.Lab(Cm.D65, Cm.D65), Cm.Lab(t.xyz / t.xyz.Y, Cm.D65)), 1) };
            });
            var prim = new[] { (0.6, 0.0, 0.0), (0.0, 0.6, 0.0), (0.0, 0.0, 0.6) }.Select(c => m.First(p => p.Is(c.Item1, c.Item2, c.Item3)).Xyz.Xy).ToArray();
            var xy = new ChromaticityChart { Height = 340 };
            xy.Triangles.Add(new ChromaticityChart.Tri("sRGB / Rec.709", (0.64, 0.33), (0.30, 0.60), (0.15, 0.06), Ui.Muted, false, true));
            xy.Triangles.Add(new ChromaticityChart.Tri(S.T("measured 60 %", "виміряно 60 %"), prim[0], prim[1], prim[2], Ui.Accent, true));
            xy.Points.Add(((0.3127, 0.3290), Brushes.White, "D65"));
            Dispatcher.Invoke(() => _out.Children.Insert(0, Ui.Card(Ui.H2(title), note == null ? null : Ui.Caption(note),
                Ui.Caption(S.T("PQ tracking", "Відстеження PQ")), track, Ui.Caption(S.T("White point error per level", "Похибка білого по рівнях")), wde,
                Ui.Table(new[] { S.T("signal, nits", "сигнал, ніт"), "cd/m²", S.T("of target", "від цілі"), "x", "y", "ΔE" }, rows), xy)));
        }
    }
}
