// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Modified 2026 by DMD61 (Victory61): numbered steps, minimise reminder, file tools moved under "not in the list?".
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>
    /// Asks the user to pick a freshly installed HDR profile in Windows Settings. Settings does not always list a new
    /// profile, so the dialog also shows where the file is and can open that folder or copy the path for "Add profile".
    /// </summary>
    public sealed class ProfilePrompt : Window
    {
        public ProfilePrompt(string description, string installedPath)
        {
            Title = "HDRDisplayCalibrate"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.MainWindow; Topmost = true;
            if (S.Rtl) FlowDirection = FlowDirection.RightToLeft;
            var path = new TextBox { Text = installedPath, IsReadOnly = true, MinWidth = 520, Margin = new Thickness(0, 4, 0, 6) };
            var name = new TextBlock { Text = description, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = Ui.Accent, Margin = new Thickness(26, 2, 0, 6) };
            var body = Ui.Stack(
                Ui.H2(S.T("Choose the new profile in Windows", "Виберіть новий профіль у Windows")),
                Line(1, S.T("Windows Settings has opened on System → Display (if not, press the button).", "Параметри Windows відкрилися на Система → Дисплей (якщо ні, натисніть кнопку)."),
                     Ui.Btn(S.T("Open display settings", "Відкрити параметри дисплея"), ColorProfiles.OpenDisplaySettings, false, "\uE713")),
                Line(2, S.T("Under “Color profile” choose:", "У «Колірний профіль» виберіть:")),
                name,
                Line(3, S.T("Minimise Settings and every other open window: the measurement needs a clear screen.", "Згорніть Параметри й усі інші відкриті вікна: для заміру потрібен чистий екран.")),
                Line(4, S.T("Come back here and press OK.", "Поверніться сюди й натисніть OK.")));
            var help = Ui.Caption(S.T("The profile is not in the list? Press “Add profile” in Settings and choose this file:", "Профілю немає у списку? Натисніть у Параметрах «Додати профіль» і виберіть цей файл:"));
            help.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(help);
            body.Children.Add(path);
            body.Children.Add(Ui.Row(
                Ui.Btn(S.T("Show the profile file", "Показати файл профілю"), () => Process.Start("explorer.exe", $"/select,\"{installedPath}\""), false, "\uE8B7",
                       tip: S.T("Opens the folder with the profile file selected.", "Відкриває теку з виділеним файлом профілю.")),
                Ui.Btn(S.T("Copy path", "Копіювати шлях"), () => Clipboard.SetText(installedPath), false, "\uE8C8",
                       tip: S.T("Copies the full path, to paste into the file name box of “Add profile”.", "Копіює повний шлях, щоб вставити його в поле імені файлу у «Додати профіль»."))));
            var ok = Ui.Btn("OK", () => { DialogResult = true; }, true);
            ok.IsDefault = true; ok.HorizontalAlignment = HorizontalAlignment.Right; ok.MinWidth = 100; ok.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(ok);
            body.Margin = new Thickness(24, 20, 24, 20);
            body.MaxWidth = 680;
            Content = body;
        }

        /// <summary>One numbered instruction, with an optional button after the text.</summary>
        static UIElement Line(int n, string text, UIElement extra = null)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = n + ".", Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
            var t = Ui.Stack(Ui.Text(text), extra);
            if (extra is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetColumn(t, 1); g.Children.Add(t);
            return g;
        }
    }
}
