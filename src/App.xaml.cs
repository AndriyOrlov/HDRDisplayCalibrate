// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Linq;
using System.Windows;

namespace HDRDisplayCalibrate
{
    public partial class App : Application
    {
        // let a session that is still quitting leave the sensor cleanly (a crash is covered by the job object)
        protected override void OnExit(ExitEventArgs e) { Core.SpotSession.WaitReleased(); base.OnExit(e); }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (Environment.GetEnvironmentVariable("HDRDC_LANG") == "en")      // English UI with English number format
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            if (e.Args.Length > 0 && e.Args[0] == "--selftest") { SelfTest.Run(e.Args.Skip(1).ToArray()); Shutdown(); return; }
            if (e.Args.Length > 1 && e.Args[0] == "--render3d")
            {
                var disp = new Core.Gamut.Space("ASUS", (0.6776, 0.3163), (0.2824, 0.6460), (0.1559, 0.0673));
                var v = new Pages.Gamut3D(disp, new[] { (Core.Gamut.References[0], System.Windows.Media.Colors.Gray), (Core.Gamut.References[2], System.Windows.Media.Color.FromRgb(0xF9, 0x73, 0x16)), (Core.Gamut.References[3], System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE)) });
                var host = new System.Windows.Controls.Border { Width = 900, Height = 460, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 36)), Child = v };
                host.Measure(new Size(900, 460)); host.Arrange(new Rect(0, 0, 900, 460)); host.UpdateLayout();
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(900, 460, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); rtb.Render(host);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var fs = System.IO.File.Create(e.Args[1])) enc.Save(fs);
                Shutdown(); return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--spot-test")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    var sb = new System.Text.StringBuilder();
                    try
                    {
                        AppState.I.Init();
                        using var s = new Core.SpotSession(AppState.I.Setup, "-a");
                        for (int i = 0; i < 6; i++)
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            var v = await s.ReadAsync();
                            sb.AppendLine($"{i}: lux {v.Y * System.Math.PI:F1}  {sw.ElapsedMilliseconds} ms");
                        }
                    }
                    catch (System.Exception ex) { sb.AppendLine(ex.ToString()); }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Core.Paths.Docs, "spot_test.txt"), sb.ToString());
                    Shutdown();
                });
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--wheel-test")
            {
                // a wheel turn over the 3D gamut zooms the camera and does not scroll the page
                var mw = new MainWindow(5); mw.Show();
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    static System.Collections.Generic.IEnumerable<DependencyObject> All(DependencyObject d)
                    {
                        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                        { var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i); yield return c; foreach (var x in All(c)) yield return x; }
                    }
                    var g = All(mw).OfType<Pages.Gamut3D>().First();
                    var sv = All(mw).OfType<System.Windows.Controls.ScrollViewer>().First(x => x.ScrollableHeight > 0);
                    var f = typeof(Pages.Gamut3D).GetField("_dist", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    double d0 = (double)f.GetValue(g), o0 = sv.VerticalOffset;
                    g.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
                    await System.Threading.Tasks.Task.Delay(200);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Core.Paths.Docs, "wheel_test.txt"), $"camera {d0} -> {f.GetValue(g)}, page offset {o0} -> {sv.VerticalOffset}");
                    Shutdown();
                });
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--esc-test")
            {
                // live meter on page 1, then a real Esc key press from outside: how long until the job is gone
                var mw = new MainWindow(1); mw.Show();
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2500);
                    var live = (Pages.LivePage)mw.Host.Content;
                    live.GetType().GetMethod("Toggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(live, null);
                    await System.Threading.Tasks.Task.Delay(9000);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    keybd_event(0x1B, 0, 0, System.UIntPtr.Zero); keybd_event(0x1B, 0, 2, System.UIntPtr.Zero);
                    while (AppState.I.Job != null && sw.ElapsedMilliseconds < 10000) await System.Threading.Tasks.Task.Delay(20);
                    long gone = sw.ElapsedMilliseconds;
                    while (System.Diagnostics.Process.GetProcessesByName("spotread").Length > 0 && sw.ElapsedMilliseconds < 10000) await System.Threading.Tasks.Task.Delay(20);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Core.Paths.Docs, "esc_test.txt"), $"job ended {gone} ms after Esc, spotread gone {sw.ElapsedMilliseconds} ms; status: {mw.StatusText.Text}");
                    Shutdown();
                });
                return;
            }
            if (e.Args.Length > 1 && e.Args[0] == "--osd-test")
            {
                // times Auto OSD on one monitor (by model), then puts its OSD values and calibration back
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    var sb = new System.Text.StringBuilder();
                    var outFile = System.IO.Path.Combine(Core.Paths.Docs, "osd_test.txt");
                    var t0 = System.Diagnostics.Stopwatch.StartNew();
                    void Out(string t) { lock (sb) { sb.AppendLine($"{t0.Elapsed.TotalSeconds,6:F1}s  {t}"); System.IO.File.WriteAllText(outFile, sb.ToString()); } }
                    var st = AppState.I; st.Init();
                    var d = st.Displays.First(x => x.Model == e.Args[1]);
                    st.Display = d; st.Setup.Display = d.ArgyllNumber.Value;
                    st.Status += (t, _) => Out(t);
                    var codes = new[] { Core.Ddc.Brightness, Core.Ddc.GainR, Core.Ddc.GainG, Core.Ddc.GainB };
                    var orig = new System.Collections.Generic.Dictionary<byte, uint>();
                    using (var ddc = Core.Ddc.Open(d)) foreach (var c in codes) if (ddc.Get(c) is (uint cur, uint _)) orig[c] = cur;
                    Out("original " + string.Join(" ", orig.Select(kv => $"0x{kv.Key:X2}={kv.Value}")));
                    try
                    {
                        double nits = e.Args.Length > 2 ? double.Parse(e.Args[2], System.Globalization.CultureInfo.InvariantCulture) : 120;
                        var res = await Jobs.AutoOsd(d, (0.3127, 0.3290), nits, Core.Paths.Session("osd_test"), default);
                        using var ddc = Core.Ddc.Open(d);
                        Out("result: " + res + " | " + string.Join(" ", codes.Select(c => $"0x{c:X2}={ddc.Get(c)?.cur}")));
                    }
                    catch (System.Exception ex) { Out(ex.ToString()); }
                    finally
                    {
                        using (var ddc = Core.Ddc.Open(d)) foreach (var kv in orig) ddc.Set(kv.Key, kv.Value);
                        Core.Argyll.LoadInstalled(d.ArgyllNumber.Value);
                        Out("restored");
                    }
                    await System.Threading.Tasks.Task.Delay(300);
                    Shutdown();
                });
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--live-test")
            {
                // finds the monitor the sensor hangs on (white on each in turn), then times live readings there
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    var sb = new System.Text.StringBuilder();
                    var outFile = System.IO.Path.Combine(Core.Paths.Docs, "live_test.txt");
                    void Out(string t) { sb.AppendLine(t); System.IO.File.WriteAllText(outFile, sb.ToString()); }
                    try
                    {
                        var st = AppState.I; st.Init();
                        Core.DisplayTarget best = null; double bestY = -1;
                        foreach (var d in st.Displays.Where(x => x.ArgyllNumber != null))
                        {
                            st.Display = d; st.Setup.Display = d.ArgyllNumber.Value;
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            using var rd = new Jobs.LiveReader();
                            var w = await rd.Read(1, 1, 1, default);
                            Out($"probe {d.Model}: white {w.Y:F1} cd/m2  ({sw.ElapsedMilliseconds} ms incl. instrument open)");
                            if (w.Y > bestY) { bestY = w.Y; best = d; }
                        }
                        st.Display = best; st.Setup.Display = best.ArgyllNumber.Value;
                        Out($"sensor on {best.Model}");
                        foreach (bool fast in new[] { false, true })
                        using (var rd = new Jobs.LiveReader(fast))
                        {
                            Out(fast ? "-- fast (non-adaptive)" : "-- adaptive");
                            foreach (var (n, r, g, b) in new[] { ("white", 1.0, 1.0, 1.0), ("white", 1, 1, 1), ("white", 1, 1, 1), ("red", 1, 0, 0), ("green", 0, 1, 0), ("blue", 0, 0, 1), ("grey50", .5, .5, .5), ("grey20", .2, .2, .2), ("white", 1, 1, 1) })
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                var v = await rd.Read(r, g, b, default);
                                var (x, y) = v.Xy;
                                Out($"{n,-7} Y {v.Y,8:F2}  x {x:F4} y {y:F4}  {Core.Cm.Cct(v),6:F0} K  {sw.ElapsedMilliseconds} ms");
                            }
                        }
                    }
                    catch (System.Exception ex) { Out(ex.ToString()); }
                    Shutdown();
                });
                return;
            }
            if (e.Args.Length > 1 && e.Args[0] == "--patch-test")
            {
                var st = AppState.I; st.Init();
                var d = st.Displays.First(x => x.ArgyllNumber == int.Parse(e.Args[1]));
                var w = new PatchWindow(d, e.Args.Length < 3 || e.Args[2] != "window", st.Cfg); w.Show(); w.Paint(200, 60, 60);
                var t = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(6) };
                t.Tick += (_, _) => { w.Close(); Shutdown(); }; t.Start();
                return;
            }
            if (e.Args.Length > 0 && e.Args[0] == "--measure-test")
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try { await SelfTest.Measure(e.Args.Skip(1).ToArray()); }
                    catch (System.Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(Core.Paths.Docs, "measure_test_error.txt"), ex.ToString()); }
                    Shutdown();
                });
                return;
            }
            int page = e.Args.Length > 1 && e.Args[0] == "--page" && int.TryParse(e.Args[1], out var pg) ? pg : 0;
            new MainWindow(page).Show();
        }
    }
}
