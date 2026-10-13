// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HDRDisplayCalibrate.Core;

namespace HDRDisplayCalibrate
{
    public static class S
    {
        // UI language follows Windows; HDRDC_LANG=en|uk overrides it
        public static readonly bool Uk = (Environment.GetEnvironmentVariable("HDRDC_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "uk";
        public static string T(string en, string uk) => Uk ? uk : en;
    }

    /// <summary>Shared state: settings, selected display/instrument and the running job.</summary>
    public sealed class AppState
    {
        public static AppState I { get; } = new();
        public Settings Cfg = Settings.Load();
        public List<DisplayTarget> Displays = new();
        public DisplayTarget Display;
        public MeasureSetup Setup = new();
        public CancellationTokenSource Job;
        public event Action Changed;
        public event Action<string, double> Status;   // text, progress 0..1 (or -1 = indeterminate)
        public void Notify() => Changed?.Invoke();
        public void Report(string text, double p = -1) => Application.Current.Dispatcher.BeginInvoke(() => Status?.Invoke(text, p));

        public void Init()
        {
            Argyll.Find(Cfg.Str("argyll_bin"));
            Setup.Instrument = (int)Cfg.Num("instrument", 1);
            Setup.DisplayType = Cfg.Str("display_type", "l");
            Setup.Correction = Cfg.Str("correction", "");
            Setup.VideoRange = Cfg.Bool("video_range", false);
            Setup.Patches = Enum.TryParse(Cfg.Str("patch_mode", "OwnFullscreen"), out PatchMode pm) ? pm : PatchMode.OwnFullscreen;
            RefreshDisplays();
        }

        public void RefreshDisplays()
        {
            Displays = Displays.Count >= 0 ? Core.Displays.List() : Displays;
            var keep = Cfg.Str("display_model");
            Display = Displays.FirstOrDefault(d => d.Model == keep) ?? Displays.FirstOrDefault();
            if (Display?.ArgyllNumber != null) Setup.Display = Display.ArgyllNumber.Value;
            Notify();
        }

        public void SaveSetup()
        {
            Cfg.Set("instrument", Setup.Instrument); Cfg.Set("display_type", Setup.DisplayType); Cfg.Set("correction", Setup.Correction);
            Cfg.Set("video_range", Setup.VideoRange); Cfg.Set("patch_mode", Setup.Patches.ToString());
            if (Display != null) Cfg.Set("display_model", Display.Model);
            Cfg.Save();
        }

        public CancellationToken BeginJob()
        {
            if (Job != null) throw new InvalidOperationException(S.T("Another measurement is running", "Уже йде інший замір"));
            Job = new CancellationTokenSource(); Notify();
            return Job.Token;
        }
        public void EndJob() { Job?.Dispose(); Job = null; Notify(); }
        public void Cancel() => Job?.Cancel();
    }

    /// <summary>Measurement orchestration on top of ArgyllCMS (own patch window or Argyll's).</summary>
    public static class Jobs
    {
        static AppState St => AppState.I;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static IProgress<Progress> Prog(string stage) => new Progress<Progress>(p =>
        {
            if (p.Done >= 0) St.Report($"{stage}: {p.Done}/{p.Total}", p.Total > 0 ? p.Done / (double)p.Total : -1);
            else if (!string.IsNullOrWhiteSpace(p.Line) && Regex.IsMatch(p.Line, "iteration|Doing|Error|error", RegexOptions.IgnoreCase)) St.Report($"{stage}: {p.Line}");
        });

        /// <summary>A patch window kept open across several measurements (live meter), or null.</summary>
        public static PatchWindow Held;

        public static void Hold(bool on)
        {
            if (on && Held == null && St.Setup.OwnWindow) { Held = new PatchWindow(St.Display, St.Setup.Patches == PatchMode.OwnFullscreen, St.Cfg); Held.Show(); }
            if (!on && Held != null) { Held.Close(); Held = null; }
        }

        /// <summary>Runs an Argyll measuring tool; in own-window mode shows our patch window and feeds it.</summary>
        public static async Task<(int code, string output)> RunMeasuring(string tool, List<string> args, string dir, string stage, CancellationToken ct)
        {
            if (!St.Setup.OwnWindow) return await Argyll.RunAsync(tool, args, dir, stage, Prog(stage), ct);
            PatchWindow win = Held;
            bool own = win == null;
            if (own)
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    win = new PatchWindow(St.Display, St.Setup.Patches == PatchMode.OwnFullscreen, St.Cfg);
                    win.Show();
                });
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var client = new PatchClient(St.Setup.WebPort);
            client.Color += (r, g, b) => Application.Current.Dispatcher.Invoke(() => win.Paint(r, g, b));
            var feed = Task.Run(() => client.RunAsync(() => Application.Current.Dispatcher.Invoke(() => win.Rendered()), stop.Token));
            try { return await Argyll.RunAsync(tool, args, dir, stage, Prog(stage), ct); }
            finally
            {
                stop.Cancel();
                try { await feed; } catch { }
                if (own) await Application.Current.Dispatcher.InvokeAsync(() => win.Close());
            }
        }

        /// <summary>Measures the given RGB patches (0..1) and returns absolute XYZ.</summary>
        public static async Task<List<Patch>> Measure(IEnumerable<(double, double, double)> rgb, string dir, string name, string stage, CancellationToken ct)
        {
            var b = Path.Combine(dir, name);
            Cgats.WriteTi1(b + ".ti1", rgb);
            if (File.Exists(b + ".ti3")) File.Delete(b + ".ti3");
            var args = St.Setup.Args(); args.Add("-v"); args.Add(name);
            var (code, outp) = await RunMeasuring("dispread", args, dir, stage, ct);
            if (code != 0 || !File.Exists(b + ".ti3")) throw new Exception(LastError(outp) ?? $"dispread exit {code}");
            return Cgats.ReadTi3(b + ".ti3", out _);
        }

        public static string LastError(string output) =>
            output?.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase) || l.Contains("failed", StringComparison.OrdinalIgnoreCase));

        /// <summary>Room light with the diffuser on: (lux, CCT).</summary>
        public static async Task<(double lux, double cct)> Ambient(CancellationToken ct)
        {
            var (_, outp) = await Argyll.RunAsync("spotread", new[] { $"-c{St.Setup.Instrument}", "-a", "-x", "-T", "-O" }, Path.GetTempPath(), "ambient", null, ct, "\n\n");
            var lux = Regex.Match(outp, @"Ambient = ([\d.]+) Lux");
            var cct = Regex.Match(outp, @"CCT = ([\d.]+)K");
            if (!lux.Success) throw new Exception(LastError(outp) ?? S.T("No ambient reading (is the diffuser over the lens?)", "Немає заміру освітлення (розсіювач на лінзі?)"));
            return (double.Parse(lux.Groups[1].Value, Inv), cct.Success ? double.Parse(cct.Groups[1].Value, Inv) : 0);
        }

        /// <summary>Standard verification chart: greys, ramps and a spread of colours.</summary>
        public static List<(double, double, double)> VerifyChart(int greys = 21)
        {
            var rows = new List<(double, double, double)>();
            for (int i = 0; i < greys; i++) { double v = i / (double)(greys - 1); rows.Add((v, v, v)); }
            foreach (var lev in new[] { 1.0, 0.75, 0.5, 0.25 })
                rows.AddRange(new[] { (lev, 0.0, 0.0), (0.0, lev, 0.0), (0.0, 0.0, lev), (lev, lev, 0.0), (0.0, lev, lev), (lev, 0.0, lev) });
            // ColorChecker-like skin, sky, foliage and mid tones (sRGB)
            rows.AddRange(new[] {
                (0.451, 0.322, 0.267), (0.761, 0.588, 0.510), (0.384, 0.478, 0.616), (0.341, 0.424, 0.263), (0.522, 0.502, 0.694), (0.404, 0.741, 0.667),
                (0.839, 0.494, 0.173), (0.314, 0.357, 0.651), (0.757, 0.353, 0.388), (0.369, 0.235, 0.424), (0.616, 0.737, 0.251), (0.878, 0.639, 0.180),
                (0.220, 0.239, 0.588), (0.275, 0.580, 0.286), (0.686, 0.212, 0.235), (0.906, 0.780, 0.122), (0.733, 0.337, 0.584), (0.031, 0.522, 0.631) });
            rows.Add((1, 1, 1)); rows.Add((0, 0, 0));
            return rows;
        }

        /// <summary>
        /// Fast reader for live work: one persistent spotread session plus our own patch window
        /// (the colour is painted by us, so readings take ~1 s instead of a new Argyll run each time).
        /// </summary>
        public sealed class LiveReader : IDisposable
        {
            readonly SpotSession _spot;
            readonly PatchWindow _win;
            /// <summary>Fixed integration time: ~1.5 s on any colour; dark greys (below ~40 %) read a bit noisier.</summary>
            public readonly bool Fast;
            public LiveReader(bool fast = false)
            {
                Fast = fast;
                _win = Application.Current.Dispatcher.Invoke(() =>
                {
                    var w = new PatchWindow(St.Display, St.Setup.Patches != PatchMode.OwnWindow, St.Cfg); w.Show(); return w;
                });
                _spot = new SpotSession(St.Setup, "-e", fast);
            }
            public async Task<V3> Read(double r, double g, double b, CancellationToken ct, int settleMs = 250)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => _win.Paint((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255)));
                await Application.Current.Dispatcher.Invoke(() => _win.Rendered());
                await Task.Delay(settleMs, ct);
                return await _spot.ReadAsync(ct);
            }
            public void Dispose() { Application.Current.Dispatcher.Invoke(() => _win.Close()); _spot.Dispose(); }   // patch goes away first: cancel feels instant
        }

        /// <summary>DDC/CI automatic monitor adjustment: RGB gains toward the white point, then brightness toward the luminance.</summary>
        public static async Task<string> AutoOsd(DisplayTarget d, (double x, double y)? wp, double? nits, string dir, CancellationToken ct)
        {
            using var ddc = Ddc.Open(d) ?? throw new Exception(S.T("This monitor does not answer DDC/CI", "Монітор не відповідає по DDC/CI"));
            if (d.ArgyllNumber is int n) Argyll.LutLinear(n);
            using var rd = new LiveReader(fast: true);      // white and full primaries only: the quick mode is as accurate there
            St.Report(S.T("Auto OSD: primaries…", "Авто-OSD: основні кольори…"));
            var P = M3.FromColumns(await rd.Read(1, 0, 0, ct), await rd.Read(0, 1, 0, ct), await rd.Read(0, 0, 1, ct));
            var gains = new[] { Ddc.GainR, Ddc.GainG, Ddc.GainB };
            string last = "";
            for (int it = 0; it < 12 && wp != null; it++)
            {
                var w = await rd.Read(1, 1, 1, ct, it == 0 ? 250 : 700);
                var tgt = Cm.XyToXyz(wp.Value.x, wp.Value.y, w.Y);
                double de = Cm.DeltaE2000(Cm.Lab(tgt, tgt), Cm.Lab(w, tgt));
                last = string.Format(Inv, "ΔE {0:F2}, {1:F0} K, {2:F1} cd/m²", de, Cm.Cct(w), w.Y);
                St.Report(S.T("Auto OSD white: ", "Авто-OSD білий: ") + last);
                if (de < 0.5) break;
                var now = P.Inverse() * w; var want = P.Inverse() * tgt;
                var ratio = new V3(want.X / now.X, want.Y / now.Y, want.Z / now.Z); ratio /= ratio.Max;   // only reduce channels
                bool moved = false;
                for (int c = 0; c < 3; c++)
                {
                    var cur = ddc.Get(gains[c]) ?? throw new Exception("DDC RGB gain");
                    // gains are roughly linear in light: move most of the way, at least one step
                    double next = cur.cur * (1 + (ratio[c] - 1) * 0.9);
                    uint v = (uint)Math.Clamp(Math.Round(next), 0, cur.max);
                    if (v == cur.cur && Math.Abs(ratio[c] - 1) > 0.004) v = (uint)Math.Clamp((int)cur.cur + (ratio[c] < 1 ? -1 : 1), 0, (int)cur.max);
                    if (v != cur.cur) { ddc.Set(gains[c], v); moved = true; }
                }
                if (!moved) break;
            }
            if (nits != null)
            {
                var cur = ddc.Get(Ddc.Brightness);
                double? prevY = null; uint prevB = 0;
                for (int it = 0; it < 8 && cur != null; it++)
                {
                    var y = (await rd.Read(1, 1, 1, ct, it == 0 ? 250 : 700)).Y;
                    last = string.Format(Inv, "{0:F1} cd/m² @ brightness {1}", y, cur.Value.cur);
                    St.Report(S.T("Auto OSD brightness: ", "Авто-OSD яскравість: ") + last);
                    double err = (y - nits.Value) / nits.Value;
                    if (Math.Abs(err) < 0.02) break;
                    // secant step from the last two readings; first step assumes 1 % of range ≈ 1.5 % of luminance
                    double slope = prevY is double py && cur.Value.cur != prevB ? (y - py) / ((double)cur.Value.cur - prevB) : y * 0.015;
                    if (slope <= 0) slope = y * 0.015;
                    int next = (int)Math.Clamp(Math.Round(cur.Value.cur + (nits.Value - y) / slope), 0, cur.Value.max);
                    if (next == cur.Value.cur) next += err > 0 ? -1 : 1;
                    if (next < 0 || next > cur.Value.max) break;
                    prevY = y; prevB = cur.Value.cur;
                    ddc.Set(Ddc.Brightness, (uint)next);
                    cur = (cur.Value.cur == (uint)next ? cur : ((uint)next, cur.Value.max));
                }
            }
            return last;
        }
    }
}
