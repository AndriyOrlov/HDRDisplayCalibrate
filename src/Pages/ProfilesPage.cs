// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HDRDisplayCalibrate.Core;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Pages
{
    public sealed class ProfilesPage : Page
    {
        readonly StackPanel _list = new();

        public ProfilesPage() : base(S.T("Profiles", "Профілі"),
            S.T("SDR (ICC) and HDR (advanced colour / MHC2) profiles associated with the selected display. HDR profiles take effect only after you pick them in Settings → Display → Color profile.",
                "SDR (ICC) і HDR (MHC2) профілі вибраного дисплея. HDR-профіль починає діяти лише після вибору в Параметри → Дисплей → Колірний профіль."))
        {
            Body.Children.Add(Ui.Card(Ui.Row(
                Ui.Btn(S.T("Install profile…", "Встановити профіль…"), Install, true, ""),
                Ui.Btn(S.T("Load calibration (LUT)", "Завантажити калібрування (LUT)"), () => { if (St.Display?.ArgyllNumber is int n) { Argyll.LoadInstalled(n); St.Report(S.T("Calibration loaded", "Калібрування завантажено"), 1); } }, false, ""),
                Ui.Btn(S.T("Reset LUT", "Скинути LUT"), () => { if (St.Display?.ArgyllNumber is int n) { Argyll.LutLinear(n); St.Report(S.T("Video LUT reset to linear", "LUT відеокарти скинуто"), 1); } }, false, ""),
                Ui.Btn(S.T("Display settings", "Параметри дисплея"), ColorProfiles.OpenDisplaySettings, false, ""),
                Ui.Btn(S.T("Profiles folder", "Тека профілів"), () => Process.Start("explorer.exe", ColorProfiles.ColorDir), false, ""))));
            Body.Children.Add(_list);
        }

        public override void OnShow() => Fill();

        void Fill()
        {
            _list.Children.Clear();
            var d = St.Display; if (d == null) return;
            foreach (bool hdr in new[] { false, true })
            {
                var def = ColorProfiles.Default(d, hdr);
                var names = ColorProfiles.Associated(d, hdr);
                if (def != null && !names.Contains(def)) names.Insert(0, def);
                var card = new StackPanel();
                card.Children.Add(Ui.H2(hdr ? S.T("HDR / advanced colour profiles", "HDR / розширені профілі") : S.T("SDR profiles", "SDR профілі")));
                if (names.Count == 0) card.Children.Add(Ui.Caption(S.T("none", "немає")));
                foreach (var n in names)
                {
                    var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                    g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var txt = Ui.Text((n == def ? "★ " : "    ") + n); if (n == def) txt.FontWeight = FontWeights.SemiBold;
                    var date = Ui.Caption(ColorProfiles.Created(n)?.ToString("yyyy-MM-dd HH:mm") ?? ""); date.VerticalAlignment = VerticalAlignment.Center;
                    var name = n; bool h = hdr;
                    var btns = Ui.Row(
                        n == def ? null : Ui.Btn(S.T("Make default", "Зробити основним"), () => { ColorProfiles.SetDefault(d, name, h); Fill(); }),
                        Ui.Btn(S.T("Remove", "Прибрати"), () => { ColorProfiles.Remove(d, name, h); Fill(); }));
                    btns.Margin = new Thickness(0);
                    Grid.SetColumn(date, 1); Grid.SetColumn(btns, 2);
                    g.Children.Add(txt); g.Children.Add(date); g.Children.Add(btns);
                    card.Children.Add(g);
                }
                _list.Children.Add(Ui.Card(card));
            }
        }

        void Install()
        {
            var d = St.Display; if (d == null) return;
            var dlg = new OpenFileDialog { Filter = "ICC profiles (*.icc;*.icm)|*.icc;*.icm", InitialDirectory = Paths.Docs };
            if (dlg.ShowDialog() != true) return;
            bool hdr = MessageBox.Show(S.T("Is this an HDR (advanced colour / MHC2) profile?", "Це HDR (MHC2) профіль?"), "HDRDisplayCalibrate", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            try
            {
                if (hdr) { ColorProfiles.Install(d, dlg.FileName, true); ColorProfiles.OpenDisplaySettings(); }
                else if (d.ArgyllNumber is int n) Argyll.InstallProfile(n, dlg.FileName);
                Fill();
            }
            catch (Exception e) { MessageBox.Show(e.Message); }
        }
    }

    public sealed class LogPage : Page
    {
        readonly TextBox _box = new() { IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 600 };
        public LogPage() : base(S.T("Log", "Журнал"), null)
        {
            Body.Children.Add(_box);
            Body.Children.Add(Ui.Row(Ui.Btn(S.T("Open log file", "Відкрити файл журналу"), () => Process.Start(new ProcessStartInfo(Path.Combine(Paths.Home, "log.txt")) { UseShellExecute = true })),
                                     Ui.Btn(S.T("Measurements folder", "Тека замірів"), () => Process.Start("explorer.exe", Paths.Docs))));
            Log.Line += s => Dispatcher.BeginInvoke(() => { _box.AppendText(s + "\n"); if (_box.LineCount > 4000) _box.Clear(); _box.ScrollToEnd(); });
        }
    }
}
