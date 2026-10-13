// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate
{
    /// <summary>
    /// Our own patch window for Argyll's web display mode: full screen on the chosen monitor,
    /// or a borderless window that can be dragged and resized (position is remembered).
    /// Placement uses physical pixels (SetWindowPos), so mixed-DPI desktops land exactly on the monitor.
    /// </summary>
    public sealed class PatchWindow : Window
    {
        readonly Border _patch = new() { Background = Brushes.Gray };
        readonly TextBlock _hint = new() { Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), FontSize = 12, Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap };
        readonly bool _fullscreen;
        readonly DisplayTarget _d;
        readonly Settings _s;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int l, t, r, b; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        static readonly IntPtr HwndTopmost = new(-1);
        const uint SWP_SHOWWINDOW = 0x0040;

        public PatchWindow(DisplayTarget d, bool fullscreen, Settings s)
        {
            _fullscreen = fullscreen; _d = d; _s = s;
            WindowStyle = WindowStyle.None; ResizeMode = fullscreen ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            Topmost = true; ShowInTaskbar = false; Background = Brushes.Black; Title = "HDRDisplayCalibrate patches";
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; Width = 200; Height = 200;
            var g = new Grid(); g.Children.Add(_patch); g.Children.Add(_hint); Content = g;
            _hint.Text = fullscreen ? "" : S.T("Drag to move · drag the corner to resize · put the sensor in the middle", "Тягніть, щоб перемістити · кут — змінити розмір · датчик по центру");
            SourceInitialized += (_, _) => Place();
            MouseLeftButtonDown += (_, e) => { if (!_fullscreen && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Closing += (_, _) => Remember();
            if (fullscreen) Cursor = Cursors.None;
        }

        void Place()
        {
            var h = new WindowInteropHelper(this).Handle;
            var (x, y, w, hh) = _d.Bounds;
            if (_fullscreen) { SetWindowPos(h, HwndTopmost, x, y, w, hh, SWP_SHOWWINDOW); return; }
            string key = "patch_" + _d.Model;
            int size = Math.Min(w, hh) / 3;
            int px = (int)_s.Num(key + "_x", x + (w - size) / 2), py = (int)_s.Num(key + "_y", y + (hh - size) / 2);
            int pw = (int)_s.Num(key + "_w", size), ph = (int)_s.Num(key + "_h", size);
            SetWindowPos(h, HwndTopmost, px, py, pw, ph, SWP_SHOWWINDOW);
        }

        void Remember()
        {
            if (_fullscreen) return;
            var h = new WindowInteropHelper(this).Handle;
            if (!GetWindowRect(h, out var r)) return;
            string key = "patch_" + _d.Model;
            _s.Set(key + "_x", r.l); _s.Set(key + "_y", r.t); _s.Set(key + "_w", r.r - r.l); _s.Set(key + "_h", r.b - r.t); _s.Save();
        }

        public void Paint(byte r, byte g, byte b) { _patch.Background = new SolidColorBrush(Color.FromRgb(r, g, b)); _hint.Visibility = Visibility.Collapsed; }

        /// <summary>Waits until WPF has rendered the new colour and a couple of display refreshes have passed.</summary>
        public async Task Rendered()
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(60);
        }
    }
}
