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
        readonly CheckBox _video = Ui.Check(S.T("TV / video levels 16–235", "ТВ / відеорівні 16–235"), tip: S.T("Tick only if the display receives a video-range signal (16–235), e.g. a TV on HDMI set to limited range. PC monitors use the full range 0–255.", "Позначте, лише якщо дисплей отримує відеодіапазон 16–235, напр. ТВ по HDMI з обмеженим діапазоном. Монітори на ПК працюють у повному 0–255."));
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

            // language first, labelled in English too, so it can be found whatever language is on screen
            var langs = new[] { ("auto", S.T("Automatic (Windows language)", "Автоматично (мова Windows)")) }.Concat(S.Languages).ToList();
            var lang = Ui.Combo(langs.Select(l => l.Item2), Math.Max(0, langs.FindIndex(l => l.Item1 == St.Cfg.Str("lang", "auto"))), null, 300);
            lang.SelectionChanged += (_, _) =>
            {
                St.Cfg.Set("lang", langs[lang.SelectedIndex].Item1); St.Cfg.Save();
                ((App)Application.Current).Reload(langs[lang.SelectedIndex].Item1);
            };
            Body.Children.Add(Ui.Card(Ui.Field(S.Lang == "en" ? "Language" : S.T("Language", "Мова") + " · Language", lang,
                tip: S.T("Interface language. The window reloads in the new language; a running measurement is cancelled.", "Мова інтерфейсу. Вікно перезавантажиться новою мовою; поточний замір буде скасовано."))));
            System.Windows.Automation.AutomationProperties.SetName(_displays, S.T("Display", "Дисплей"));
            Ui.Tip(_displays, S.T("The screen to calibrate. Profiles, patches and DDC/CI all go to this display.", "Екран, який калібруємо. Профілі, патчі й DDC/CI стосуються саме цього дисплея."));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Display", "Дисплей")),
                Ui.Row(_displays, Ui.Btn(S.T("Identify", "Показати номери"), Identify, false, "", tip: S.T("Shows which physical screen this is.", "Показує, який це фізичний екран.")), Ui.Btn(S.T("Refresh", "Оновити"), () => { St.RefreshDisplays(); OnShow(); }, false, "", tip: S.T("Looks for displays and colorimeters again, e.g. after plugging in the sensor or switching HDR.", "Заново шукає дисплеї та колориметри, напр. після підключення датчика чи перемикання HDR."))),
                _info));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Colorimeter", "Колориметр")),
                Ui.Field(S.T("Instrument", "Пристрій"), _instr, tip: S.T("The colorimeter found by ArgyllCMS. If the list is empty, plug it in and press Refresh.", "Колориметр, який знайшов ArgyllCMS. Якщо список порожній, підключіть його й натисніть «Оновити».")),
                Ui.Field(S.T("Display technology", "Технологія дисплея"), _type, S.T("i1Display / ColorMunki: LCD and OLED use non-refresh", "i1Display / ColorMunki: для LCD і OLED — без регенерації"), tip: S.T("Tells the colorimeter how to measure. LCD and OLED: non-refresh. CRT, plasma or a flickering (PWM) backlight: refresh.", "Підказує колориметру, як міряти. LCD і OLED — non-refresh. CRT, плазма чи мерехтливе (ШІМ) підсвічування — refresh.")),
                Ui.Field(S.T("Correction (.ccss / .ccmx)", "Корекція (.ccss / .ccmx)"), Ui.Row(_corr, Ui.Btn("…", PickCorrection, tip: S.T("Choose a .ccss or .ccmx correction file.", "Вибрати файл корекції .ccss або .ccmx."))), S.T("Optional spectral correction for your panel type", "Необов'язкова спектральна корекція під тип панелі"), tip: S.T("Optional spectral correction for the panel's backlight type (WLED, wide-gamut LED, OLED…). Makes a colorimeter more accurate on that panel; leave empty if unsure.", "Необов'язкова спектральна корекція під тип підсвічування панелі (WLED, широкий гамут, OLED…). Робить колориметр точнішим на цій панелі; якщо не впевнені — залиште порожнім.")),
                _video));
            Body.Children.Add(Ui.Card(Ui.H2(S.T("Test patches", "Тестові патчі")),
                Ui.Field(S.T("Show patches in", "Показувати патчі"), _patches,
                    S.T("Own window works full screen or as a window you can drag and resize; OLED/plasma with ABL prefer a window", "Власне вікно: на весь екран або перетягуване вікно; для OLED/плазми з ABL краще вікно"), tip: S.T("Where the test colours appear: our own window (full screen, or a window you can move and resize) or ArgyllCMS's window. A smaller window helps OLED and plasma screens that dim large bright areas.", "Де показуються тестові кольори: у власному вікні програми (на весь екран або у вікні, яке можна рухати й змінювати) чи у вікні ArgyllCMS. Менше вікно допомагає OLED і плазмі, які притемнюють великі яскраві ділянки.")),
                Ui.Btn(S.T("Preview patch window (5 s)", "Показати вікно патчів (5 с)"), PreviewPatches, false, "", tip: S.T("Shows the patch window on the selected display for 5 seconds so you can place the sensor.", "Показує вікно патча на вибраному дисплеї на 5 секунд, щоб ви поставили датчик."))));
            Body.Children.Add(Ui.Card(Ui.H2("ArgyllCMS"),
                Ui.Field(S.T("bin folder", "Папка bin"), Ui.Row(_argyll, Ui.Btn(S.T("Apply", "Застосувати"), () => { St.Cfg.Set("argyll_bin", _argyll.Text.Trim()); St.Cfg.Save(); Argyll.Find(_argyll.Text.Trim()); St.RefreshDisplays(); OnShow(); }, tip: S.T("Use this ArgyllCMS folder and search for displays and instruments again.", "Використати цю теку ArgyllCMS і заново знайти дисплеї та прилади."))), tip: S.T("Folder with the ArgyllCMS tools (dispcal.exe, spotread.exe…). C:\\ArgyllCMS\\bin, Program Files and a DisplayCAL download are found automatically.", "Тека з інструментами ArgyllCMS (dispcal.exe, spotread.exe…). C:\\ArgyllCMS\\bin, Program Files і завантаження DisplayCAL знаходяться автоматично.")),
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
            try { using var x = Ddc.Open(d); var b = x?.Get(Ddc.Brightness); ddc = b == null ? S.T("no", "ні") : S.F("yes · brightness {0}/{1}", "так · яскравість {0}/{1}", b.Value.cur, b.Value.max); }
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
                    async () => { Displays.SetHdr(d, !d.HdrOn); await Task.Delay(6000); St.RefreshDisplays(); OnShow(); }, false, "", tip: S.T("Switches Windows HDR for this display. The monitor may show its own message for a few seconds.", "Перемикає Windows HDR для цього дисплея. Монітор може кілька секунд показувати своє повідомлення."))));
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
