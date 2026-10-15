// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
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
            var path = new TextBox { Text = installedPath, IsReadOnly = true, MinWidth = 520, Margin = new Thickness(0, 4, 0, 12) };
            var body = Ui.Stack(
                Ui.Text(S.T("In Settings → System → Display → (this monitor) → Color profile, choose:", "У Параметри → Система → Дисплей → (цей монітор) → Колірний профіль виберіть:")),
                new TextBlock { Text = description, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(16, 8, 0, 12) },
                Ui.Caption(S.T("Not in the list? Press “Add profile” there and choose this file:", "Немає у списку? Натисніть там «Додати профіль» і виберіть цей файл:")),
                path,
                Ui.Row(
                    Ui.Btn(S.T("Show the profile file", "Показати файл профілю"), () => Process.Start("explorer.exe", $"/select,\"{installedPath}\""), false, "",
                           tip: S.T("Opens the folder with the profile file selected.", "Відкриває теку з виділеним файлом профілю.")),
                    Ui.Btn(S.T("Copy path", "Копіювати шлях"), () => Clipboard.SetText(installedPath), false, "",
                           tip: S.T("Copies the full path, to paste into the file name box of “Add profile”.", "Копіює повний шлях, щоб вставити його в поле імені файлу у «Додати профіль».")),
                    Ui.Btn(S.T("Open display settings", "Відкрити параметри дисплея"), ColorProfiles.OpenDisplaySettings, false, "")),
                Ui.Caption(S.T("Then move the Settings window off the patch area and press OK.", "Потім відсуньте вікно Параметрів від патча і натисніть OK.")));
            var ok = Ui.Btn("OK", () => { DialogResult = true; }, true);
            ok.IsDefault = true; ok.HorizontalAlignment = HorizontalAlignment.Right; ok.MinWidth = 100; ok.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(ok);
            body.Margin = new Thickness(24, 20, 24, 20);
            Content = body;
        }
    }
}
