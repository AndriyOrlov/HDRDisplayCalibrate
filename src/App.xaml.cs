// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System.Linq;
using System.Windows;

namespace HDRDisplayCalibrate
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
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
