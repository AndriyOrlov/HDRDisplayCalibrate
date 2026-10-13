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
            int page = e.Args.Length > 1 && e.Args[0] == "--page" && int.TryParse(e.Args[1], out var pg) ? pg : 0;
            new MainWindow(page).Show();
        }
    }
}
