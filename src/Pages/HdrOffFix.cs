// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Added 2026 by DMD61 (Victory61): after Windows HDR is turned off, Windows keeps the HDR (MHC2) colour correction loaded
// in the Desktop Window Manager until it restarts (sign-out), so SDR colours look wrong. The app restarts DWM by itself.
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate.Pages
{
    public static class HdrOffFix
    {
        /// <summary>Shown at the end of the HDR calibration: what to do when HDR is later switched off by hand.</summary>
        public static string SwitchNote => S.T("When you later switch Windows HDR off with the Windows switch, sign out of Windows and back in: Windows keeps the colour correction of the HDR profile active also in SDR, and until then the SDR colours look wrong (washed out or solarised). From SDR to HDR nothing is needed.",
            "Коли пізніше вимикатимете HDR перемикачем Windows, вийдіть із Windows і увійдіть знову: Windows залишає корекцію кольору HDR-профілю активною й у SDR, і доти кольори SDR виглядають неправильно (вицвілі або «соляризовані»). З SDR на HDR нічого робити не треба.");

        /// <summary>True when the HDR profile is a calibration (not the Windows default), i.e. something can stay stuck.</summary>
        public static bool Needed(string hdrProfile) =>
            !string.IsNullOrEmpty(hdrProfile) && !hdrProfile.StartsWith("sRGB", StringComparison.OrdinalIgnoreCase);

        /// <summary>Restarts DWM through an elevated copy of this app ("--restart-dwm"); falls back to a sign-out message.</summary>
        public static async Task Run(string hdrProfile)
        {
            if (!Needed(hdrProfile)) return;
            St.Report(S.T("Clearing the HDR colour correction that Windows keeps after HDR is turned off: the screen goes black for a moment…", "Скидаю HDR-корекцію кольору, яку Windows залишає після вимкнення HDR: екран на мить стане чорним…"));
            bool ok = await Task.Run(() =>
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath, "--restart-dwm") { UseShellExecute = true, Verb = "runas" });
                    p?.WaitForExit(20000);
                    return true;
                }
                catch (Exception e) { Log.Write("restart DWM: " + e.Message); return false; }
            });
            if (ok) { await Task.Delay(4000); St.Report(S.T("SDR colours restored", "Кольори SDR відновлено"), 1); return; }
            MessageBox.Show(S.T("Windows keeps the HDR colour correction active after HDR is turned off, so the SDR colours are wrong until it is cleared, and an SDR calibration would come out wrong.\n\nSign out of Windows and back in, then come back to the SDR calibration.", "Windows залишає HDR-корекцію кольору активною після вимкнення HDR, тож кольори SDR неправильні, доки її не скинуто, і SDR-калібрування вийшло б неправильним.\n\nВийдіть із Windows і увійдіть знову, потім поверніться до SDR-калібрування."),
                "HDRDisplayCalibrate", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>Called by "--restart-dwm" (elevated): Windows restarts DWM by itself, open windows stay.</summary>
        public static void KillDwm()
        {
            foreach (var p in Process.GetProcessesByName("dwm"))
                try { p.Kill(); } catch (Exception e) { Log.Write("dwm: " + e.Message); }
        }
    }
}
