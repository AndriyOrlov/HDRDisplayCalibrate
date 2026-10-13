// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Globalization;
using System.Windows.Controls;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>White point selector shared by pages: preset, CCT, x/y or native.</summary>
    public sealed class WhitePicker
    {
        public readonly ComboBox Combo;
        public readonly TextBox Value = Ui.Box("6500", 140);
        static readonly string[] Names =
        {
            "D65 (sRGB / Rec.709 / Rec.2020)", "D50", "DCI-P3 (0.314, 0.351)",
            S.T("Colour temperature, K", "Колірна температура, K"), S.T("Custom x, y", "Свої x, y"), S.T("Native (no change)", "Рідна (без змін)"),
        };
        public WhitePicker(int selected = 0) { Combo = Ui.Combo(Names, selected, i => Value.IsEnabled = i is 3 or 4, 300); Value.IsEnabled = selected is 3 or 4; }

        public (double x, double y)? Xy()
        {
            switch (Combo.SelectedIndex)
            {
                case 0: return (0.3127, 0.3290);
                case 1: return (0.3457, 0.3585);
                case 2: return (0.314, 0.351);
                case 3: return Cm.DaylightXy(Ui.Num(Value, 6500));
                case 4:
                    var p = Value.Text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    return p.Length == 2 ? (double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture)) : (0.3127, 0.3290);
                default: return null;
            }
        }

        public System.Windows.UIElement View => Ui.Row(Combo, Value);
    }
}
