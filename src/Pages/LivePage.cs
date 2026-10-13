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
            _start = Ui.Btn(S.T("Start", "Старт"), Toggle, true, "");
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Target", "Ціль")),
                Ui.Field(S.T("White point", "Точка білого"), _wp.View),
                Ui.Field(S.T("White luminance, cd/m²", "Яскравість білого, кд/м²"), _nits, S.T("Empty = don't care", "Порожньо = не важливо")),
                Ui.Field(S.T("Grey level, %", "Рівень сірого, %"), _level, S.T("100% → gain / contrast controls; 20–30% → offset / bias (TV 2-point white balance)", "100% → gain / contrast; 20–30% → offset / bias (2-точковий баланс білого ТВ)")),
                Ui.Field(S.T("Reference channel", "Опорний канал"), _ref)));
            Body.Children.Add(Ui.Card(Ui.Row(Ui.Btn(S.T("Measure R/G/B", "Виміряти R/G/B"), MeasurePrimaries, false, ""), _start,
                                             Ui.Btn(S.T("Auto OSD (DDC/CI)", "Авто-OSD (DDC/CI)"), AutoOsd, false, ""),
                                             Ui.Btn(S.T("Room light", "Освітлення кімнати"), Room, false, "")),
                _room, _big, _sub, _bars,
                Ui.Caption(S.T("While the meter runs, the display's calibration is switched off (linear video LUT). It is restored when you stop.",
                               "Поки йде замір, калібрування дисплея вимкнене (лінійний LUT). Після зупинки воно повертається."))));
        }

        void MeasurePrimaries() => RunJob(async ct =>
        {
            var dir = Paths.Session("live");
            if (St.Display?.ArgyllNumber is int n) Argyll.LutLinear(n);
            var p = await Jobs.Measure(new[] { (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (1.0, 1.0, 1.0) }, dir, "prim", S.T("Primaries", "Основні кольори"), ct);
            _prim = M3.FromColumns(p[0].Xyz, p[1].Xyz, p[2].Xyz);
            _sub.Text = string.Join("   ", new[] { "R", "G", "B" }.Select((c, i) => { var (x, y) = p[i].Xyz.Xy; return $"{c} {x:F4} {y:F4}"; })) +
                        $"   · white {p[3].Xyz.Y:F1} cd/m², {Cm.Cct(p[3].Xyz):F0} K";
            if (St.Display?.ArgyllNumber is int m) Argyll.LoadInstalled(m);
            St.Report(S.T("Primaries measured", "Основні кольори виміряно"), 1);
        });

        void Toggle()
        {
            if (_running) { _running = false; return; }
            _running = true; ((TextBlock)((StackPanel)_start.Content).Children[1]).Text = S.T("Stop", "Стоп");
            RunJob(async ct =>
            {
                var dir = Paths.Session("live");
                if (St.Display?.ArgyllNumber is int n) Argyll.LutLinear(n);
                Jobs.Hold(true);
                try
                {
                    while (_running && !ct.IsCancellationRequested)
                    {
                        double lev = double.Parse((string)_level.SelectedItem, CultureInfo.InvariantCulture) / 100;
                        var xyz = (await Jobs.Measure(new[] { (lev, lev, lev) }, dir, "live", S.T("Live", "Живий"), ct))[0].Xyz;
                        Show(xyz, lev);
                    }
                }
                finally
                {
                    Jobs.Hold(false); _running = false;
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
                lines.Add(S.T($"ΔE to target {de:F2}", $"ΔE до цілі {de:F2}") + (de < 1 ? "  ✓" : ""));
                if (_prim is M3 P && de >= 0.6)
                {
                    var now = P.Inverse() * xyz; var want = P.Inverse() * t;
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
                lines.Add(Math.Abs(d) < 3 ? S.T("Luminance OK ✓", "Яскравість OK ✓") : S.T($"Luminance: {(d > 0 ? "lower" : "raise")} ({d:+0;-0}%)", $"Яскравість: {(d > 0 ? "зменшити" : "збільшити")} ({d:+0;-0}%)"));
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
            _room.Text = S.T($"Room: {lux:F1} lx, {cct:F0} K → suggested white {sug:F0} cd/m²", $"Кімната: {lux:F1} лк, {cct:F0} K → рекомендований білий {sug:F0} кд/м²");
            St.Cfg.Set("room_lux", lux); St.Cfg.Set("room_cct", cct); St.Cfg.Save();
            St.Report(_room.Text, 1);
        });
    }
}
