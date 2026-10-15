// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>
    /// Loads every display's calibration curves (vcgt of its default SDR profile) into the video card at sign-in.
    /// Windows has its own loader (the "Calibration Loader" scheduled task), but other calibration tools
    /// (calibrite PROFILER, for one) switch it off and bring their own; without any loader the calibration
    /// is lost after every restart.
    /// </summary>
    public static class LutLoader
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "HDRDisplayCalibrate LUT loader";
        const string Task = @"\Microsoft\Windows\WindowsColorSystem\Calibration Loader";
        public const string EnableTaskCommand = "schtasks /Change /TN \"" + Task + "\" /ENABLE";

        public static bool Enabled
        {
            get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(ValueName) != null; }
            set
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --load-luts");
                else k.DeleteValue(ValueName, false);
            }
        }

        /// <summary>True when Windows' own calibration loader task exists and is disabled.</summary>
        public static bool WindowsLoaderDisabled()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks", $"/Query /TN \"{Task}\" /FO CSV /NH") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
                return o.Contains("Disabled", StringComparison.OrdinalIgnoreCase) || o.Contains("Вимкнено", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void LoadAll()
        {
            foreach (var d in Displays.List())
            {
                if (d.ArgyllNumber is not int n) continue;
                var name = ColorProfiles.Default(d, false);
                if (string.IsNullOrEmpty(name)) continue;
                var path = Path.Combine(ColorProfiles.ColorDir, name);
                if (!File.Exists(path)) continue;
                Argyll.LutLoad(n, path);
                Log.Write($"loader: {d.Name} ← {name}");
            }
        }
    }
}
