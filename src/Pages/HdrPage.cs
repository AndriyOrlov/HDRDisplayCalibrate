// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
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
        string _dir;
        double _sdrWhite;
        List<Patch> _base, _probe, _v1m, _v2m;
        HdrPipeline.Response _resp;
        Mhc2Profile _v1, _v2, _final;

        public HdrPage() : base(S.T("HDR calibration", "HDR калібрування"),
            S.T("Builds a native Windows HDR (MHC2) profile from colorimeter readings: correct PQ tracking up to your monitor's real peak, D65 white at every level, accurate colours, and the real peak/black reported to games. No background app and no HDR test patterns needed.",
                "Будує рідний HDR-профіль Windows (MHC2) за замірами колориметра: правильне відстеження PQ до реального піку монітора, білий D65 на всіх рівнях, точні кольори і реальний пік/чорний для ігор. Без програм у фоні і без HDR-патернів."))
        {
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Before you start", "Перед початком")), _pre));
            Body.Children.Add(_steps);
            Body.Children.Add(_out);
            BuildSteps();
        }

        public override void OnShow() => Prereq();

        void Prereq()
        {
            _pre.Children.Clear();
            var d = St.Display; if (d == null) return;
            Displays.RefreshColor(d);
            bool ok(bool v) => v;
            _pre.Children.Add(Ui.Table(new[] { "", "", "" }, new[]
            {
                new[] { ok(d.HdrOn) ? "✓" : "✗", S.T("Windows HDR is on for this display", "Windows HDR увімкнено для цього дисплея"), d.HdrOn ? "" : S.T("required", "обов'язково") },
                new[] { d.SdrWhiteNits >= 400 ? "✓" : "!", S.F("“SDR content brightness” = {0:F0} nits", "«Яскравість SDR-вмісту» = {0:F0} ніт", d.SdrWhiteNits), S.T("set the slider to 100 % while calibrating (measures up to 480 nits of signal); set it back afterwards", "на час калібрування поставте повзунок на 100 % (міряє до 480 ніт сигналу); потім поверніть") },
                new[] { "!", S.T("Monitor OSD brightness in HDR at maximum", "Яскравість OSD монітора в HDR на максимумі"), S.T("so the monitor follows the PQ signal as closely as it can", "щоб монітор якомога точніше відстежував PQ-сигнал") },
                new[] { "!", S.T("Sensor in the centre of the screen, diffuser open", "Датчик по центру екрана, розсіювач відкритий"), "" },
            }, new double[] { 30, 330, 520 }));
            _pre.Children.Add(Ui.Row(
                Ui.Btn(d.HdrOn ? S.T("HDR is on", "HDR увімкнено") : S.T("Turn HDR on", "Увімкнути HDR"), async () => { if (!d.HdrOn) { Displays.SetHdr(d, true); await Task.Delay(7000); St.RefreshDisplays(); Prereq(); } }, false, "", tip: S.T("Turns Windows HDR on for this display (takes a few seconds).", "Вмикає Windows HDR для цього дисплея (займає кілька секунд).")),
                Ui.Btn(S.T("Open display settings", "Відкрити параметри дисплея"), ColorProfiles.OpenDisplaySettings, false, "", tip: S.T("Opens Windows Settings → Display, where the HDR colour profile and “SDR content brightness” are set.", "Відкриває Параметри Windows → Дисплей, де вибирається HDR-профіль і «Яскравість SDR-вмісту».")),
                Ui.Btn(S.T("Set OSD brightness 100 (DDC/CI)", "OSD яскравість 100 (DDC/CI)"), () => { using var x = Ddc.Open(d); if (x?.Get(Ddc.Brightness) is { } b) x.Set(Ddc.Brightness, b.max); }, false, "", tip: S.T("Sets the monitor's own brightness to maximum over DDC/CI so it follows the PQ signal as closely as it can.", "Ставить власну яскравість монітора на максимум через DDC/CI, щоб він якомога точніше відстежував PQ-сигнал.")),
                Ui.Btn(S.T("Refresh", "Оновити"), Prereq, false, "", tip: S.T("Checks the HDR prerequisites again.", "Ще раз перевіряє умови для HDR."))));
        }

        void BuildSteps()
        {
            _steps.Children.Clear();
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
                S.T("Compensates for the SDR calibration curve Windows also applies in HDR, installs the final profile and verifies it.", "Компенсує SDR-криву, яку Windows застосовує і в HDR, встановлює фінальний профіль і перевіряє."), Step5));
        }

        static UIElement Step(int n, string title, string text, Action run, UIElement extra = null) =>
            Ui.Card(Ui.Row(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Accent, Child = new TextBlock { Text = n.ToString(), Foreground = Brushes.Black, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                           Ui.H2(title)), Ui.Caption(text), extra, Ui.Row(Ui.Btn(S.T("Run", "Виконати"), run, true, "", tip: text)));

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
        async Task Activate(Mhc2Profile p, string file)
        {
            var path = Path.Combine(_dir, file);
            p.Save(path);
            ColorProfiles.Install(St.Display, path, true);
            await Dispatcher.InvokeAsync(() =>
            {
                ColorProfiles.OpenDisplaySettings();
                MessageBox.Show(S.F("In Settings → System → Display → (this monitor) → Color profile, choose:\n\n    {0}\n\nthen move the Settings window off the patch area and press OK.",
                                    "У Параметри → Система → Дисплей → (цей монітор) → Колірний профіль виберіть:\n\n    {0}\n\nвідсуньте вікно Параметрів від патча і натисніть OK.", p.Description), "HDRDisplayCalibrate");
            });
            await Task.Delay(1500);
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
        void Step1() { if (!Ready()) return; RunJob(async ct =>
        {
            await Activate(Named(new Mhc2Profile(), "neutral"), $"HDC_{St.Display.Model}_neutral_{DateTime.Now:HHmmss}.icc");
            _base = await Ramp("hdr_base", ct);
            var w = _base.First(p => p.Is(1, 1, 1)).Xyz;
            Show(S.T("Monitor as it is", "Монітор як є"), _base, null, S.F("White {0:F0} cd/m² at {1:F0} nits of signal ({2:F0} %), {3:F0} K", "Білий {0:F0} кд/м² на {1:F0} ніт сигналу ({2:F0} %), {3:F0} K", w.Y, _sdrWhite, w.Y / _sdrWhite * 100, Cm.Cct(w)));
            St.Report(S.T("Step 1 done", "Крок 1 виконано"), 1);
        }); }

        void Step2() { if (!Ready() || _base == null) { Need(1); return; } RunJob(async ct =>
        {
            await Activate(Named(HdrPipeline.ProbeProfile(), "probe x8"), $"HDC_{St.Display.Model}_probe_{DateTime.Now:HHmmss}.icc");
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
        }); }

        void Step3() { if (!Ready() || _resp == null) { Need(2); return; } RunJob(async ct =>
        {
            double peak = 0, knee = 0;
            Dispatcher.Invoke(() => { peak = Ui.Num(_peak, _resp.ClipOut * 0.85); knee = Ui.Num(_knee, peak * 0.62); });
            var m = HdrPipeline.MatrixFrom60(_base, _sdrWhite);
            var curve = HdrPipeline.PqCurve(_resp, knee, peak);
            var (r, g, b) = HdrPipeline.Primaries(_base, 1.0);
            _v1 = Named(new Mhc2Profile { Matrix = m, Luts = new[] { curve, curve, curve }, MinNits = _resp.Black, MaxNits = peak, Red = r, Green = g, Blue = b }, "v1");
            await Activate(_v1, $"HDC_{St.Display.Model}_v1_{DateTime.Now:HHmmss}.icc");
            _v1m = await Ramp("hdr_v1", ct);
            Show(S.T("Profile v1", "Профіль v1"), _v1m, (knee, peak), null);
            St.Report(S.T("Step 3 done", "Крок 3 виконано"), 1);
        }); }

        void Step4() { if (!Ready() || _v1m == null) { Need(3); return; } RunJob(async ct =>
        {
            _v2 = Named(HdrPipeline.GreyBalance(_v1, _v1m, _resp, _sdrWhite, _v1.MaxNits), "v2");
            await Activate(_v2, $"HDC_{St.Display.Model}_v2_{DateTime.Now:HHmmss}.icc");
            _v2m = await Ramp("hdr_v2", ct);
            Show(S.T("Profile v2 (grey balanced)", "Профіль v2 (баланс сірого)"), _v2m, (Ui.Num(_knee, 0), _v2.MaxNits), null);
            St.Report(S.T("Step 4 done", "Крок 4 виконано"), 1);
        }); }

        void Step5() { if (!Ready() || _v2 == null) { Need(4); return; } RunJob(async ct =>
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
            await Activate(_final, $"HDC_{d.Model}_HDR_{DateTime.Now:yyyyMMdd_HHmm}.icc");
            if (sdr != null) Argyll.LutLoad(disp, Path.Combine(ColorProfiles.ColorDir, sdr));   // real-world state: SDR LUT on top of HDR
            var m = await Jobs.Measure(HdrPipeline.RampChart(_sdrWhite), _dir, "hdr_final", S.T("Verification", "Перевірка"), ct);
            Show(S.T("Final HDR profile", "Фінальний HDR-профіль"), m, (Ui.Num(_knee, 0), _final.MaxNits),
                 S.F("Installed and active. Games and apps now see peak {0:F0} cd/m² and black {1:F3} cd/m². You can set “SDR content brightness” back (e.g. ~10 for 120 cd/m² desktop).",
                     "Встановлено й активно. Ігри та програми тепер бачать пік {0:F0} кд/м² і чорний {1:F3} кд/м². «Яскравість SDR-вмісту» можна повернути (напр. ~10 для робочого столу 120 кд/м²).", _final.MaxNits, _final.MinNits));
            St.Report(S.T("HDR calibration done", "HDR калібрування завершено"), 1);
        }); }

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
