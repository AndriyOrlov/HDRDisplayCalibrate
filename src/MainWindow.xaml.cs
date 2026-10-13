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
        readonly List<(string icon, string title, Func<Page> make)> _pages = new()
        {
            ("", S.T("Display & sensor", "Дисплей і датчик"), () => new DevicePage()),
            ("", S.T("Live meter", "Живий замір"), () => new LivePage()),
            ("", S.T("SDR calibration", "SDR калібрування"), () => new SdrPage()),
            ("", S.T("HDR calibration", "HDR калібрування"), () => new HdrPage()),
            ("", S.T("Verify & report", "Перевірка і звіт"), () => new VerifyPage()),
            ("", S.T("Gamut", "Гамут"), () => new GamutPage()),
            ("", S.T("3D LUT", "3D-LUT"), () => new LutPage()),
            ("", S.T("Profiles", "Профілі"), () => new ProfilesPage()),
            ("", S.T("Log", "Журнал"), () => new LogPage()),
        };
        readonly Dictionary<int, Page> _made = new();

        public MainWindow(int startPage = 0)
        {
            InitializeComponent();
            CancelButton.Content = S.T("Cancel (Esc)", "Скасувати (Esc)");
            foreach (var (icon, title, _) in _pages)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };
                sp.Children.Add(new TextBlock { Text = icon, FontFamily = Ui.Icons, FontSize = 16, Width = 28, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
                Nav.Items.Add(new ListBoxItem { Content = sp });
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
            AppState.I.Status += (t, p) =>
            {
                StatusText.Text = t;
                StatusBar.IsIndeterminate = p < 0 && AppState.I.Job != null;
                if (p >= 0) StatusBar.Value = p * 100;
            };
            AppState.I.Changed += () => Dispatcher.Invoke(() =>
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
