// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>
    /// A long-running interactive spotread: the instrument is opened once and every reading is
    /// triggered by a key press, so readings take about a second instead of 3–5 s per process.
    /// ArgyllCMS reads keys from its console (not stdin), so keys are injected with WriteConsoleInput
    /// into the hidden console of the child process — the same idea DisplayCAL uses.
    /// </summary>
    public sealed class SpotSession : IDisposable
    {
        readonly Process _p;
        readonly StringBuilder _buf = new();
        readonly SemaphoreSlim _gate = new(1, 1);
        static readonly object ConsoleLock = new();
        static Task _closing = Task.CompletedTask;     // the previous session quitting in the background
        /// <summary>Waits until a session that was just closed has released the instrument.</summary>
        public static void WaitReleased() => _closing.Wait(4000);

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        struct INPUT_RECORD { [FieldOffset(0)] public ushort EventType; [FieldOffset(4)] public KEY_EVENT_RECORD Key; }
        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        struct KEY_EVENT_RECORD
        {
            [FieldOffset(0)] public int bKeyDown; [FieldOffset(4)] public ushort wRepeatCount; [FieldOffset(6)] public ushort wVirtualKeyCode;
            [FieldOffset(8)] public ushort wVirtualScanCode; [FieldOffset(10)] public char UnicodeChar; [FieldOffset(12)] public uint dwControlKeyState;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WriteConsoleInput(IntPtr h, INPUT_RECORD[] buf, uint n, out uint written);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        /// <param name="mode">"-e" emissive (screen, cd/m²) or "-a" ambient (room, lux).</param>
        /// <param name="fast">Fixed short integration time (-Y A): much quicker on colours and greys, a little noisier in the dark.</param>
        public SpotSession(MeasureSetup m, string mode, bool fast = false)
        {
            _closing.Wait(4000);                        // the instrument can only be opened by one spotread
            var psi = new ProcessStartInfo(Argyll.Exe("spotread"))
            {
                UseShellExecute = false, CreateNoWindow = true,       // hidden console of its own: that is where keys go
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Latin1, StandardErrorEncoding = Encoding.Latin1,
            };
            foreach (var a in new[] { $"-c{m.Instrument}", mode, "-x", "-N" }) psi.ArgumentList.Add(a);
            if (mode == "-e") { psi.ArgumentList.Add("-y"); psi.ArgumentList.Add(m.DisplayType); }
            if (fast) { psi.ArgumentList.Add("-Y"); psi.ArgumentList.Add("A"); }
            if (!string.IsNullOrWhiteSpace(m.Correction)) { psi.ArgumentList.Add("-X"); psi.ArgumentList.Add(m.Correction); }
            _p = Process.Start(psi);
            // prompts end without a newline, so read characters, not lines
            async Task Pump(System.IO.StreamReader r)
            {
                var b = new char[512]; int n;
                try { while ((n = await r.ReadAsync(b, 0, b.Length)) > 0) lock (_buf) _buf.Append(b, 0, n); } catch { }
            }
            _ = Pump(_p.StandardOutput); _ = Pump(_p.StandardError);
        }

        void Key(char c)
        {
            lock (ConsoleLock)
            {
                if (!AttachConsole((uint)_p.Id)) throw new InvalidOperationException("AttachConsole " + Marshal.GetLastWin32Error());
                try
                {
                    var h = CreateFile("CONIN$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    var down = new INPUT_RECORD { EventType = 1, Key = new KEY_EVENT_RECORD { bKeyDown = 1, wRepeatCount = 1, UnicodeChar = c, wVirtualKeyCode = (ushort)char.ToUpperInvariant(c) } };
                    var up = down; up.Key.bKeyDown = 0;
                    WriteConsoleInput(h, new[] { down, up }, 2, out _);
                    CloseHandle(h);
                }
                finally { FreeConsole(); }
            }
        }

        string Snapshot() { lock (_buf) return _buf.ToString(); }

        async Task<string> WaitFor(Regex re, int from, CancellationToken ct, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                var s = Snapshot();
                if (s.Length > from) { var m = re.Match(s, from); if (m.Success) return s.Substring(from); }
                if (_p.HasExited) throw new InvalidOperationException("spotread exited: " + s.Trim().Split('\n')[^1]);
                await Task.Delay(30, ct);
            }
            throw new TimeoutException("spotread did not answer");
        }

        static readonly Regex Ready = new(@"any other key to take a reading", RegexOptions.IgnoreCase);
        static readonly Regex Result = new(@"Result is XYZ: ([\d.\-]+) ([\d.\-]+) ([\d.\-]+)");

        /// <summary>Takes one reading. Emissive: absolute XYZ in cd/m²; ambient: XYZ with Y = lux / π.</summary>
        public async Task<V3> ReadAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                await WaitFor(Ready, 0, ct, 30000);              // instrument opened and waiting
                int from = Snapshot().Length;
                Key(' ');
                var t = await WaitFor(Result, from, ct, 30000);
                var m = Result.Match(t);
                var v = new V3(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
                lock (_buf) _buf.Remove(0, Math.Min(from, _buf.Length));   // keep the buffer small
                return v;
            }
            finally { _gate.Release(); }
        }

        /// <summary>Returns at once; spotread is asked to quit (so the instrument is left in a clean state) in the background.</summary>
        public void Dispose()
        {
            var p = _p;
            _closing = Task.Run(() =>
            {
                try { if (!p.HasExited) { Key('q'); Thread.Sleep(150); Key('q'); if (!p.WaitForExit(1500)) p.Kill(); } } catch { try { p.Kill(); } catch { } }
                p.Dispose();
            });
        }
    }
}
