// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HDRDisplayCalibrate.Core;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Pages
{
    public sealed class VerifyPage : Page
    {
        readonly WhitePicker _wp = new();
        readonly ComboBox _curve = Ui.Combo(new[] { "2.2", "2.4", "BT.1886", "sRGB", "L*" });
        readonly ComboBox _size = Ui.Combo(new[] { S.T("Quick (37 patches)", "Швидко (37 патчів)"), S.T("Standard (63 patches)", "Стандарт (63 патчі)"), S.T("Extended (63 + 160 random)", "Розширено (63 + 160 випадкових)") }, 1);
        readonly CheckBox _raw = Ui.Check(S.T("Measure the bare display (switch the calibration LUT off)", "Міряти «голий» дисплей (вимкнути калібрувальний LUT)"), tip: S.T("Switches the calibration curve off while measuring, to see how the panel performs without it.", "Вимикає калібрувальну криву на час заміру, щоб побачити, який вигляд має панель без неї."));
        readonly StackPanel _result = new();
        static readonly string[] CurveIds = { "2.2", "2.4", "bt1886", "srgb", "lstar" };

        public VerifyPage() : base(S.T("Verify & report", "Перевірка і звіт"),
            S.T("Measures how close the display is to the target: white, black, contrast, gamma, ΔE2000 of greys and colours.", "Міряє, наскільки дисплей близький до цілі: білий, чорний, контраст, гамма, ΔE2000 сірих і кольорів."))
        {
            Body.Children.Add(Ui.Card(
                Ui.Field(S.T("Target white", "Цільовий білий"), _wp.View, tip: S.T("The white point the results are compared with.", "Точка білого, з якою порівнюються результати.")),
                Ui.Field(S.T("Target tone curve", "Цільова крива"), _curve, tip: S.T("The tone curve the results are compared with.", "Тонова крива, з якою порівнюються результати.")),
                Ui.Field(S.T("Patch set", "Набір патчів"), _size, tip: S.T("How many colours to measure: a quick check, the standard report, or an extended set with random colours.", "Скільки кольорів міряти: швидка перевірка, стандартний звіт або розширений набір із випадковими кольорами.")), _raw,
                Ui.Row(Ui.Btn(S.T("Measure", "Заміряти"), Run, true, "", tip: S.T("Measures the patches on the screen and builds the report. Esc cancels.", "Міряє патчі на екрані й будує звіт. Esc скасовує.")), Ui.Btn(S.T("Open .ti3…", "Відкрити .ti3…"), Open, false, "", tip: S.T("Builds a report from an existing ArgyllCMS measurement file (.ti3).", "Будує звіт з наявного файлу замірів ArgyllCMS (.ti3).")))));
            Body.Children.Add(_result);
        }

        void Run()
        {
            var d = St.Display; if (d?.ArgyllNumber is not int disp) return;
            int si = _size.SelectedIndex;
            RunJob(async ct =>
            {
                var dir = Paths.Session("verify_" + d.Model);
                var chart = si == 0 ? Jobs.VerifyChart(11).Take(11 + 24).Append((1.0, 1.0, 1.0)).Append((0.0, 0.0, 0.0)).ToList() : Jobs.VerifyChart();
                if (si == 2) { var rnd = new Random(7); for (int i = 0; i < 160; i++) chart.Add((rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble())); }
                bool raw = Dispatcher.Invoke(() => _raw.IsChecked == true);
                if (raw) Argyll.LutLinear(disp);
                try
                {
                    var m = await Jobs.Measure(chart, dir, "verify", S.T("Verification", "Перевірка"), ct);
                    Dispatcher.Invoke(() => Show(m, dir));
                }
                finally { if (raw) Argyll.LoadInstalled(disp); }
                St.Report(S.T("Verification done", "Перевірку завершено"), 1);
            });
        }

        void Open()
        {
            var dlg = new OpenFileDialog { Filter = "ArgyllCMS measurements (*.ti3)|*.ti3", InitialDirectory = Paths.Docs };
            if (dlg.ShowDialog() == true) Show(Cgats.ReadTi3(dlg.FileName, out _), Path.GetDirectoryName(dlg.FileName));
        }

        void Show(List<Patch> m, string dir)
        {
            string curve = CurveIds[Math.Max(0, _curve.SelectedIndex)];
            if (!m.Any(p => p.Is(1, 1, 1))) { MessageBox.Show(S.T("The measurement has no white patch.", "У замірі немає білого патча.")); return; }
            var wp = _wp.Xy() ?? m.First(p => p.Is(1, 1, 1)).Xyz.Xy;
            var r = Report.Build(m, curve, wp);
            _result.Children.Clear();
            _result.Children.Add(Ui.Card(ReportView.Build(r, curve, wp, S.T("Report", "Звіт")), Ui.Caption(dir)));
        }
    }
}
