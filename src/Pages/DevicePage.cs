// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HDRDisplayCalibrate.Core;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Pages
{
    public sealed class DevicePage : Page
    {
        readonly ComboBox _displays = Ui.Combo(Array.Empty<string>(), 0, null, 420);
        readonly StackPanel _info = new();
        readonly ComboBox _instr = Ui.Combo(Array.Empty<string>(), 0, null, 420);
        readonly ComboBox _type, _patches;
        readonly TextBox _corr = Ui.Box("", 380), _argyll = Ui.Box("", 420);
        readonly CheckBox _video = Ui.Check(S.T("TV / video levels 16–235", "ТВ / відеорівні 16–235"));
        readonly TextBlock _argyllState = Ui.Caption("");
        bool _loading;

        public DevicePage() : base(S.T("Display & sensor", "Дисплей і датчик"),
            S.T("Pick the screen to calibrate, the colorimeter and how test patches are shown.", "Виберіть екран, колориметр і як показувати тестові патчі."))
        {
            _type = Ui.Combo(new[] { S.T("LCD / OLED (non-refresh)", "LCD / OLED (без регенерації)"), S.T("CRT / plasma / flickering backlight (refresh)", "CRT / плазма / мерехтлива підсвітка (refresh)") }, 0, _ => Save(), 420);
            _patches = Ui.Combo(new[] {
                S.T("Own window — full screen", "Власне вікно — на весь екран"),
                S.T("Own window — movable, resizable", "Власне вікно — перетягуване, змінний розмір"),
                S.T("Argyll window — full screen", "Вікно Argyll — на весь екран"),
                S.T("Argyll window — centre square", "Вікно Argyll — квадрат по центру") }, 0, _ => Save(), 420);
            _displays.SelectionChanged += (_, _) => { if (_loading) return; var i = _displays.SelectedIndex; if (i >= 0 && i < St.Displays.Count) { St.Display = St.Displays[i]; if (St.Display.ArgyllNumber is int n) St.Setup.Display = n; Save(); ShowInfo(); St.Notify(); } };
            _instr.SelectionChanged += (_, _) => Save();
            _video.Click += (_, _) => Save();
            _corr.LostFocus += (_, _) => Save();

            Body.Children.Add(Ui.Card(Ui.H2(S.T("Display", "Дисплей")),
                Ui.Row(_displays, Ui.Btn(S.T("Identify", "Показати номери"), Identify, false, ""), Ui.Btn(S.T("Refresh", "Оновити"), () => { St.RefreshDisplays(); OnShow(); }, false, "")),
                _info));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Colorimeter", "Колориметр")),
                Ui.Field(S.T("Instrument", "Пристрій"), _instr),
                Ui.Field(S.T("Display technology", "Технологія дисплея"), _type, S.T("i1Display / ColorMunki: LCD and OLED use non-refresh", "i1Display / ColorMunki: для LCD і OLED — без регенерації")),
                Ui.Field(S.T("Correction (.ccss / .ccmx)", "Корекція (.ccss / .ccmx)"), Ui.Row(_corr, Ui.Btn("…", PickCorrection)), S.T("Optional spectral correction for your panel type", "Необов'язкова спектральна корекція під тип панелі")),
                _video));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Test patches", "Тестові патчі")),
                Ui.Field(S.T("Show patches in", "Показувати патчі"), _patches,
                    S.T("Own window works full screen or as a window you can drag and resize; OLED/plasma with ABL prefer a window", "Власне вікно: на весь екран або перетягуване вікно; для OLED/плазми з ABL краще вікно")),
                Ui.Btn(S.T("Preview patch window (5 s)", "Показати вікно патчів (5 с)"), PreviewPatches, false, "")));
            Body.Children.Add(Ui.Card(Ui.H2("ArgyllCMS"),
                Ui.Field(S.T("bin folder", "Папка bin"), Ui.Row(_argyll, Ui.Btn(S.T("Apply", "Застосувати"), () => { St.Cfg.Set("argyll_bin", _argyll.Text.Trim()); St.Cfg.Save(); Argyll.Find(_argyll.Text.Trim()); St.RefreshDisplays(); OnShow(); }))),
                _argyllState));
        }

        public override void OnShow()
        {
            _loading = true;
            _displays.Items.Clear();
            foreach (var d in St.Displays) _displays.Items.Add($"{d.Name}  ·  {d.Model}  ·  {d.Bounds.w}×{d.Bounds.h}{(d.HdrOn ? "  ·  HDR" : "")}");
            _displays.SelectedIndex = Math.Max(0, St.Displays.IndexOf(St.Display));
            _instr.Items.Clear();
            try { foreach (var (n, name) in Argyll.Bin != null ? Argyll.Instruments() : new()) _instr.Items.Add($"{n}: {name}"); } catch { }
            if (_instr.Items.Count == 0) _instr.Items.Add(S.T("No colorimeter found — plug it in and press Refresh", "Колориметр не знайдено — підключіть і натисніть «Оновити»"));
            _instr.SelectedIndex = Math.Clamp(St.Setup.Instrument - 1, 0, _instr.Items.Count - 1);
            _type.SelectedIndex = St.Setup.DisplayType is "r" or "c" ? 1 : 0;
            _patches.SelectedIndex = (int)St.Setup.Patches;
            _corr.Text = St.Setup.Correction; _video.IsChecked = St.Setup.VideoRange;
            _argyll.Text = St.Cfg.Str("argyll_bin", Argyll.Bin ?? "");
            _argyllState.Text = Argyll.Bin != null ? "✓ " + Argyll.Bin : "✗ " + S.T("not found", "не знайдено");
            ShowInfo();
            _loading = false;
        }

        void ShowInfo()
        {
            _info.Children.Clear();
            var d = St.Display; if (d == null) return;
            Displays.RefreshColor(d);
            string ddc;
            try { using var x = Ddc.Open(d); var b = x?.Get(Ddc.Brightness); ddc = b == null ? S.T("no", "ні") : S.T($"yes · brightness {b.Value.cur}/{b.Value.max}", $"так · яскравість {b.Value.cur}/{b.Value.max}"); }
            catch { ddc = "?"; }
            _info.Children.Add(Ui.Table(new[] { "", "" }, new[]
            {
                new[] { S.T("Windows device", "Пристрій Windows"), d.GdiName },
                new[] { S.T("Argyll display", "Дисплей Argyll"), d.ArgyllNumber?.ToString() ?? "—" },
                new[] { "HDR", d.HdrSupported ? (d.HdrOn ? S.T("on", "увімкнено") : S.T("supported, off", "підтримується, вимкнено")) : S.T("not supported", "не підтримується") },
                new[] { S.T("Auto colour management (ACM)", "Автокерування кольором (ACM)"), d.WideSupported ? (d.AcmOn ? S.T("on", "увімкнено") : S.T("off", "вимкнено")) : "—" },
                new[] { S.T("SDR white in HDR", "Білий SDR у HDR"), d.HdrOn ? $"{d.SdrWhiteNits:F0} cd/m²" : "—" },
                new[] { "DDC/CI", ddc },
                new[] { S.T("SDR profile", "Профіль SDR"), ColorProfiles.Default(d, false) ?? "—" },
                new[] { S.T("HDR profile", "Профіль HDR"), ColorProfiles.Default(d, true) ?? "—" },
            }, new double[] { 230, 600 }));
            if (d.HdrSupported)
                _info.Children.Add(Ui.Row(Ui.Btn(d.HdrOn ? S.T("Turn HDR off", "Вимкнути HDR") : S.T("Turn HDR on", "Увімкнути HDR"),
                    async () => { Displays.SetHdr(d, !d.HdrOn); await Task.Delay(6000); St.RefreshDisplays(); OnShow(); }, false, "")));
            if (d.AcmOn && !d.HdrOn)
                _info.Children.Add(new TextBlock { Text = S.T("⚠ ACM is on: in SDR Windows then uses the HDR (advanced colour) profile instead of the ICC calibration. Turn it off in Settings → Display → Color profile for SDR calibration.",
                                                              "⚠ ACM увімкнено: у SDR Windows тоді бере HDR-профіль замість ICC-калібрування. Для SDR-калібрування вимкніть його в Параметри → Дисплей → Колірний профіль."), Foreground = Ui.Warm, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        }

        void Save()
        {
            if (_loading) return;
            St.Setup.Instrument = Math.Max(1, _instr.SelectedIndex + 1);
            St.Setup.DisplayType = _type.SelectedIndex == 1 ? "r" : "l";
            St.Setup.Patches = (PatchMode)Math.Max(0, _patches.SelectedIndex);
            St.Setup.Correction = _corr.Text.Trim(); St.Setup.VideoRange = _video.IsChecked == true;
            St.SaveSetup();
        }

        void PickCorrection()
        {
            var dlg = new OpenFileDialog { Filter = "Colorimeter correction (*.ccss;*.ccmx)|*.ccss;*.ccmx" };
            if (dlg.ShowDialog() == true) { _corr.Text = dlg.FileName; Save(); }
        }

        void Identify()
        {
            foreach (var d in St.Displays)
            {
                var w = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, Width = 360, Height = 220,
                    Background = new SolidColorBrush(Color.FromRgb(24, 24, 27)), WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = Ui.Stack(new TextBlock { Text = (St.Displays.IndexOf(d) + 1).ToString(), FontSize = 96, FontWeight = FontWeights.Bold, Foreground = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Center },
                                       new TextBlock { Text = d.Name, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center }),
                };
                w.Loaded += (_, _) =>
                {
                    var t = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                    w.Left = (d.Bounds.x + d.Bounds.w / 2) * t.M11 - 180; w.Top = (d.Bounds.y + d.Bounds.h / 2) * t.M22 - 110;
                };
                w.Show();
                _ = Task.Delay(3000).ContinueWith(_ => w.Dispatcher.Invoke(w.Close));
            }
        }

        void PreviewPatches()
        {
            if (St.Display == null) return;
            if (!St.Setup.OwnWindow) { MessageBox.Show(S.T("Argyll draws its own window during measurement.", "Argyll сам малює своє вікно під час заміру.")); return; }
            var w = new PatchWindow(St.Display, St.Setup.Patches == PatchMode.OwnFullscreen, St.Cfg);
            w.Show(); w.Paint(200, 200, 200);
            _ = Task.Delay(5000).ContinueWith(_ => w.Dispatcher.Invoke(w.Close));
        }
    }
}
