// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HDRDisplayCalibrate.Core
{
    public sealed record ArgyllDisplay(int Number, string Name, int X, int Y, int W, int H, bool Primary)
    {
        public override string ToString() => $"{Number}: {Name} {W}×{H} @ ({X},{Y}){(Primary ? " · primary" : "")}";
    }

    public sealed record Progress(string Stage, int Done, int Total, string Line);

    /// <summary>How measurement patches are shown.</summary>
    public enum PatchMode { OwnFullscreen, OwnWindow, ArgyllFullscreen, ArgyllWindow }

    /// <summary>Shared measurement options, turned into ArgyllCMS arguments.</summary>
    public sealed class MeasureSetup
    {
        public int Display = 1;              // Argyll display number (native window mode)
        public int Instrument = 1;
        public string DisplayType = "l";     // i1D3: l/n = non-refresh (LCD/OLED), r/c = refresh (CRT, plasma)
        public string Correction = "";       // .ccss / .ccmx
        public bool VideoRange;              // TV 16-235
        public PatchMode Patches = PatchMode.OwnFullscreen;
        public int WebPort = 18080;

        public bool OwnWindow => Patches == PatchMode.OwnFullscreen || Patches == PatchMode.OwnWindow;

        public List<string> Args()
        {
            var a = new List<string> { OwnWindow ? $"-dweb:{WebPort}" : $"-d{Display}", $"-c{Instrument}", "-y", DisplayType, "-Y", "p" };
            if (Patches == PatchMode.ArgyllFullscreen) a.AddRange(new[] { "-P", "0.5,0.5,20" });
            if (Patches == PatchMode.ArgyllWindow) a.Add("-F");
            if (VideoRange) a.Add("-E");
            if (!string.IsNullOrWhiteSpace(Correction)) { a.Add("-X"); a.Add(Correction); }
            return a;
        }
    }

    public static class Argyll
    {
        public static string Bin { get; private set; }

        public static string Find(string configured)
        {
            bool Ok(string d) => !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d, "dispcal.exe"));
            var cands = new List<string> { configured, Path.Combine(AppContext.BaseDirectory, "Argyll", "bin"), @"C:\Program Files\ArgyllCMS\bin", @"C:\ArgyllCMS\bin" };
            foreach (var (root, sub) in new[] {
                (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"DisplayCAL\dl"),
                (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"GloamApp\current\argyll_cache"),
                (@"C:\", ""), (@"C:\Program Files", "") })
                try
                {
                    var d = Path.Combine(root, sub);
                    if (Directory.Exists(d)) cands.AddRange(Directory.GetDirectories(d, "Argyll*").OrderByDescending(x => x).Select(x => Path.Combine(x, "bin")));
                }
                catch { }
            cands.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));
            return Bin = cands.FirstOrDefault(Ok);
        }

        // the folder found at start can disappear (an app that bundled Argyll was uninstalled): look again
        public static string Exe(string tool)
        {
            if (Bin == null || !File.Exists(Path.Combine(Bin, tool + ".exe"))) Find(null);
            return Path.Combine(Bin ?? throw new FileNotFoundException("ArgyllCMS"), tool + ".exe");
        }

        /// <summary>Runs a tool, streaming each \r / \n separated line; parses "patch N of M" into progress.</summary>
        public static async Task<(int code, string output)> RunAsync(string tool, IEnumerable<string> args, string workDir,
            string stage, IProgress<Progress> progress, CancellationToken ct, string stdin = null)
        {
            await Task.Run(SpotSession.WaitReleased);
            if (tool is "dispread" or "dispcal" or "spotread" or "chartread") Instrument.Free();
            var psi = new ProcessStartInfo(Exe(tool))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Latin1, StandardErrorEncoding = Encoding.Latin1,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var all = new StringBuilder();
            using var p = Process.Start(psi); ChildProcesses.Track(p);
            Log.Write($"> {tool} {string.Join(' ', psi.ArgumentList)}");
            if (stdin != null) p.StandardInput.Write(stdin);
            p.StandardInput.Close();
            using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });

            async Task Pump(StreamReader r)
            {
                var buf = new char[256]; var line = new StringBuilder();
                int n;
                while ((n = await r.ReadAsync(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < n; i++)
                    {
                        char ch = buf[i];
                        if (ch == '\r' || ch == '\n')
                        {
                            if (line.Length == 0) continue;
                            var s = line.ToString(); line.Clear();
                            lock (all) all.AppendLine(s);
                            var m = Regex.Match(s, @"patch (\d+) of (\d+)");
                            if (m.Success) progress?.Report(new Progress(stage, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), null));
                            else { progress?.Report(new Progress(stage, -1, -1, s)); Log.Write("  " + s); }
                        }
                        else line.Append(ch);
                    }
            }
            await Task.WhenAll(Pump(p.StandardOutput), Pump(p.StandardError));
            await p.WaitForExitAsync();
            ct.ThrowIfCancellationRequested();
            return (p.ExitCode, all.ToString());
        }

        static string Usage(string tool)
        {
            var psi = new ProcessStartInfo(Exe(tool), "-?") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi); ChildProcesses.Track(p);
            var t = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return t;
        }

        public static List<ArgyllDisplay> Displays()
        {
            var res = new List<ArgyllDisplay>();
            foreach (Match m in Regex.Matches(Usage("dispwin"), @"^\s+(\d+) = '(.*?), at (-?\d+), (-?\d+), width (\d+), height (\d+)(.*?)'", RegexOptions.Multiline))
                res.Add(new ArgyllDisplay(int.Parse(m.Groups[1].Value), m.Groups[2].Value, int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value),
                                          int.Parse(m.Groups[5].Value), int.Parse(m.Groups[6].Value), m.Groups[7].Value.Contains("Primary")));
            return res;
        }

        /// <summary>Instruments as listed by spotread (index, description); serial ports are skipped.</summary>
        public static List<(int n, string name)> Instruments()
        {
            var t = Usage("spotread");
            var block = Regex.Match(t, @"-c listno.*?(?=\n -\w)", RegexOptions.Singleline).Value;
            return Regex.Matches(block, @"^\s+(\d+) = '(.*?)'", RegexOptions.Multiline)
                        .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value))
                        .Where(x => !x.Item2.StartsWith("COM", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // ------------------------------------------------------------ video LUT helpers (dispwin)
        static int Dispwin(params string[] args)
        {
            var psi = new ProcessStartInfo(Exe("dispwin")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi); ChildProcesses.Track(p); p.StandardError.ReadToEnd(); p.WaitForExit(30000);
            return p.ExitCode;
        }
        public static void LutLinear(int display) => Dispwin($"-d{display}", "-c");
        public static void LutLoad(int display, string calOrIcc) => Dispwin($"-d{display}", calOrIcc);
        public static void LutSave(int display, string calPath) => Dispwin($"-d{display}", "-s", calPath);
        public static int InstallProfile(int display, string icc) => Dispwin($"-d{display}", "-I", icc);
        public static void LoadInstalled(int display) => Dispwin($"-d{display}", "-L");
    }
}
