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
    /// <summary>One test pattern: title, what it shows, how it looks when the calibration is right, what a defect looks like,
    /// and the screens it shows (built for a size in physical pixels).</summary>
    public sealed record TestPattern(string Title, string What, string Ok, string Bad, Func<int, int, FrameworkElement>[] Screens);

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
                Ui.Caption(S.T("Below, for each pattern: what it shows, how it looks when the calibration is right (✓) and what a defect looks like (✗). The same lines appear at the bottom of the pattern; they disappear after a few seconds and come back when you move the mouse.",
                               "Нижче для кожного шаблону: що він показує, який вигляд має при правильному калібруванні (✓) і як виглядає дефект (✗). Ці самі рядки з'являються внизу шаблону; вони зникають за кілька секунд і повертаються, коли ви рухаєте мишею.")),
                Ui.Caption(S.T("The patterns are shown as normal desktop content: they check greys, colours, shadows and shades, not the HDR highlights above the desktop white.",
                               "Шаблони показуються як звичайний вміст робочого столу: вони перевіряють сірі, кольори, тіні й переходи, але не HDR-світла понад білий робочого столу.")),
                Ui.Row(Ui.Btn(S.T("Show all patterns", "Показати всі шаблони"), () => Show(0), true, "\uE768",
                    tip: S.T("Opens the first pattern full screen; go on with a click or →.", "Відкриває перший шаблон на весь екран; далі — клік або →.")))));

            var all = Patterns.All;
            for (int i = 0; i < all.Count; i++)
            {
                int k = i;
                Body.Children.Add(Ui.Card(
                    Ui.Text($"{i + 1}. {all[i].Title}", 15),
                    Ui.Caption(all[i].What),
                    Verdict("✓  " + S.T("Calibration OK:", "Калібрування в порядку:"), all[i].Ok, Ui.Good),
                    Verdict("✗  " + S.T("Defect:", "Дефект:"), all[i].Bad, Ui.Bad),
                    Ui.Row(Ui.Btn(S.T("Show", "Показати"), () => Show(k), false, "\uE7B3"))));
            }
        }

        static UIElement Verdict(string label, string text, Brush colour)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 6, 0, 0) };
            t.Inlines.Add(new System.Windows.Documents.Run(label + " ") { Foreground = colour, FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new System.Windows.Documents.Run(text));
            return t;
        }

        /// <summary>Card shown at the end of a calibration: the check by eye is suggested, with a button that opens this page.</summary>
        public static UIElement Suggestion()
        {
            var t = Ui.Text(S.T("If you wish, you can check the result by eye in the “Test patterns” section: shades, neutral greys, shadows, highlights and colours.",
                                "За бажанням результат можна перевірити на око в розділі «Тестові шаблони»: переходи, нейтральний сірий, тіні, світла й кольори."), 14);
            var card = Ui.Card(t, Ui.Row(Ui.Btn(S.T("Open Test patterns", "Відкрити тестові шаблони"), () => MainWindow.ShowPage(MainWindow.PatternsPageIndex), true, "\uE790")));
            card.BorderBrush = Ui.Accent; card.BorderThickness = new Thickness(2);
            return card;
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
                S.T("Top: from black (left) to white (right). Bottom: only the darkest quarter, enlarged.",
                    "Угорі: від чорного (ліворуч) до білого (праворуч). Унизу: лише найтемніша чверть, збільшена."),
                S.T("The whole shade is grey from left to right, in both halves. Small, even grey steps are normal (they are the 256 levels of the picture), above all in the bottom half.",
                    "Увесь перехід сірий зліва направо в обох половинах. Дрібні рівні сірі сходинки — це нормально (це 256 рівнів зображення), особливо в нижній половині."),
                S.T("Bands or patches with a colour (pink, green, violet, brownish); some steps much wider or more visible than the others; a stretch where the shade stays flat.",
                    "Смуги або плями з кольором (рожеві, зелені, фіолетові, коричнюваті); окремі сходинки значно ширші чи помітніші за інші; ділянка, де перехід стоїть на місці."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Ramps(w, h, new[] { (1.0, 1.0, 1.0, 1.0), (1.0, 1.0, 1.0, 0.25) }) }),

            new(S.T("Grey steps 0–100%", "Сірі сходинки 0–100%"),
                S.T("21 bars from 0% (left) to 100% (right), 5% apart.",
                    "21 смуга від 0% (ліворуч) до 100% (праворуч), через 5%."),
                S.T("Every bar is neutral grey, each one a little lighter than the one on its left, and you can tell all of them apart, also the first dark ones and the last light ones.",
                    "Кожна смуга нейтрально-сіра, кожна трохи світліша за сусідню ліворуч, і всі їх можна розрізнити, також перші темні й останні світлі."),
                S.T("A bar with a tint (pinkish, greenish, bluish, yellowish); two neighbouring bars that look the same; a bar darker than the one on its left.",
                    "Смуга з відтінком (рожевуватим, зеленуватим, синюватим, жовтуватим); дві сусідні смуги, що виглядають однаково; смуга темніша за ту, що ліворуч."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Steps() }),

            new(S.T("Shadow detail", "Деталі в тінях"),
                S.T("Ten squares from 1% to 10% on a black background. Look at it with the room light low.",
                    "Десять квадратів від 1% до 10% на чорному тлі. Дивіться при приглушеному світлі в кімнаті."),
                S.T("You see the squares at least from 2–3%, each one a little lighter than the one before, all grey; the background stays deep black.",
                    "Квадрати видно щонайменше від 2–3%, кожен трохи світліший за попередній, усі сірі; тло лишається глибоким чорним."),
                S.T("Several squares (for example up to 5%) disappear into the black: shadows are crushed. The background looks dark grey instead of black: black is raised. Squares with a tint.",
                    "Кілька квадратів (наприклад, до 5%) зливаються з чорним: тіні «з'їдені». Тло виглядає темно-сірим замість чорного: чорний піднятий. Квадрати з відтінком."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Squares(0, Enumerable.Range(1, 10).Select(i => (double)i).ToArray(), 30) }),

            new(S.T("Highlight detail", "Деталі у світлах"),
                S.T("Ten squares from 90% to 99% on a white background.",
                    "Десять квадратів від 90% до 99% на білому тлі."),
                S.T("All squares stand out from the white, the last ones (98–99%) at least faintly, and they are all neutral grey.",
                    "Усі квадрати вирізняються на білому, останні (98–99%) хоча б ледь помітно, і всі нейтрально-сірі."),
                S.T("The last squares disappear into the white: highlights are clipped (burnt). Squares with a tint, or a white background that looks coloured.",
                    "Останні квадрати зникають у білому: світла обрізані («пропалені»). Квадрати з відтінком або біле тло, що виглядає кольоровим."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Squares(100, Enumerable.Range(90, 10).Select(i => (double)i).ToArray(), 60) }),

            new(S.T("Colour ramps", "Кольорові градієнти"),
                S.T("Six bands, red, green, blue, cyan, magenta and yellow, from black (left) to full colour (right).",
                    "Шість смуг — червона, зелена, синя, блакитна, пурпурова й жовта — від чорного (ліворуч) до повного кольору (праворуч)."),
                S.T("Each band grows smoothly from left to right and keeps the same hue all along; small even steps are normal.",
                    "Кожна смуга плавно наростає зліва направо й тримає той самий відтінок по всій довжині; дрібні рівні сходинки — це нормально."),
                S.T("Visible bands or jumps; the hue changes along the band (red turning orange, blue turning violet, yellow turning greenish); the colour reaches full long before the right edge and then stays flat.",
                    "Помітні смуги або стрибки; відтінок змінюється вздовж смуги (червоний стає помаранчевим, синій — фіолетовим, жовтий — зеленуватим); колір досягає повного задовго до правого краю, а далі стоїть."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Ramps(w, h, new[] { (1.0, 0.0, 0.0, 1.0), (0.0, 1.0, 0.0, 1.0), (0.0, 0.0, 1.0, 1.0), (0.0, 1.0, 1.0, 1.0), (1.0, 0.0, 1.0, 1.0), (1.0, 1.0, 0.0, 1.0) }) }),

            new(S.T("Saturations", "Насиченості"),
                S.T("Rows: red, green, blue, cyan, magenta, yellow. Columns: 25, 50, 75 and 100% saturation.",
                    "Рядки: червоний, зелений, синій, блакитний, пурпуровий, жовтий. Стовпці: насиченість 25, 50, 75 і 100%."),
                S.T("In each row the colour grows evenly from pale (25%) to intense (100%), with steps that look equally spaced, and keeps the same hue across the row.",
                    "У кожному рядку колір рівномірно зростає від блідого (25%) до насиченого (100%), кроки виглядають однаковими, а відтінок у рядку не змінюється."),
                S.T("75% and 100% look the same, or the colours look neon (too saturated); the 100% looks dull (not saturated enough); the hue shifts along the row (a pale red that looks pink, a full one that looks orange).",
                    "75% і 100% виглядають однаково, або кольори кислотні (занадто насичені); 100% виглядає тьмяним (недостатньо насичений); відтінок змінюється вздовж рядка (блідий червоний виглядає рожевим, повний — помаранчевим)."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Saturations() }),

            new(S.T("Reference colours", "Еталонні кольори"),
                S.T("The 24 classic check colours: skin, sky, foliage, flowers, primaries and, in the bottom row, six greys from white to black.",
                    "24 класичні контрольні кольори: шкіра, небо, листя, квіти, основні кольори й у нижньому рядку шість сірих від білого до чорного."),
                S.T("The two skin tones look natural, the sky a believable blue, the foliage a natural green; the six greys at the bottom are neutral and evenly spaced from white to black.",
                    "Два відтінки шкіри виглядають природно, небо — правдоподібно блакитним, листя — природно зеленим; шість сірих унизу нейтральні й рівномірно йдуть від білого до чорного."),
                S.T("Skin too orange, too red or greyish-green; colours that look fluorescent or washed out; greys at the bottom with a tint.",
                    "Шкіра надто помаранчева, надто червона або сіро-зелена; кольори виглядають флуоресцентними чи вицвілими; сірі внизу з відтінком."),
                new Func<int, int, FrameworkElement>[] { (w, h) => Checker() }),

            new(S.T("Full screens 5%, 50%, 100%", "Повні екрани 5%, 50%, 100%"),
                S.T("Three full screens one after the other: dark grey (5%), mid grey (50%), white (100%).",
                    "Три повні екрани один за одним: темно-сірий (5%), середньо-сірий (50%), білий (100%)."),
                S.T("Each screen has the same brightness and the same tint everywhere. On the dark grey, slightly darker corners can be normal for an LCD panel.",
                    "Кожен екран має однакову яскравість і однаковий відтінок усюди. На темно-сірому трохи темніші кути можуть бути нормою для РК-панелі."),
                S.T("Spots, clouds, vertical or horizontal bands, clearly darker edges, or areas with a different tint (pinkish, greenish) from the rest. These are panel limits: a calibration cannot fix them.",
                    "Плями, «хмари», вертикальні чи горизонтальні смуги, помітно темніші краї або ділянки з іншим відтінком (рожевуватим, зеленуватим). Це обмеження панелі: калібрування їх не виправляє."),
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
        readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(12) };
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
            _hintText.Text = $"{number}/{_all.Count} · {p.Title}{part}\n" +
                             "✓  " + S.T("Calibration OK:", "Калібрування в порядку:") + " " + p.Ok + "\n" +
                             "✗  " + S.T("Defect:", "Дефект:") + " " + p.Bad + "\n" +
                             S.T("Next: click or →   ·   Previous: right click or ←   ·   Close: Esc", "Далі: клік або →   ·   Назад: правий клік або ←   ·   Закрити: Esc");
            ShowHint();
        }

        void ShowHint() { _hint.Visibility = Visibility.Visible; _hide.Stop(); _hide.Start(); }
    }
}
