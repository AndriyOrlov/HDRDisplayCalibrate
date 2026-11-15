// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Added 2026 by DMD61 (Victory61): guided sensor dialogs (place / check / remove), modelled on the
// step-by-step prompts of commercial calibration software, so the HDR wizard can be followed without a manual.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>
    /// Modal dialogs that tell the user when and how to put the colorimeter on the screen,
    /// remind them not to move it between steps, and tell them when it can be removed.
    /// </summary>
    public sealed class ProbeGuide : Window
    {
        public enum Mode { Place, Check, Remove }

        /// <summary>Shows the dialog for the given moment; returns false if the user cancelled.</summary>
        public static bool Ask(Mode mode, string extra = null)
        {
            var w = new ProbeGuide(mode, extra);
            return w.ShowDialog() == true;
        }

        ProbeGuide(Mode mode, string extra)
        {
            Title = "HDRDisplayCalibrate"; SizeToContent = SizeToContent.WidthAndHeight; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Owner = Application.Current.MainWindow; Topmost = true;
            if (S.Rtl) FlowDirection = FlowDirection.RightToLeft;

            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20), MaxWidth = 680 };
            Button ok, cancel = null;

            if (mode == Mode.Place)
            {
                body.Children.Add(Ui.H2(S.T("Place the sensor on the screen", "Поставте датчик на екран")));
                body.Children.Add(Diagram());
                body.Children.Add(Numbered(
                    S.T("Tilt the screen slightly back.", "Злегка відхиліть екран назад."),
                    S.T("Open the diffuser (turn it aside): the sensor must be uncovered to measure a screen.", "Відкрийте розсіювач (відверніть убік): щоб міряти екран, датчик має бути відкритим."),
                    S.T("Put the sensor flat against the glass in the centre of the screen.", "Притуліть датчик до скла по центру екрана."),
                    S.T("Hang the cable over the top edge with the counterweight behind the monitor, so the sensor stays still.", "Перекиньте кабель через верхній край, противагу — за монітор, щоб датчик не рухався."),
                    S.T("Leave it there until the app tells you it can be removed.", "Залиште його там, доки програма не скаже, що його можна зняти.")));
                body.Children.Add(Note(S.T("During each measurement the screen turns black, grey, white and then coloured for a few minutes. That is normal: do not touch the mouse, keyboard or sensor until the step is done.",
                                           "Під час кожного заміру екран на кілька хвилин стає чорним, сірим, білим, а потім кольоровим. Це нормально: не торкайтеся миші, клавіатури й датчика, доки крок не завершиться.")));
                ok = Ui.Btn(S.T("Sensor in place — start", "Датчик на місці — почати"), () => { DialogResult = true; }, true);
                cancel = Ui.Btn(S.T("Cancel", "Скасувати"), () => { DialogResult = false; });
            }
            else if (mode == Mode.Check)
            {
                body.Children.Add(Ui.H2(S.T("Is the sensor still in the centre of the screen?", "Датчик досі по центру екрана?")));
                body.Children.Add(Diagram());
                body.Children.Add(Ui.Text(S.T("Do not move it between steps: every step must be measured at the same spot. Keep the Settings window away from the centre.",
                                              "Не рухайте його між кроками: усі кроки треба міряти в тому самому місці. Тримайте вікно Параметрів подалі від центру.")));
                body.Children.Add(Note(S.T("The measurement takes a few minutes: do not touch anything until the step is done.",
                                           "Замір триває кілька хвилин: нічого не торкайтеся, доки крок не завершиться.")));
                ok = Ui.Btn(S.T("Yes, continue", "Так, продовжити"), () => { DialogResult = true; }, true);
                cancel = Ui.Btn(S.T("Cancel", "Скасувати"), () => { DialogResult = false; });
            }
            else
            {
                body.Children.Add(Ui.H2(S.T("Measurement finished — you can remove the sensor", "Замір завершено — датчик можна зняти")));
                body.Children.Add(Ui.Text(S.T("Close the diffuser to protect the sensor.", "Закрийте розсіювач, щоб захистити датчик.")));
                if (!string.IsNullOrEmpty(extra)) body.Children.Add(Note(extra));
                ok = Ui.Btn("OK", () => { DialogResult = true; }, true);
            }

            ok.IsDefault = true; ok.MinWidth = 120;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            if (cancel != null) { cancel.IsCancel = true; cancel.MinWidth = 100; cancel.Margin = new Thickness(0, 0, 8, 0); buttons.Children.Add(cancel); }
            ok.Margin = new Thickness(0);
            buttons.Children.Add(ok);
            body.Children.Add(buttons);
            Content = body;
        }

        static UIElement Numbered(params string[] lines)
        {
            var list = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            for (int i = 0; i < lines.Length; i++)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(new TextBlock { Text = (i + 1) + ".", Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
                var t = Ui.Text(lines[i]); Grid.SetColumn(t, 1); g.Children.Add(t);
                list.Children.Add(g);
            }
            return list;
        }

        static UIElement Note(string text) => new Border
        {
            BorderBrush = Ui.Warm, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 8, 0, 0),
            Child = Ui.Text(text)
        };

        /// <summary>Front and side view: sensor in the centre, diffuser open, cable over the top, counterweight behind.</summary>
        static UIElement Diagram()
        {
            var c = new Canvas { Width = 640, Height = 225, Margin = new Thickness(0, 6, 0, 10), HorizontalAlignment = HorizontalAlignment.Left };
            void At(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }
            TextBlock L(string t, Brush b) => new() { Text = t, FontSize = 12, Foreground = b };

            // front view
            At(L(S.T("Front", "Спереду"), Ui.Muted), 105, 0);
            At(new Rectangle { Width = 240, Height = 150, Stroke = Ui.Muted, StrokeThickness = 3, RadiusX = 4, RadiusY = 4 }, 10, 20);
            At(new Rectangle { Width = 8, Height = 30, Fill = Ui.Muted }, 126, 170);
            At(new Rectangle { Width = 70, Height = 6, Fill = Ui.Muted }, 95, 200);
            c.Children.Add(new Line { X1 = 130, Y1 = 20, X2 = 130, Y2 = 79, Stroke = Ui.Accent, StrokeThickness = 2 });
            At(new Ellipse { Width = 34, Height = 34, Fill = Brushes.Black, Stroke = Ui.Accent, StrokeThickness = 3 }, 113, 78);
            At(L(S.T("sensor in the centre", "датчик по центру"), Ui.Warm), 152, 87);
            At(L(S.T("cable over the top", "кабель через верх"), Ui.Muted), 136, 30);

            // side view (screen leaning back: the top is further right than the bottom)
            At(L(S.T("Side", "Збоку"), Ui.Muted), 440, 0);
            c.Children.Add(new Line { X1 = 470, Y1 = 22, X2 = 455, Y2 = 170, Stroke = Ui.Muted, StrokeThickness = 8 });
            c.Children.Add(new Line { X1 = 456, Y1 = 170, X2 = 466, Y2 = 200, Stroke = Ui.Muted, StrokeThickness = 5 });
            At(new Rectangle { Width = 60, Height = 6, Fill = Ui.Muted }, 436, 200);
            At(new Rectangle { Width = 22, Height = 22, Fill = Brushes.Black, Stroke = Ui.Accent, StrokeThickness = 2 }, 437, 84);
            c.Children.Add(new Line { X1 = 448, Y1 = 84, X2 = 448, Y2 = 12, Stroke = Ui.Accent, StrokeThickness = 2 });
            c.Children.Add(new Line { X1 = 448, Y1 = 12, X2 = 505, Y2 = 12, Stroke = Ui.Accent, StrokeThickness = 2 });
            c.Children.Add(new Line { X1 = 505, Y1 = 12, X2 = 505, Y2 = 110, Stroke = Ui.Accent, StrokeThickness = 2 });
            At(new Rectangle { Width = 16, Height = 26, Fill = Ui.Muted }, 497, 110);
            c.Children.Add(new Line { X1 = 437, Y1 = 106, X2 = 417, Y2 = 124, Stroke = Ui.Warm, StrokeThickness = 4 });
            At(L(S.T("diffuser open", "розсіювач відкритий"), Ui.Warm), 300, 122);
            At(L(S.T("counterweight behind", "противага позаду"), Ui.Muted), 520, 115);
            return c;
        }
    }
}
