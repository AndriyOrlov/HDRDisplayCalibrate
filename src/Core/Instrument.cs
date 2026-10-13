// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>
    /// The colorimeter can be opened by one program at a time. Before a measurement, a quick room-light reading
    /// started by a background tool (AmbientLightCalibrate) is stopped; any other colour program holding the
    /// sensor is reported by name instead of Argyll's "Instrument Access Failed".
    /// </summary>
    public static class Instrument
    {
        static readonly string[] ArgyllTools = { "spotread", "dispread", "dispcal", "chartread", "ccxxmake" };
        static readonly string[] Yielding = { "AmbientLightCalibrate" };   // its readings are one-shot and safe to stop
        static readonly string[] Others = { "DisplayCAL", "calibrite PROFILER", "i1Profiler", "Calibrator" };

        public static void Free()
        {
            int me = Environment.ProcessId;
            var parents = ParentMap();
            foreach (var tool in ArgyllTools)
                foreach (var p in Process.GetProcessesByName(tool))
                    using (p)
                    {
                        int parent = parents.TryGetValue(p.Id, out var pp) ? pp : 0;
                        if (parent == me) continue;                       // one of ours, e.g. a session still closing
                        string owner = Name(parent);
                        if (Yielding.Contains(owner, StringComparer.OrdinalIgnoreCase))
                        {
                            try { p.Kill(); p.WaitForExit(2000); } catch { }
                            Log.Write($"stopped {tool} of {owner} to free the sensor");
                        }
                        else throw new InvalidOperationException(S.T($"The sensor is busy: {tool} ({owner ?? "another program"}) is using it. Close it and try again.",
                                                                      $"Датчик зайнятий: його використовує {tool} ({owner ?? "інша програма"}). Закрийте і спробуйте ще раз."));
                    }
            var other = Others.FirstOrDefault(n => Process.GetProcessesByName(n).Length > 0);
            if (other != null) Log.Write($"note: {other} is running and may hold the sensor");
        }

        static string Name(int pid)
        {
            if (pid == 0) return null;
            try { using var p = Process.GetProcessById(pid); return p.ProcessName; } catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr h, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr h, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static Dictionary<int, int> ParentMap()
        {
            var map = new Dictionary<int, int>();
            var h = CreateToolhelp32Snapshot(2, 0);                 // TH32CS_SNAPPROCESS
            if (h == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                for (bool ok = Process32First(h, ref e); ok; ok = Process32Next(h, ref e)) map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID;
            }
            finally { CloseHandle(h); }
            return map;
        }
    }
}

namespace HDRDisplayCalibrate.Core
{
    /// <summary>Every Argyll process we start joins a job object that is killed when the app exits or crashes,
    /// so no orphaned spotread keeps the sensor locked.</summary>
    public static class ChildProcesses
    {
        [StructLayout(LayoutKind.Sequential)]
        struct BASIC { public long a, b; public uint LimitFlags; public UIntPtr c, d; public uint e; public UIntPtr f; public uint g, h; }
        [StructLayout(LayoutKind.Sequential)]
        struct IO { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct EXTENDED { public BASIC Basic; public IO Io; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr sec, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXTENDED info, uint len);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        static readonly IntPtr Job = Create();

        static IntPtr Create()
        {
            var j = CreateJobObject(IntPtr.Zero, null);
            var info = new EXTENDED(); info.Basic.LimitFlags = 0x2000;      // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            SetInformationJobObject(j, 9, ref info, (uint)Marshal.SizeOf<EXTENDED>());
            return j;
        }

        public static void Track(Process p) { try { if (Job != IntPtr.Zero) AssignProcessToJobObject(Job, p.Handle); } catch { } }
    }
}
