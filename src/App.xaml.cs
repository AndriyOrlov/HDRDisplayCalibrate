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
