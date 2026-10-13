// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>One active Windows display path (DisplayConfig API), plus what we know about it.</summary>
    public sealed class DisplayTarget
    {
        public string Name, Model, DevicePath, GdiName;   // GdiName: \\.\DISPLAY1
        internal Native.LUID Adapter;
        internal uint SourceId, TargetId;
        public bool HdrSupported, HdrOn, AcmOn, WideSupported;
        public double SdrWhiteNits;                        // Windows "SDR content brightness" reference white
        public int? ArgyllNumber;
        public (int x, int y, int w, int h) Bounds;        // desktop rectangle (physical pixels)
        public override string ToString() => $"{Name} ({Model}){(HdrOn ? " · HDR" : "")}";
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)] public struct PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }
        [StructLayout(LayoutKind.Sequential)] public struct RATIONAL { public uint N, D; }
        [StructLayout(LayoutKind.Sequential)] public struct PATH_TARGET { public LUID adapterId; public uint id, modeInfoIdx; public int outputTechnology, rotation, scaling; public RATIONAL refreshRate; public int scanLineOrdering, targetAvailable; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] public struct PATH_INFO { public PATH_SOURCE sourceInfo; public PATH_TARGET targetInfo; public uint flags; }
        [StructLayout(LayoutKind.Sequential)] public struct MODE_INFO { public int infoType; public uint id; public LUID adapterId; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] data; }
        [StructLayout(LayoutKind.Sequential)] public struct HEADER { public int type; public uint size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TARGET_NAME
        {
            public HEADER header; public uint flags; public int outputTechnology; public ushort edidManufactureId, edidProductCodeId; public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SOURCE_NAME { public HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
        [StructLayout(LayoutKind.Sequential)] public struct COLOR2 { public HEADER header; public uint value; public int encoding; public uint bits; public int activeColorMode; }
        [StructLayout(LayoutKind.Sequential)] public struct SET_COLOR { public HEADER header; public uint value; }
        [StructLayout(LayoutKind.Sequential)] public struct SDR_WHITE { public HEADER header; public uint level; }

        [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint np, out uint nm);
        [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH_INFO[] p, ref uint nm, [Out] MODE_INFO[] m, IntPtr topo);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME r);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME r);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref COLOR2 r);
        [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref SDR_WHITE r);
        [DllImport("user32.dll")] public static extern int DisplayConfigSetDeviceInfo(ref SET_COLOR r);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
        public delegate bool EnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFOEX mi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc; }
        [DllImport("dxva2.dll")] public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
        [DllImport("dxva2.dll")] public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PHYSICAL_MONITOR[] a);
        [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitor(IntPtr h);
        [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, IntPtr type, out uint cur, out uint max);
        [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);
    }

    public static class Displays
    {
        static string ModelOf(string devicePath)
        {
            var m = Regex.Match(devicePath ?? "", @"DISPLAY#([A-Z0-9]+)#", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "?";
        }

        public static List<DisplayTarget> List(bool withArgyll = true)
        {
            var list = new List<DisplayTarget>();
            Native.GetDisplayConfigBufferSizes(2, out uint np, out uint nm);
            var paths = new Native.PATH_INFO[np]; var modes = new Native.MODE_INFO[nm];
            if (Native.QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;
            foreach (var p in paths.Take((int)np))
            {
                var t = new Native.TARGET_NAME(); t.header.type = 2; t.header.size = (uint)Marshal.SizeOf<Native.TARGET_NAME>();
                t.header.adapterId = p.targetInfo.adapterId; t.header.id = p.targetInfo.id;
                if (Native.DisplayConfigGetDeviceInfo(ref t) != 0) continue;
                var s = new Native.SOURCE_NAME(); s.header.type = 1; s.header.size = (uint)Marshal.SizeOf<Native.SOURCE_NAME>();
                s.header.adapterId = p.sourceInfo.adapterId; s.header.id = p.sourceInfo.id;
                Native.DisplayConfigGetDeviceInfo(ref s);
                var d = new DisplayTarget
                {
                    Name = string.IsNullOrWhiteSpace(t.monitorFriendlyDeviceName) ? ModelOf(t.monitorDevicePath) : t.monitorFriendlyDeviceName,
                    Model = ModelOf(t.monitorDevicePath), DevicePath = t.monitorDevicePath, GdiName = s.viewGdiDeviceName,
                    Adapter = p.targetInfo.adapterId, SourceId = p.sourceInfo.id, TargetId = p.targetInfo.id,
                };
                RefreshColor(d);
                list.Add(d);
            }
            // desktop rectangles by GDI name
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref Native.RECT rc, IntPtr data) =>
            {
                var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
                Native.GetMonitorInfo(h, ref mi);
                foreach (var d in list.Where(x => x.GdiName == mi.szDevice))
                    d.Bounds = (mi.rcMonitor.l, mi.rcMonitor.t, mi.rcMonitor.r - mi.rcMonitor.l, mi.rcMonitor.b - mi.rcMonitor.t);
                return true;
            }, IntPtr.Zero);
            if (withArgyll && Argyll.Bin != null)
                try
                {
                    var ad = Argyll.Displays();
                    foreach (var d in list)
                        d.ArgyllNumber = ad.FirstOrDefault(a => @"\\.\" + a.Name == d.GdiName)?.Number;
                }
                catch { }
            return list;
        }

        /// <summary>HDR / ACM state and the SDR white level.</summary>
        public static void RefreshColor(DisplayTarget d)
        {
            var c = new Native.COLOR2(); c.header.type = 15; c.header.size = (uint)Marshal.SizeOf<Native.COLOR2>();
            c.header.adapterId = d.Adapter; c.header.id = d.TargetId;
            if (Native.DisplayConfigGetDeviceInfo(ref c) == 0)
            {
                d.HdrSupported = ((c.value >> 4) & 1) == 1; d.HdrOn = ((c.value >> 5) & 1) == 1;
                d.WideSupported = ((c.value >> 6) & 1) == 1; d.AcmOn = ((c.value >> 7) & 1) == 1;
            }
            var w = new Native.SDR_WHITE(); w.header.type = 11; w.header.size = (uint)Marshal.SizeOf<Native.SDR_WHITE>();
            w.header.adapterId = d.Adapter; w.header.id = d.TargetId;
            d.SdrWhiteNits = Native.DisplayConfigGetDeviceInfo(ref w) == 0 ? w.level * 80.0 / 1000 : 80;
        }

        /// <summary>Turns Windows HDR on or off for the display (same as Win+Alt+B for that screen).</summary>
        public static bool SetHdr(DisplayTarget d, bool on)
        {
            var s = new Native.SET_COLOR(); s.header.type = 10; s.header.size = (uint)Marshal.SizeOf<Native.SET_COLOR>();
            s.header.adapterId = d.Adapter; s.header.id = d.TargetId; s.value = on ? 1u : 0u;
            return Native.DisplayConfigSetDeviceInfo(ref s) == 0;
        }
    }

    /// <summary>DDC/CI monitor controls (brightness, contrast, RGB gains).</summary>
    public sealed class Ddc : IDisposable
    {
        public const byte Brightness = 0x10, Contrast = 0x12, GainR = 0x16, GainG = 0x18, GainB = 0x1A;
        readonly IntPtr _h;
        Ddc(IntPtr h) { _h = h; }

        public static Ddc Open(DisplayTarget d)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref Native.RECT rc, IntPtr data) =>
            {
                var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
                Native.GetMonitorInfo(h, ref mi);
                if (mi.szDevice != d.GdiName || found != IntPtr.Zero) return true;
                if (Native.GetNumberOfPhysicalMonitorsFromHMONITOR(h, out uint n) && n > 0)
                {
                    var arr = new Native.PHYSICAL_MONITOR[n];
                    if (Native.GetPhysicalMonitorsFromHMONITOR(h, n, arr))
                    {
                        found = arr[0].h;
                        for (int i = 1; i < n; i++) Native.DestroyPhysicalMonitor(arr[i].h);
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found == IntPtr.Zero ? null : new Ddc(found);
        }

        public (uint cur, uint max)? Get(byte code) =>
            Native.GetVCPFeatureAndVCPFeatureReply(_h, code, IntPtr.Zero, out uint c, out uint m) ? (c, m) : null;
        public bool Set(byte code, uint v) => Native.SetVCPFeature(_h, code, v);
        public void Dispose() => Native.DestroyPhysicalMonitor(_h);
    }
}
