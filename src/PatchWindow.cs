// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate
{
    /// <summary>
    /// Our own patch window for Argyll's web display mode: full screen on the chosen monitor,
    /// or a borderless window that can be dragged and resized (position is remembered).
    /// </summary>
    public sealed class PatchWindow : Window
    {
        readonly Border _patch = new() { Background = Brushes.Gray };
        readonly TextBlock _hint = new() { Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), FontSize = 12, Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Bottom };
        readonly bool _fullscreen;

        public PatchWindow(DisplayTarget d, bool fullscreen, Settings s)
        {
            _fullscreen = fullscreen;
            WindowStyle = WindowStyle.None; ResizeMode = fullscreen ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            Topmost = true; ShowInTaskbar = false; Background = Brushes.Black; Title = "HDRDisplayCalibrate patches";
            var g = new Grid(); g.Children.Add(_patch); g.Children.Add(_hint); Content = g;
            _hint.Text = fullscreen ? "" : S.T("Drag to move · drag the corner to resize · put the sensor in the middle", "Тягніть, щоб перемістити · кут — змінити розмір · датчик по центру");
            WindowStartupLocation = WindowStartupLocation.Manual;
            var (x, y, w, h) = d.Bounds;
            Loaded += (_, _) =>
            {
                var src = PresentationSource.FromVisual(this);
                double sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1, sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1;
                if (fullscreen) { Left = x * sx; Top = y * sy; Width = w * sx; Height = h * sy; }
                else
                {
                    double ww = s.Num("patch_w", 420), hh = s.Num("patch_h", 420);
                    Width = ww; Height = hh;
                    Left = s.J.ContainsKey("patch_x") ? s.Num("patch_x", 0) : (x + w / 2) * sx - ww / 2;
                    Top = s.J.ContainsKey("patch_y") ? s.Num("patch_y", 0) : (y + h / 2) * sy - hh / 2;
                }
            };
            MouseLeftButtonDown += (_, e) => { if (!_fullscreen && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            Closing += (_, _) =>
            {
                if (_fullscreen) return;
                s.Set("patch_x", Left); s.Set("patch_y", Top); s.Set("patch_w", Width); s.Set("patch_h", Height); s.Save();
            };
            if (fullscreen) Cursor = Cursors.None;
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
