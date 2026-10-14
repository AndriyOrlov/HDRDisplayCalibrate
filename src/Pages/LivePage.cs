// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>Continuous white/grey reading for adjusting the monitor or TV menu, plus DDC/CI auto adjustment.</summary>
    public sealed class LivePage : Page
    {
        readonly WhitePicker _wp = new();
        readonly TextBox _nits = Ui.Box("120");
        readonly ComboBox _level = Ui.Combo(new[] { "100", "90", "80", "75", "70", "60", "50", "40", "30", "20", "10", "5" }, 0, null, 120);
        readonly ComboBox _ref;
        readonly TextBlock _big = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Text = "—" };
        readonly TextBlock _sub = new() { FontSize = 15, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        readonly StackPanel _bars = new() { Margin = new Thickness(0, 10, 0, 0) };
        readonly TextBlock _room = Ui.Caption("");
        M3? _prim;
        bool _running;
        Button _start;

        public LivePage() : base(S.T("Live meter", "Живий замір"),
            S.T("Adjust the monitor's own menu (RGB gain/offset, brightness) while watching the readings, or let DDC/CI do it automatically.",
                "Налаштовуйте меню самого монітора (RGB, яскравість), дивлячись на цифри, або дайте DDC/CI зробити це автоматично."))
        {
            _ref = Ui.Combo(new[] { S.T("auto (only reduce)", "авто (лише зменшувати)"), S.T("keep R", "R не чіпати"), S.T("keep G", "G не чіпати"), S.T("keep B", "B не чіпати") }, 0, null, 200);
            _start = Ui.Btn(S.T("Start", "Старт"), Toggle, true, "", tip: S.T("Starts continuous readings of a grey patch. Press again, or Esc, to stop.", "Починає безперервні заміри сірого патча. Натисніть ще раз або Esc, щоб зупинити."));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Target", "Ціль")),
                Ui.Field(S.T("White point", "Точка білого"), _wp.View, tip: S.T("The white you are aiming for. D65 is the standard for sRGB, Rec.709 and Rec.2020.", "Білий, до якого прагнете. D65 — стандарт для sRGB, Rec.709 і Rec.2020.")),
                Ui.Field(S.T("White luminance, cd/m²", "Яскравість білого, кд/м²"), _nits, S.T("Empty = don't care", "Порожньо = не важливо"), tip: S.T("Target brightness of white: about 80–120 for a dim room, 120–160 for an office. Leave empty to ignore luminance.", "Цільова яскравість білого: близько 80–120 для темної кімнати, 120–160 для офісу. Порожньо — яскравість не враховується.")),
                Ui.Field(S.T("Grey level, %", "Рівень сірого, %"), _level, S.T("100% → gain / contrast controls; 20–30% → offset / bias (TV 2-point white balance)", "100% → gain / contrast; 20–30% → offset / bias (2-точковий баланс білого ТВ)"), tip: S.T("Which grey is shown. 100 % for the RGB gain / contrast controls, 20–30 % for offset / bias (two-point white balance on TVs).", "Який сірий показується. 100 % — для RGB gain / контрасту, 20–30 % — для offset / bias (двоточковий баланс білого на ТВ).")),
                Ui.Field(S.T("Reference channel", "Опорний канал"), _ref, tip: S.T("The channel left as it is while the hints adjust the other two. “Auto” only ever lowers channels, so none of them clips.", "Канал, який лишається як є, поки підказки підганяють два інші. «Авто» лише зменшує канали, тож жоден не обрізається."))));
            Body.Children.Add(Ui.Card(Ui.Row(Ui.Btn(S.T("Measure R/G/B", "Виміряти R/G/B"), MeasurePrimaries, false, "", tip: S.T("Measures pure red, green and blue once. Needed for the per-channel hints.", "Один раз міряє чистий червоний, зелений і синій. Потрібно для підказок по каналах.")), _start,
                                             Ui.Btn(S.T("Auto OSD (DDC/CI)", "Авто-OSD (DDC/CI)"), AutoOsd, false, "", tip: S.T("The program sets the monitor's own RGB gains and brightness over DDC/CI toward the target white and luminance (about 15 s).", "Програма сама виставляє RGB і яскравість у меню монітора через DDC/CI до цільового білого та яскравості (близько 15 с).")),
                                             Ui.Btn(S.T("Room light", "Освітлення кімнати"), Room, false, "", tip: S.T("Measures the room light with the diffuser over the sensor and suggests a white luminance for it.", "Міряє освітлення кімнати (розсіювач на датчику) і радить відповідну яскравість білого."))),
                _room, _big, _sub, _bars,
                Ui.Caption(S.T("While the meter runs, the display's calibration is switched off (linear video LUT). It is restored when you stop.",
                               "Поки йде замір, калібрування дисплея вимкнене (лінійний LUT). Після зупинки воно повертається."))));
        }

        void MeasurePrimaries() => RunJob(async ct =>
        {
            if (St.Display?.ArgyllNumber is int n) Argyll.LutLinear(n);
            try
            {
                using var rd = new Jobs.LiveReader(fast: true);
                var p = new[] { await rd.Read(1, 0, 0, ct), await rd.Read(0, 1, 0, ct), await rd.Read(0, 0, 1, ct), await rd.Read(1, 1, 1, ct) };
                if (p.Take(3).Any(v => v.Y <= 0.05)) throw new Exception(S.T("A primary read as black: is the sensor on this monitor?", "Один з кольорів виміряно як чорний: датчик точно на цьому моніторі?"));
                _prim = M3.FromColumns(p[0], p[1], p[2]);
                _sub.Text = string.Join("   ", new[] { "R", "G", "B" }.Select((c, i) => { var (x, y) = p[i].Xy; return $"{c} {x:F4} {y:F4}"; })) +
                            $"   · white {p[3].Y:F1} cd/m², {Cm.Cct(p[3]):F0} K";
            }
            finally { if (St.Display?.ArgyllNumber is int m) Argyll.LoadInstalled(m); }
            St.Report(S.T("Primaries measured", "Основні кольори виміряно"), 1);
        });

        void Toggle()
        {
            if (_running) { _running = false; return; }
            _running = true; ((TextBlock)((StackPanel)_start.Content).Children[1]).Text = S.T("Stop", "Стоп");
            RunJob(async ct =>
            {
                if (St.Display?.ArgyllNumber is int n) Argyll.LutLinear(n);
                Jobs.LiveReader rd = null;
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew(); int count = 0;
                    while (_running && !ct.IsCancellationRequested)
                    {
                        double lev = double.Parse((string)_level.SelectedItem, CultureInfo.InvariantCulture) / 100;
                        // quick fixed-time readings for bright levels, the slower adaptive mode for dark greys
                        bool fast = lev >= 0.4;
                        if (rd == null || rd.Fast != fast) { rd?.Dispose(); rd = new Jobs.LiveReader(fast); sw.Restart(); count = 0; }
                        var xyz = await rd.Read(lev, lev, lev, ct, 0);
                        count++;
                        Show(xyz, lev);
                        St.Report(S.F("Live: {0} readings, {1:F1} s each", "Живий замір: {0} замірів, {1:F1} с на замір", count, sw.Elapsed.TotalSeconds / count));
                    }
                }
                finally
                {
                    rd?.Dispose();
                    _running = false;
                    ((TextBlock)((StackPanel)_start.Content).Children[1]).Text = S.T("Start", "Старт");
                    if (St.Display?.ArgyllNumber is int m) Argyll.LoadInstalled(m);
                }
            });
        }

        void Show(V3 xyz, double lev)
        {
            var (x, y) = xyz.Xy;
            _big.Text = $"{xyz.Y:F2} cd/m²   {Cm.Cct(xyz):F0} K   x {x:F4}  y {y:F4}";
            _bars.Children.Clear();
            var tw = _wp.Xy();
            var lines = new System.Collections.Generic.List<string>();
            if (tw is (double tx, double ty))
            {
                var t = Cm.XyToXyz(tx, ty, xyz.Y);
                double de = Cm.DeltaE2000(Cm.Lab(t, t), Cm.Lab(xyz, t));
                lines.Add(S.F("ΔE to target {0:F2}", "ΔE до цілі {0:F2}", de) + (de < 1 ? "  ✓" : ""));
                M3? inv = null;
                try { if (_prim is M3 P0) inv = P0.Inverse(); } catch (InvalidOperationException) { _prim = null; }
                if (inv is M3 Pi && de >= 0.6)
                {
                    var now = Pi * xyz; var want = Pi * t;
                    var r = new V3(want.X / now.X, want.Y / now.Y, want.Z / now.Z);
                    r = _ref.SelectedIndex == 0 ? r / r.Max : r / r[_ref.SelectedIndex - 1];
                    for (int c = 0; c < 3; c++) _bars.Children.Add(Bar("RGB"[c].ToString(), r[c], new[] { Brushes.IndianRed, Brushes.LimeGreen, Brushes.DodgerBlue }[c]));
                }
                else if (_prim == null) lines.Add(S.T("Press “Measure R/G/B” for channel hints.", "Натисніть «Виміряти R/G/B» для підказок по каналах."));
            }
            double n = Ui.Num(_nits, double.NaN);
            if (!double.IsNaN(n) && lev > 0.99)
            {
                double d = (xyz.Y - n) / n * 100;
                lines.Add(Math.Abs(d) < 3 ? S.T("Luminance OK ✓", "Яскравість OK ✓") : d > 0 ? S.F("Luminance: lower ({0:+0;-0}%)", "Яскравість: зменшити ({0:+0;-0}%)", d) : S.F("Luminance: raise ({0:+0;-0}%)", "Яскравість: збільшити ({0:+0;-0}%)", d));
            }
            _sub.Text = string.Join("\n", lines);
        }

        static UIElement Bar(string ch, double ratio, Brush b)
        {
            double pct = (ratio - 1) * 100;
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var track = new Border { Height = 10, Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), CornerRadius = new CornerRadius(5) };
            var fill = new Border { Height = 10, Background = b, CornerRadius = new CornerRadius(5), HorizontalAlignment = pct < 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                                    Width = Math.Min(200, Math.Abs(pct) * 4), Margin = pct < 0 ? new Thickness(0, 0, 200, 0) : new Thickness(200, 0, 0, 0) };
            var inner = new Grid(); inner.Children.Add(track); inner.Children.Add(fill);
            inner.Children.Add(new Border { Width = 2, Height = 16, Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            var label = new TextBlock { Text = Math.Abs(pct) < 0.5 ? "OK" : $"{(pct > 0 ? "↑" : "↓")} {Math.Abs(pct):F1}%", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(inner, 1); Grid.SetColumn(label, 2);
            g.Children.Add(new TextBlock { Text = ch, FontWeight = FontWeights.SemiBold }); g.Children.Add(inner); g.Children.Add(label);
            return g;
        }

        void AutoOsd() => RunJob(async ct =>
        {
            double n = Ui.Num(_nits, double.NaN);
            var res = await Jobs.AutoOsd(St.Display, _wp.Xy(), double.IsNaN(n) ? null : n, Paths.Session("auto_osd"), ct);
            _sub.Text = S.T("Auto OSD done: ", "Авто-OSD готово: ") + res;
            if (St.Display?.ArgyllNumber is int m) Argyll.LoadInstalled(m);
            St.Report(S.T("Auto OSD done", "Авто-OSD готово"), 1);
        });

        void Room() => RunJob(async ct =>
        {
            St.Report(S.T("Measuring room light (diffuser over the lens)…", "Міряю освітлення кімнати (розсіювач на лінзі)…"));
            var (lux, cct) = await Jobs.Ambient(ct);
            double sug = lux <= 2 ? 100 : lux >= 1000 ? 220 : Cm.Interp(Math.Log10(lux), new[] { Math.Log10(2), 1, Math.Log10(50), Math.Log10(150), Math.Log10(400), 3 }, new[] { 100.0, 120, 140, 160, 185, 220 });
            _room.Text = S.F("Room: {0:F1} lx, {1:F0} K → suggested white {2:F0} cd/m²", "Кімната: {0:F1} лк, {1:F0} K → рекомендований білий {2:F0} кд/м²", lux, cct, sug);
            St.Cfg.Set("room_lux", lux); St.Cfg.Set("room_cct", cct); St.Cfg.Save();
            St.Report(_room.Text, 1);
        });
    }
}
