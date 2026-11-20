// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Added 2026 by DMD61 (Victory61): name of the final HDR profile and the closing window of the HDR wizard
// (final profile active, sensor off, SDR slider back to its previous value, test profiles deleted).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>
    /// Asks for the name of the final profile (the proposed name can be kept with one click), and where and in which
    /// format the report with values and charts is saved at the end. Folder and format are remembered.
    /// </summary>
    public sealed class ProfileNameDialog : Window
    {
        readonly TextBox _box;
        readonly TextBox _dir;
        readonly RadioButton _pdf, _html;
        public string Result { get; private set; }

        public static (string name, string reportDir, bool html) Ask(string proposed)
        {
            var w = new ProfileNameDialog(proposed);
            bool ok = w.ShowDialog() == true;
            var cfg = AppState.I.Cfg;
            string dir = ok ? w._dir.Text.Trim() : cfg.Str("report_dir", Paths.Docs);
            bool html = ok ? w._html.IsChecked == true : cfg.Str("report_format", "pdf") == "html";
            if (string.IsNullOrEmpty(dir)) dir = Paths.Docs;
            return (ok ? w.Result : proposed, dir, html);
        }

        ProfileNameDialog(string proposed)
        {
            Title = "HDRDisplayCalibrate"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.MainWindow; Topmost = true;
            if (S.Rtl) FlowDirection = FlowDirection.RightToLeft;
            var cfg = AppState.I.Cfg;
            _box = new TextBox { Text = proposed, MinWidth = 460, Margin = new Thickness(0, 8, 0, 4) };
            _dir = new TextBox { Text = cfg.Str("report_dir", Paths.Docs), MinWidth = 400, IsReadOnly = true, VerticalAlignment = VerticalAlignment.Center };
            bool html = cfg.Str("report_format", "pdf") == "html";
            _pdf = new RadioButton { Content = "PDF", GroupName = "fmt", IsChecked = !html, Margin = new Thickness(0, 0, 20, 0) };
            _html = new RadioButton { Content = "HTML", GroupName = "fmt", IsChecked = html };
            var ok = Ui.Btn(S.T("Confirm", "Підтвердити"), Confirm, true);
            ok.IsDefault = true; ok.MinWidth = 120; ok.HorizontalAlignment = HorizontalAlignment.Right; ok.Margin = new Thickness(0, 14, 0, 0);
            var report = Ui.H2(S.T("Report with values and charts", "Звіт зі значеннями й графіками")); report.Margin = new Thickness(0, 16, 0, 2);
            var body = Ui.Stack(
                Ui.H2(S.T("Name of the final profile", "Назва фінального профілю")),
                Ui.Text(S.T("This is the name you will see in Windows under “Color profile”. Keep the proposed name or type another one, then press Confirm.",
                            "Цю назву ви побачите у Windows у «Колірний профіль». Залиште запропоновану або введіть іншу й натисніть «Підтвердити».")),
                _box,
                Ui.Caption(S.T("Use underscores (_) instead of spaces: some programs, such as command-line tools, do not handle names with spaces well. Spaces are turned into underscores automatically.",
                               "Використовуйте підкреслення (_) замість пробілів: деякі програми, напр. інструменти командного рядка, погано працюють з назвами з пробілами. Пробіли автоматично замінюються на підкреслення.")),
                report,
                Ui.Text(S.T("At the end of the calibration it is saved automatically in this folder:", "Наприкінці калібрування він автоматично зберігається в цю теку:")),
                Ui.Row(_dir, Ui.Btn(S.T("Change folder…", "Змінити теку…"), ChangeFolder, false, "\uE8B7")),
                Ui.Row(Ui.Text(S.T("Format:", "Формат:")), _pdf, _html),
                ok);
            body.Margin = new Thickness(24, 20, 24, 20); body.MaxWidth = 640;
            Content = body;
            Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
        }

        void ChangeFolder()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = S.T("Folder for the report", "Тека для звіту") };
            if (Directory.Exists(_dir.Text)) dlg.InitialDirectory = _dir.Text;
            if (dlg.ShowDialog(this) == true) _dir.Text = dlg.FolderName;
        }

        void Confirm()
        {
            var bad = Path.GetInvalidFileNameChars();
            var n = new string(_box.Text.Trim().Select(c => bad.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray());
            if (n.EndsWith(".icc", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
            if (n.Length == 0) return;
            Result = n;
            var cfg = AppState.I.Cfg;
            cfg.Set("report_dir", _dir.Text.Trim()); cfg.Set("report_format", _html.IsChecked == true ? "html" : "pdf");
            try { cfg.Save(); } catch { }
            DialogResult = true;
        }
    }

    /// <summary>
    /// Closing window of the HDR wizard: the final profile is already active, the sensor can be removed, Windows HDR
    /// settings are opened so "SDR content brightness" can go back to its previous value (a tick shows when it is),
    /// and the test profiles of this calibration can be deleted.
    /// </summary>
    public sealed class FinishDialog : Window
    {
        readonly DisplayTarget _d;
        readonly double? _before;
        readonly TextBlock _sdrState = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

        /// <summary>Slider position for a reference white: 0 = 80 nits, each step 4 nits more (100 = 480 nits).</summary>
        public static int Slider(double nits) => (int)Math.Round(Math.Clamp((nits - 80) / 4, 0, 100));

        public static void Run(DisplayTarget d, string finalName, double? sdrBefore, List<string> testProfiles, string report, string workDir) =>
            new FinishDialog(d, finalName, sdrBefore, testProfiles, report, workDir).ShowDialog();

        FinishDialog(DisplayTarget d, string finalName, double? sdrBefore, List<string> testProfiles, string report, string workDir)
        {
            _d = d; _before = sdrBefore;
            Title = "HDRDisplayCalibrate"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.MainWindow; Topmost = false;   // Settings must be able to come on top of it
            if (S.Rtl) FlowDirection = FlowDirection.RightToLeft;

            var del = Ui.Check(S.F("Delete the test profiles of this calibration ({0})", "Видалити тестові профілі цього калібрування ({0})",
                                   string.Join(", ", testProfiles.Select(ShortTag))), testProfiles.Count > 0);
            del.IsEnabled = testProfiles.Count > 0;
            bool hasDir = !string.IsNullOrEmpty(workDir) && Directory.Exists(workDir);
            var clean = Ui.Check(S.T("Delete the work files of this calibration (measurements and profile copies)", "Видалити робочі файли цього калібрування (заміри й копії профілів)"), hasDir);
            clean.IsEnabled = hasDir;
            var ok = Ui.Btn("OK", () =>
            {
                if (del.IsChecked == true) DeleteTests(testProfiles);
                if (clean.IsChecked == true && hasDir)
                    try { Directory.Delete(workDir, true); Log.Write("work folder deleted: " + workDir); }
                    catch (Exception e) { Log.Write("work folder not deleted: " + e.Message); MessageBox.Show(S.T("The work folder could not be deleted:", "Робочу теку не вдалося видалити:") + "\n" + workDir, "HDRDisplayCalibrate"); }
                DialogResult = true;
            }, true);
            ok.IsDefault = true; ok.MinWidth = 120; ok.HorizontalAlignment = HorizontalAlignment.Right; ok.Margin = new Thickness(0, 14, 0, 0);

            var body = Ui.Stack(
                Ui.H2(S.T("HDR calibration complete", "HDR калібрування завершено")),
                Ui.Text(S.F("The final profile “{0}” is already active: you do not have to do anything else.", "Фінальний профіль «{0}» уже активний: більше нічого робити не треба.", finalName)),
                report == null ? null : Ui.Caption(S.F("The report with values and charts has been saved here: {0}", "Звіт зі значеннями й графіками збережено тут: {0}", report)),
                Section(S.T("1. Sensor", "1. Датчик")),
                Ui.Text(S.T("You can remove the sensor. Turn the lens cover back over the lens to protect it.", "Датчик можна зняти. Поверніть кришку назад на лінзу, щоб захистити її.")),
                Section(S.T("2. SDR content brightness", "2. Яскравість SDR-вмісту")),
                Ui.Text(_before is double b
                    ? S.F("Windows HDR settings are open: move “SDR content brightness” back to its previous value: {0} ({1:F0} nits).", "Параметри HDR Windows відкрито: поверніть «Яскравість SDR-вмісту» на попереднє значення: {0} ({1:F0} ніт).", Slider(b), b)
                    : S.T("Windows HDR settings are open: move “SDR content brightness” back to the value you use for the desktop.", "Параметри HDR Windows відкрито: поверніть «Яскравість SDR-вмісту» на значення, яке ви використовуєте для робочого столу.")),
                _sdrState,
                Ui.Row(Ui.Btn(S.T("Open HDR settings", "Відкрити параметри HDR"), ColorProfiles.OpenHdrSettings, false, "\uE713")),
                Note(S.T("If in your screenshots the highlights look altered compared with the screen (burnt whites, no detail), lower this value a little and try again.",
                         "Якщо на знімках екрана світла виглядають зміненими порівняно з екраном (пересвічений білий, без деталей), трохи зменште це значення й спробуйте ще раз.")),
                Section(S.T("3. Clean-up", "3. Прибирання")),
                del, clean,
                ManualHelp(),
                Section(S.T("4. Check by eye (optional)", "4. Перевірка на око (за бажанням)")),
                Ui.Text(S.T("If you wish, you can check the result by eye in the “Test patterns” section: shades, neutral greys, shadows, highlights and colours.",
                            "За бажанням результат можна перевірити на око в розділі «Тестові шаблони»: переходи, нейтральний сірий, тіні, світла й кольори.")),
                Section(S.T("5. Switching from HDR to SDR", "5. Перехід з HDR на SDR")),
                Note(HdrOffFix.SwitchNote),   // DMD61
                ok);
            body.Margin = new Thickness(24, 20, 24, 20); body.MaxWidth = 680;
            Content = body;

            _timer.Tick += (_, _) => UpdateSdr();
            Loaded += (_, _) => { UpdateSdr(); _timer.Start(); try { ColorProfiles.OpenHdrSettings(); } catch { } };
            Closed += (_, _) => _timer.Stop();
        }

        /// <summary>Reminder for removing colour profiles by hand, with buttons that open the two places.</summary>
        static UIElement ManualHelp()
        {
            var t = Ui.Caption(S.T("To remove colour profiles by hand later: press Win+R, type colorcpl and press Enter; in Color Management, on the “All Profiles” tab, select the profile and press “Remove”. The profile files are in C:\\Windows\\System32\\spool\\drivers\\color.",
                                   "Щоб пізніше видалити колірні профілі вручну: натисніть Win+R, введіть colorcpl і натисніть Enter; у «Керування кольором» на вкладці «Усі профілі» виберіть профіль і натисніть «Видалити». Файли профілів лежать у C:\\Windows\\System32\\spool\\drivers\\color."));
            t.Margin = new Thickness(0, 10, 0, 0);
            return Ui.Stack(t, Ui.Row(
                Ui.Btn(S.T("Open Color Management", "Відкрити «Керування кольором»"), () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("colorcpl.exe") { UseShellExecute = true }); } catch { } }, false, "\uE790"),
                Ui.Btn(S.T("Open the profiles folder", "Відкрити теку профілів"), () => { try { System.Diagnostics.Process.Start("explorer.exe", ColorProfiles.ColorDir); } catch { } }, false, "\uE8B7")));
        }

        static string ShortTag(string file)
        {
            foreach (var t in new[] { "neutral", "probe", "v1", "v2" }) if (file.Contains("_" + t + "_")) return t;
            return Path.GetFileNameWithoutExtension(file);
        }

        static TextBlock Section(string t) { var h = Ui.H2(t); h.Margin = new Thickness(0, 14, 0, 2); return h; }

        static UIElement Note(string text) => new Border
        {
            BorderBrush = Ui.Warm, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 8, 0, 0),
            Child = Ui.Text(text)
        };

        void UpdateSdr()
        {
            try { Displays.RefreshColor(_d); } catch { return; }
            double now = _d.SdrWhiteNits;
            if (_before is double b && Slider(now) == Slider(b))
            { _sdrState.Text = "✓ " + S.F("The slider is back at {0}.", "Повзунок знову на {0}.", Slider(b)); _sdrState.Foreground = Ui.Good; }
            else
            { _sdrState.Text = S.F("Now: {0} ({1:F0} nits)", "Зараз: {0} ({1:F0} ніт)", Slider(now), now); _sdrState.Foreground = Ui.Warm; }
        }

        void DeleteTests(List<string> names)
        {
            var left = names.Where(n => !ColorProfiles.Uninstall(_d, n, true)).ToList();
            if (left.Count > 0)
                MessageBox.Show(S.T("These test profiles could not be deleted. You can delete them by hand from C:\\Windows\\System32\\spool\\drivers\\color:", "Ці тестові профілі не вдалося видалити. Їх можна видалити вручну з C:\\Windows\\System32\\spool\\drivers\\color:")
                                + "\n\n" + string.Join("\n", left), "HDRDisplayCalibrate");
        }
    }
}
