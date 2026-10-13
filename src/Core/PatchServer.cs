// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HDRDisplayCalibrate.Core
{
    /// <summary>
    /// Client for ArgyllCMS "-dweb" mode: Argyll serves the next patch colour on a long-poll
    /// endpoint; we paint it in our own window and report back what is on screen, exactly like
    /// Argyll's webdisp.js does (GET /ajax/messages?rgb(r, g, b)).
    /// </summary>
    public sealed class PatchClient
    {
        readonly int _port;
        public event Action<byte, byte, byte> Color;   // raised on the caller's sync context
        public PatchClient(int port) { _port = port; }

        public async Task RunAsync(Func<Task> afterPaint, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            string shown = "rgb(128, 128, 128)";
            while (!ct.IsCancellationRequested)
            {
                string reply;
                try
                {
                    reply = await http.GetStringAsync($"http://127.0.0.1:{_port}/ajax/messages?{Uri.EscapeDataString(shown)}", ct);
                }
                catch (HttpRequestException) { await Task.Delay(200, ct); continue; }   // server not up yet / finished
                reply = reply.Trim();
                if (reply.Length != 7 || reply[0] != '#') { await Task.Delay(50, ct); continue; }
                byte r = byte.Parse(reply.Substring(1, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(reply.Substring(3, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(reply.Substring(5, 2), NumberStyles.HexNumber);
                Color?.Invoke(r, g, b);
                await afterPaint();
                shown = $"rgb({r}, {g}, {b})";
            }
        }
    }
}
