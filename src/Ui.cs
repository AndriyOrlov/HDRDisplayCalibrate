// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HDRDisplayCalibrate
{
    /// <summary>Small helpers to build Fluent-styled pages in code.</summary>
    public static class Ui
    {
        public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
        public static readonly Brush Warm = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3D));
        public static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
        public static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        public static readonly Brush CardBg = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
        public static readonly Brush CardStroke = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static TextBlock H1(string t) => new() { Text = t, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        public static TextBlock H2(string t) => new() { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        public static TextBlock Caption(string t) => new() { Text = t, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        public static TextBlock Text(string t, double size = 13) => new() { Text = t, FontSize = size, TextWrapping = TextWrapping.Wrap };

        public static Border Card(params UIElement[] children)
        {
            var sp = new StackPanel();
            foreach (var c in children) if (c != null) sp.Children.Add(c);
            return new Border { Background = CardBg, BorderBrush = CardStroke, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = sp };
        }

        public static StackPanel Stack(params UIElement[] c) { var s = new StackPanel(); foreach (var x in c) if (x != null) s.Children.Add(x); return s; }
        public static StackPanel Row(params UIElement[] c)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            foreach (var x in c) if (x != null) { if (x is FrameworkElement fe && fe.Margin == default) fe.Margin = new Thickness(0, 0, 8, 0); s.Children.Add(x); }
            return s;
        }

        /// <summary>Label on the left, control on the right.</summary>
        /// <summary>Attaches a wrapped, long-lived tooltip.</summary>
        public static T Tip<T>(T el, string tip) where T : FrameworkElement
        {
            if (string.IsNullOrEmpty(tip)) return el;
            el.ToolTip = new ToolTip { Content = new TextBlock { Text = tip, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } };
            ToolTipService.SetShowDuration(el, 60000);
            ToolTipService.SetInitialShowDelay(el, 400);
            ToolTipService.SetShowOnDisabled(el, true);
            return el;
        }

        public static Grid Field(string label, UIElement control, string hint = null, double labelWidth = 220, string tip = null)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var l = Stack(Text(label), hint == null ? null : Caption(hint)); l.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(l); Grid.SetColumn(control, 1); g.Children.Add(control);
            if (control is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; if (fe.ToolTip == null) Tip(fe, tip); }
            // screen readers and UI automation see the field's label as the control's name
            if (string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(control))) System.Windows.Automation.AutomationProperties.SetName(control, label);
            Tip(l, tip);
            return g;
        }

        public static Button Btn(string text, Action click, bool accent = false, string icon = null, string tip = null)
        {
            var content = icon == null ? (object)text : Row(new TextBlock { Text = icon, FontFamily = Icons, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) });
            if (content is StackPanel sp) sp.Margin = new Thickness(0);
            var b = new Button { Content = content, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
            if (accent) b.Style = (Style)Application.Current.TryFindResource("AccentButtonStyle") ?? b.Style;
            b.Click += (_, _) => click();
            System.Windows.Automation.AutomationProperties.SetName(b, text);      // content is icon + text: give it a readable name
            return Tip(b, tip);
        }

        public static ComboBox Combo(IEnumerable<string> items, int selected = 0, Action<int> changed = null, double width = 260)
        {
            var c = new ComboBox { MinWidth = width, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedIndex = Math.Min(selected, c.Items.Count - 1);
            if (changed != null) c.SelectionChanged += (_, _) => changed(c.SelectedIndex);
            return c;
        }

        public static TextBox Box(string text, double width = 120) => new() { Text = text, MinWidth = width, HorizontalAlignment = HorizontalAlignment.Left };
        public static CheckBox Check(string text, bool on = false, string tip = null) => Tip(new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(0, 4, 0, 4) }, tip);

        /// <summary>Simple table: header row plus rows of strings.</summary>
        public static Grid Table(string[] header, IEnumerable<string[]> rows, double[] widths = null)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            for (int c = 0; c < header.Length; c++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = widths != null && c < widths.Length ? new GridLength(widths[c]) : new GridLength(1, GridUnitType.Star) });
            int r = 0;
            void Add(string[] cells, bool head)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < cells.Length && c < header.Length; c++)
                {
                    var t = new TextBlock { Text = cells[c], Margin = new Thickness(0, 3, 12, 3), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
                    if (head) t.Foreground = Muted;
                    Grid.SetRow(t, r); Grid.SetColumn(t, c); g.Children.Add(t);
                }
                r++;
            }
            Add(header, true);
            foreach (var row in rows) Add(row, false);
            return g;
        }

        public static double Num(TextBox b, double def)
        {
            var t = b.Text.Trim().Replace(',', '.');
            return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        public static string F(double v, int d = 2) => v.ToString("F" + d, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Base class for pages: a scrollable column with a title.</summary>
    public abstract class Page : UserControl
    {
        protected readonly StackPanel Body = new() { Margin = new Thickness(32, 24, 32, 32), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        protected static AppState St => AppState.I;
        protected Page(string title, string subtitle)
        {
            Body.Children.Add(Ui.H1(title));
            if (subtitle != null) { var c = Ui.Caption(subtitle); c.Margin = new Thickness(0, 0, 0, 16); c.FontSize = 13; Body.Children.Add(c); }
            Content = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        public virtual void OnShow() { }

        /// <summary>Runs a measurement job with status, cancellation and error reporting.</summary>
        protected async void RunJob(Func<System.Threading.CancellationToken, System.Threading.Tasks.Task> work)
        {
            System.Threading.CancellationToken ct;
            try { ct = St.BeginJob(); } catch (Exception e) { MessageBox.Show(e.Message); return; }
            try { await work(ct); }
            catch (OperationCanceledException) { St.Report(S.T("Cancelled", "Скасовано"), 0); }
            catch (Exception e) { St.Report(S.T("Error: ", "Помилка: ") + e.Message, 0); Core.Log.Write("ERROR " + e); MessageBox.Show(e.Message, "HDRDisplayCalibrate"); }
            finally { St.EndJob(); }
        }
    }
}
