// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>Windows colour profile associations per display (mscms "ColorProfile*Display*" API, current user).</summary>
    public static class ColorProfiles
    {
        const int UserScope = 1, IccType = 0, SdrSub = 7, HdrSub = 8;   // CPST_STANDARD / EXTENDED_DISPLAY_COLOR_MODE
        public static string ColorDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\drivers\color");

        [DllImport("mscms.dll", CharSet = CharSet.Unicode)] static extern bool InstallColorProfileW(string machine, string path);
        [DllImport("mscms.dll", CharSet = CharSet.Unicode)] static extern int ColorProfileAddDisplayAssociation(int scope, string profile, Native.LUID adapter, uint sourceId, bool setDefault, bool advancedColor);
        [DllImport("mscms.dll", CharSet = CharSet.Unicode)] static extern int ColorProfileRemoveDisplayAssociation(int scope, string profile, Native.LUID adapter, uint sourceId, bool advancedColor);
        [DllImport("mscms.dll", CharSet = CharSet.Unicode)] static extern int ColorProfileSetDisplayDefaultAssociation(int scope, string profile, int type, int subType, Native.LUID adapter, uint sourceId);
        [DllImport("mscms.dll", CharSet = CharSet.Unicode)] static extern int ColorProfileGetDisplayDefault(int scope, Native.LUID adapter, uint sourceId, int type, int subType, out IntPtr name);

        /// <summary>Copies the profile into the system colour folder and makes it the display's default (SDR or HDR list).</summary>
        public static string Install(DisplayTarget d, string iccPath, bool hdr)
        {
            if (!InstallColorProfileW(null, iccPath)) throw new IOException("InstallColorProfile failed: " + Marshal.GetLastWin32Error());
            var name = Path.GetFileName(iccPath);
            int hr = ColorProfileAddDisplayAssociation(UserScope, name, d.Adapter, d.SourceId, true, hdr);
            if (hr != 0) throw new IOException($"associate failed 0x{hr:X8}");
            ColorProfileSetDisplayDefaultAssociation(UserScope, name, IccType, hdr ? HdrSub : SdrSub, d.Adapter, d.SourceId);
            Log.Write($"profile {name} → {d.Name} ({(hdr ? "HDR" : "SDR")})");
            return name;
        }

        public static void SetDefault(DisplayTarget d, string name, bool hdr) =>
            ColorProfileSetDisplayDefaultAssociation(UserScope, name, IccType, hdr ? HdrSub : SdrSub, d.Adapter, d.SourceId);

        public static void Remove(DisplayTarget d, string name, bool hdr) =>
            ColorProfileRemoveDisplayAssociation(UserScope, name, d.Adapter, d.SourceId, hdr);

        public static string Default(DisplayTarget d, bool hdr) =>
            ColorProfileGetDisplayDefault(UserScope, d.Adapter, d.SourceId, IccType, hdr ? HdrSub : SdrSub, out IntPtr n) == 0 && n != IntPtr.Zero
                ? Marshal.PtrToStringUni(n) : null;

        /// <summary>Associated profile names (registry, same list the Settings app shows).</summary>
        public static List<string> Associated(DisplayTarget d, bool hdr)
        {
            var res = new List<string>();
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\{4d36e96e-e325-11ce-bfc1-08002be10318}");
                if (root == null) return res;
                foreach (var sub in root.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(sub);
                    if (k?.GetValue(hdr ? "ICMProfileAC" : "ICMProfile") is string[] arr && arr.Contains(Default(d, hdr) ?? "\u0000"))
                        res.AddRange(arr);
                }
            }
            catch { }
            return res.Distinct().ToList();
        }

        /// <summary>ICC header creation date (bytes 24..35).</summary>
        public static DateTime? Created(string name)
        {
            try
            {
                var b = new byte[36];
                using (var f = File.OpenRead(Path.Combine(ColorDir, name))) if (f.Read(b, 0, 36) < 36) return null;
                int U(int o) => (b[o] << 8) | b[o + 1];
                return new DateTime(U(24), U(26), U(28), U(30), U(32), U(34), DateTimeKind.Utc).ToLocalTime();
            }
            catch { return null; }
        }

        /// <summary>Opens Settings → Display, where an HDR profile must be picked for Windows to apply it.</summary>
        public static void OpenDisplaySettings() =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
    }
}
