// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Added 2026 by DMD61 (Victory61): test patterns for checking a calibration by eye, at the end of a calibration
// or at any time as an independent check.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>One test pattern: a title, what to look at, and the screens it shows (built for a size in physical pixels).</summary>
    public sealed record TestPattern(string Title, string Check, Func<int, int, FrameworkElement>[] Screens);

    public sealed class PatternsPage : Page
    {
        public PatternsPage() : base(S.T("Test patterns", "Тестові шаблони"),
            S.T("Patterns for checking the calibration by eye: shades, neutral greys, shadows, highlights and colours. They can be used after a calibration or at any time, on their own.",
                "Шаблони для перевірки калібрування на око: переходи, нейтральний сірий, тіні, світла й кольори. Їх можна використовувати після калібрування або будь-коли окремо."))
        {
            Body.Children.Add(Ui.Card(
                Ui.Text(S.T("How to use them", "Як користуватися"), 15),
                Ui.Caption(S.T("The patterns open full screen on the display chosen in “Display & sensor”, with the colour profile that is active now. Next pattern: click, → or Space. Previous: right click or ←. Close: Esc.",
                               "Шаблони відкриваються на весь екран на дисплеї, вибраному в «Дисплей і датчик», з колірним профілем, який активний зараз. Наступний шаблон: клік, → або пробіл. Попередній: правий клік або ←. Закрити: Esc.")),
                Ui.Caption(S.T("At the bottom of each pattern a short line says what to look at; it disappears after a few seconds and comes back when you move the mouse.",
                               "Унизу кожного шаблону короткий рядок підказує, на що дивитися; він зникає за кілька секунд і повертається, коли ви рухаєте мишею.")),
                Ui.Caption(S.T("The patterns are shown as normal desktop content: they check greys, colours, shadows and shades, not the HDR highlights above the desktop white.",
                               "Шаблони показуються як звичайний вміст робочого столу: вони перевіряють сірі, кольори, тіні й переходи, але не HDR-світла понад білий робочого столу.")),
                Ui.Row(Ui.Btn(S.T("Show all patterns", "Показати всі шаблони"), () => Show(0), true, "",
                    tip: S.T("Opens the first pattern full screen; go on with a click or →.", "Відкриває перший шаблон на весь екран; далі — клік або →.")))));

            var all = Patterns.All;
            for (int i = 0; i < all.Count; i++)
            {
                int k = i;
                Body.Children.Add(Ui.Card(
                    Ui.Text($"{i + 1}. {all[i].Title}", 14),
                    Ui.Caption(all[i].Check),
                    Ui.Row(Ui.Btn(S.T("Show", "Показати"), () => Show(k), false, ""))));
            }
        }

        void Show(int index)
        {
            var d = St.Display;
            if (d == null) { MessageBox.Show(S.T("Choose the display first (Display & sensor).", "Спершу виберіть дисплей («Дисплей і датчик»)."), "HDRDisplayCalibrate"); return; }
            new PatternWindow(d, index).Show();
        }
    }

    /// <summary>The patterns. Levels are 8-bit code values, as any picture on the desktop.</summary>
    public static class Patterns
    {
        static byte P(double percent) => (byte)Math.Round(Math.Clamp(percent, 0, 100) * 2.55);

        /// <summary>Built on each call, so the texts follow the current language.</summary>
        public static List<TestPattern> All => new()
        {
            new(S.T("Grey ramp", "Сіра градієнтна смуга"),
                S.T("Top: black to white. Bottom: only the dark part, enlarged. The shade must be grey everywhere, without coloured bands (pink, green, violet). Even grey steps are normal: they are the 256 levels of the picture.",
                    "Угорі: від чорного до білого. Унизу: лише темна частина, збільшена. Перехід має бути сірим усюди, без кольорових смуг (рожевих, зелених, фіолетових). Рівні сірі сходинки — це нормально: це 256 рівнів зображення."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Ramps(w, h, new[] { (1.0, 1.0, 1.0, 1.0), (1.0, 1.0, 1.0, 0.25) }) }),

            new(S.T("Grey steps 0–100%", "Сірі сходинки 0–100%"),
                S.T("21 bars from 0 to 100% in steps of 5%. Every bar must look neutral grey, with no tint, and each one a little lighter than the one before.",
                    "21 смуга від 0 до 100% з кроком 5%. Кожна смуга має бути нейтрально-сірою, без відтінку, і кожна трохи світліша за попередню."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Steps() }),

            new(S.T("Shadow detail", "Деталі в тінях"),
                S.T("Squares from 1% to 10% on black. You should see as many as possible: if the first ones disappear into the black, shadows are crushed; if the black background looks grey, black is raised.",
                    "Квадрати від 1% до 10% на чорному. Їх має бути видно якомога більше: якщо перші зливаються з чорним, тіні «з'їдені»; якщо чорне тло виглядає сірим, чорний піднятий."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Squares(0, Enumerable.Range(1, 10).Select(i => (double)i).ToArray(), 30) }),

            new(S.T("Highlight detail", "Деталі у світлах"),
                S.T("Squares from 90% to 99% on white. They must all be distinguishable from the white background: if the last ones disappear, highlights are clipped.",
                    "Квадрати від 90% до 99% на білому. Усі мають відрізнятися від білого тла: якщо останні зникають, світла обрізані."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Squares(100, Enumerable.Range(90, 10).Select(i => (double)i).ToArray(), 60) }),

            new(S.T("Colour ramps", "Кольорові градієнти"),
                S.T("Red, green, blue, cyan, magenta and yellow from black to full. Each ramp must be smooth and keep its hue, without bands or sudden jumps.",
                    "Червоний, зелений, синій, блакитний, пурпуровий і жовтий від чорного до повного. Кожен перехід має бути плавним і тримати свій відтінок, без смуг і стрибків."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Ramps(w, h, new[] { (1.0, 0.0, 0.0, 1.0), (0.0, 1.0, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0), (0.0, 1.0, 1.0, 1.0), (1.0, 0.0, 1.0, 1.0), (1.0, 1.0, 0.0, 1.0) }) }),

            new(S.T("Saturations", "Насиченості"),
                S.T("Each colour at 25, 50, 75 and 100% saturation. From left to right it must grow evenly from pale to full, keeping the same hue, without turning neon or dull.",
                    "Кожен колір із насиченістю 25, 50, 75 і 100%. Зліва направо він має рівномірно зростати від блідого до повного, зберігаючи відтінок, не стаючи кислотним чи тьмяним."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Saturations() }),

            new(S.T("Reference colours", "Еталонні кольори"),
                S.T("The 24 classic check colours (skin, sky, foliage, primaries and greys). Skin must look natural, sky and foliage believable, the six greys at the bottom neutral.",
                    "24 класичні контрольні кольори (шкіра, небо, листя, основні кольори й сірі). Шкіра має виглядати природно, небо й листя — правдоподібно, шість сірих унизу — нейтрально."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Checker() }),

            new(S.T("Full screens 5%, 50%, 100%", "Повні екрани 5%, 50%, 100%"),
                S.T("Three full screens: dark grey, mid grey, white. Look for spots, darker edges or areas with a different tint across the panel.",
                    "Три повні екрани: темно-сірий, середньо-сірий, білий. Шукайте плями, темніші краї або ділянки з іншим відтінком на панелі."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Field(5), (w, h) => Field(50), (w, h) => Field(100) }),
        };

        static Brush Grey(double percent) { byte v = P(percent); return new SolidColorBrush(Color.FromRgb(v, v, v)); }

        static FrameworkElement Field(double percent) => new Border { Background = Grey(percent) };

        /// <summary>Horizontal bands, each from black to the colour (r, g, b) times "top" (1 = full, 0.25 = only the dark quarter).
        /// Drawn pixel for pixel, so nothing is smoothed by scaling.</summary>
        static FrameworkElement Ramps(int w, int h, (double r, double g, double b, double top)[] bands)
        {
            w = Math.Max(w, 2); h = Math.Max(h, bands.Length);
            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
            int stride = w * 3; var px = new byte[stride * h];
            for (int y = 0; y < h; y++)
            {
                var (r, g, b, top) = bands[Math.Min(y * bands.Length / h, bands.Length - 1)];
                for (int x = 0; x < w; x++)
                {
                    double t = x / (double)(w - 1) * top;
                    int o = y * stride + x * 3;
                    px[o] = (byte)Math.Round(255 * t * b); px[o + 1] = (byte)Math.Round(255 * t * g); px[o + 2] = (byte)Math.Round(255 * t * r);
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
            var img = new Image { Source = bmp, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            return img;
        }

        static FrameworkElement Steps()
        {
            var g = new UniformGrid { Rows = 1, Columns = 21 };
            for (int i = 0; i <= 20; i++)
            {
                double p = i * 5;
                g.Children.Add(new Border
                {
                    Background = Grey(p),
                    Child = new TextBlock { Text = $"{p:0}", FontSize = 14, Foreground = Grey(p < 50 ? p + 30 : p - 30), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 40) }
                });
            }
            return g;
        }

        /// <summary>A row of squares at the given levels on a background, with small labels.</summary>
        static FrameworkElement Squares(double background, double[] levels, double labelLevel)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var l in levels)
                row.Children.Add(new StackPanel
                {
                    Margin = new Thickness(12),
                    Children =
                    {
                        new Border { Width = 110, Height = 110, Background = Grey(l) },
                        new TextBlock { Text = $"{l:0}%", FontSize = 16, Foreground = Grey(labelLevel), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) },
                    }
                });
            return new Border { Background = Grey(background), Child = new Viewbox { Child = row, Margin = new Thickness(60), StretchDirection = StretchDirection.Both } };
        }

        static FrameworkElement Saturations()
        {
            var cols = new[] { 25.0, 50, 75, 100 };
            var hues = new (double r, double g, double b)[] { (1, 0, 0), (0, 1, 0), (0, 0, 1), (0, 1, 1), (1, 0, 1), (1, 1, 0) };
            var grid = new Grid();
            for (int c = 0; c < cols.Length; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int r = 0; r < hues.Length; r++) grid.RowDefinitions.Add(new RowDefinition());
            for (int c = 0; c < cols.Length; c++)
            {
                var t = new TextBlock { Text = $"{cols[c]:0}%", FontSize = 16, Foreground = Grey(45), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
                Grid.SetColumn(t, c); grid.Children.Add(t);
            }
            const double stim = 0.75;   // 75% stimulus, the usual level for saturation sweeps
            for (int r = 0; r < hues.Length; r++)
                for (int c = 0; c < cols.Length; c++)
                {
                    double s = cols[c] / 100;
                    byte V(double on) => (byte)Math.Round(255 * stim * (on > 0 ? 1 : 1 - s));
                    var b = new Border { Background = new SolidColorBrush(Color.FromRgb(V(hues[r].r), V(hues[r].g), V(hues[r].b))), Margin = new Thickness(6) };
                    Grid.SetRow(b, r + 1); Grid.SetColumn(b, c); grid.Children.Add(b);
                }
            return new Border { Background = Grey(10), Padding = new Thickness(30), Child = grid };
        }

        /// <summary>ColorChecker Classic, sRGB (D65) values of the 24 patches.</summary>
        static readonly (byte r, byte g, byte b, string en, string uk)[] Patches =
        {
            (115, 82, 68, "Dark skin", "Темна шкіра"), (194, 150, 130, "Light skin", "Світла шкіра"), (98, 122, 157, "Blue sky", "Блакитне небо"),
            (87, 108, 67, "Foliage", "Листя"), (133, 128, 177, "Blue flower", "Синя квітка"), (103, 189, 170, "Bluish green", "Блакитно-зелений"),
            (214, 126, 44, "Orange", "Помаранчевий"), (80, 91, 166, "Purplish blue", "Фіолетово-синій"), (193, 90, 99, "Moderate red", "Помірний червоний"),
            (94, 60, 108, "Purple", "Фіолетовий"), (157, 188, 64, "Yellow green", "Жовто-зелений"), (224, 163, 46, "Orange yellow", "Помаранчево-жовтий"),
            (56, 61, 150, "Blue", "Синій"), (70, 148, 73, "Green", "Зелений"), (175, 54, 60, "Red", "Червоний"),
            (231, 199, 31, "Yellow", "Жовтий"), (187, 86, 149, "Magenta", "Пурпуровий"), (8, 133, 161, "Cyan", "Блакитний"),
            (243, 243, 242, "White", "Білий"), (200, 200, 200, "Neutral 8", "Нейтральний 8"), (160, 160, 160, "Neutral 6.5", "Нейтральний 6.5"),
            (122, 122, 121, "Neutral 5", "Нейтральний 5"), (85, 85, 85, "Neutral 3.5", "Нейтральний 3.5"), (52, 52, 52, "Black", "Чорний"),
        };

        static FrameworkElement Checker()
        {
            var g = new UniformGrid { Rows = 4, Columns = 6 };
            foreach (var p in Patches)
                g.Children.Add(new Border
                {
                    Margin = new Thickness(10), Background = new SolidColorBrush(Color.FromRgb(p.r, p.g, p.b)),
                    Child = new TextBlock { Text = S.T(p.en, p.uk), FontSize = 14, Foreground = Grey(p.r + p.g + p.b > 380 ? 25 : 80), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Bottom }
                });
            return new Border { Background = Grey(12), Padding = new Thickness(30), Child = g };
        }
    }

    /// <summary>Full-screen window on the chosen monitor that shows the patterns one after another.</summary>
    public sealed class PatternWindow : Window
    {
        readonly DisplayTarget _d;
        readonly List<TestPattern> _all = Patterns.All;
        readonly List<(TestPattern p, int n, Func<int, int, FrameworkElement> make)> _screens = new();
        readonly Grid _root = new();
        readonly Border _hint = new() { Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 900 };
        readonly TextBlock _hintText = new() { Foreground = Brushes.White, FontSize = 15, TextWrapping = TextWrapping.Wrap };
        readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(6) };
        int _i;
        Point _lastMouse = new(double.NaN, double.NaN);

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        static readonly IntPtr HwndTopmost = new(-1);

        public PatternWindow(DisplayTarget d, int pattern)
        {
            _d = d;
            foreach (var p in _all)
                for (int k = 0; k < p.Screens.Length; k++) _screens.Add((p, k, p.Screens[k]));
            var first = _all[Math.Clamp(pattern, 0, _all.Count - 1)];
            _i = Math.Max(0, _screens.FindIndex(s => ReferenceEquals(s.p, first)));

            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
            Background = Brushes.Black; Title = "HDRDisplayCalibrate patterns"; Cursor = Cursors.None;
            WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; Width = 200; Height = 200;
            _hint.Child = _hintText;
            Content = _root;

            SourceInitialized += (_, _) =>
            {
                var (x, y, w, h) = _d.Bounds;
                SetWindowPos(new WindowInteropHelper(this).Handle, HwndTopmost, x, y, w, h, 0x0040);
            };
            Loaded += (_, _) => { Activate(); Focus(); Paint(); };
            KeyDown += (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Escape: Close(); break;
                    case Key.Right: case Key.Space: case Key.Enter: case Key.PageDown: Go(1); break;
                    case Key.Left: case Key.Back: case Key.PageUp: Go(-1); break;
                    default: return;
                }
                e.Handled = true;
            };
            MouseLeftButtonUp += (_, _) => Go(1);
            MouseRightButtonUp += (_, _) => Go(-1);
            MouseMove += (_, e) =>
            {
                var pt = e.GetPosition(this);
                if (!double.IsNaN(_lastMouse.X) && (pt - _lastMouse).Length > 4) ShowHint();
                _lastMouse = pt;
            };
            _hide.Tick += (_, _) => { _hide.Stop(); _hint.Visibility = Visibility.Collapsed; };
            Closed += (_, _) => _hide.Stop();
        }

        void Go(int step) { _i = (_i + step + _screens.Count) % _screens.Count; Paint(); }

        void Paint()
        {
            var (p, n, make) = _screens[_i];
            var (_, _, w, h) = _d.Bounds;
            _root.Children.Clear();
            _root.Children.Add(make(w, h));
            _root.Children.Add(_hint);
            int number = _all.IndexOf(p) + 1;
            string part = p.Screens.Length > 1 ? $" ({n + 1}/{p.Screens.Length})" : "";
            _hintText.Text = $"{number}/{_all.Count} · {p.Title}{part}\n{p.Check}\n" +
                             S.T("Next: click or →   ·   Previous: right click or ←   ·   Close: Esc", "Далі: клік або →   ·   Назад: правий клік або ←   ·   Закрити: Esc");
            ShowHint();
        }

        void ShowHint() { _hint.Visibility = Visibility.Visible; _hide.Stop(); _hide.Start(); }
    }
}
