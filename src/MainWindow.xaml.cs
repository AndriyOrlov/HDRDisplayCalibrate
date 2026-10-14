// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using HDRDisplayCalibrate.Pages;

namespace HDRDisplayCalibrate
{
    public partial class MainWindow : Window
    {
        readonly List<(string icon, string title, string tip, Func<Page> make)> _pages = new()
        {
            ("", S.T("Display & sensor", "Дисплей і датчик"), S.T("Choose the display, the colorimeter, how patches are shown, and the language.", "Вибір дисплея, колориметра, способу показу патчів і мови."), () => new DevicePage()),
            ("", S.T("Live meter", "Живий замір"), S.T("Continuous readings while you adjust the monitor's menu, or automatic adjustment over DDC/CI.", "Безперервні заміри, поки ви налаштовуєте меню монітора, або автоматичне налаштування через DDC/CI."), () => new LivePage()),
            ("", S.T("SDR calibration", "SDR калібрування"), S.T("Grey scale, white point and an ICC profile for normal (SDR) mode.", "Сіра шкала, точка білого та ICC-профіль для звичайного (SDR) режиму."), () => new SdrPage()),
            ("", S.T("HDR calibration", "HDR калібрування"), S.T("Step-by-step Windows HDR calibration into a native MHC2 profile.", "Покрокове калібрування Windows HDR у рідний MHC2-профіль."), () => new HdrPage()),
            ("", S.T("Verify & report", "Перевірка і звіт"), S.T("Check how accurate the display is now: ΔE, gamma, contrast.", "Перевірка, наскільки точний дисплей зараз: ΔE, гамма, контраст."), () => new VerifyPage()),
            ("", S.T("Gamut", "Гамут"), S.T("Which colour standards the display can show, in 2D and 3D.", "Які колірні стандарти дисплей може показати — у 2D і 3D."), () => new GamutPage()),
            ("", S.T("3D LUT", "3D-LUT"), S.T("Export the calibration as a 3D LUT for madVR, Resolve, OBS or ReShade.", "Експорт калібрування як 3D-LUT для madVR, Resolve, OBS чи ReShade."), () => new LutPage()),
            ("", S.T("Profiles", "Профілі"), S.T("The display's SDR and HDR profiles, video card LUT tools.", "SDR і HDR профілі дисплея, інструменти LUT відеокарти."), () => new ProfilesPage()),
            ("", S.T("Log", "Журнал"), S.T("Everything the program and ArgyllCMS did — useful for reporting problems.", "Усе, що робили програма й ArgyllCMS, — знадобиться для повідомлень про проблеми."), () => new LogPage()),
        };
        readonly Dictionary<int, Page> _made = new();
        public int Page => Math.Max(0, Nav.SelectedIndex);

        public MainWindow(int startPage = 0)
        {
            InitializeComponent();
            CancelButton.Content = S.T("Cancel (Esc)", "Скасувати (Esc)");
            Ui.Tip(CancelButton, S.T("Stops the running measurement at once and restores the display calibration. Esc does the same from any window.", "Одразу зупиняє поточний замір і повертає калібрування дисплея. Esc робить те саме з будь-якого вікна."));
            if (S.Rtl) FlowDirection = FlowDirection.RightToLeft;
            foreach (var (icon, title, tip, _) in _pages)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
                sp.Children.Add(new TextBlock { Text = icon, FontFamily = Ui.Icons, FontSize = 16, Width = 28, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
                Nav.Items.Add(Ui.Tip(new ListBoxItem { Content = sp }, tip));
            }
            SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                int on = 1, mica = 2;
                DwmSetWindowAttribute(h, 20, ref on, 4);     // dark title bar
                DwmSetWindowAttribute(h, 38, ref mica, 4);   // Mica backdrop
                HwndSource.FromHwnd(h).AddHook((IntPtr hw, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == 0x0312 && wp.ToInt32() == EscHotkey) { AppState.I.Cancel(); handled = true; }   // WM_HOTKEY
                    return IntPtr.Zero;
                });
            };
            // Esc cancels any measurement at once, wherever the focus is (our patch window, Argyll's window, another app):
            // the key is taken globally only while a job runs
            PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && AppState.I.Job != null) { AppState.I.Cancel(); e.Handled = true; } };
            Action<string, double> onStatus = (t, p) =>
            {
                StatusText.Text = t;
                StatusBar.IsIndeterminate = p < 0 && AppState.I.Job != null;
                if (p >= 0) StatusBar.Value = p * 100;
            };
            Action onChanged = () => Dispatcher.Invoke(() =>
            {
                CancelButton.IsEnabled = AppState.I.Job != null;
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    if (AppState.I.Job != null && !_escOn) _escOn = RegisterHotKey(hwnd, EscHotkey, 0x4000 /*MOD_NOREPEAT*/, 0x1B);
                    else if (AppState.I.Job == null && _escOn) { UnregisterHotKey(hwnd, EscHotkey); _escOn = false; }
                }
                if (AppState.I.Job == null) StatusBar.IsIndeterminate = false;
                var d = AppState.I.Display;
                TitleDisplay.Text = d == null ? "" : $"· {d.Name}{(d.HdrOn ? " · HDR" : "")}";
            });
            AppState.I.Status += onStatus; AppState.I.Changed += onChanged;
            Closed += (_, _) =>          // a window replaced by a language reload stops listening
            {
                AppState.I.Status -= onStatus; AppState.I.Changed -= onChanged;
                var hw = new WindowInteropHelper(this).Handle;
                if (_escOn && hw != IntPtr.Zero) UnregisterHotKey(hw, EscHotkey);
            };
            Loaded += (_, _) =>
            {
                AppState.I.Init();
                if (Core.Argyll.Bin == null)
                    MessageBox.Show(S.T("ArgyllCMS was not found. Install ArgyllCMS (or DisplayCAL) and set its bin folder on the first page.",
                                        "Не знайдено ArgyllCMS. Встановіть ArgyllCMS (або DisplayCAL) і вкажіть папку bin на першій сторінці."), "HDRDisplayCalibrate");
                Nav.SelectedIndex = Math.Clamp(startPage, 0, _pages.Count - 1);
            };
            Closing += (_, _) => AppState.I.Cancel();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int EscHotkey = 0xE5C;
        bool _escOn;

        void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int i = Nav.SelectedIndex; if (i < 0) return;
            if (!_made.TryGetValue(i, out var p)) _made[i] = p = _pages[i].make();
            Host.Content = p; p.OnShow();
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => AppState.I.Cancel();
    }
}
