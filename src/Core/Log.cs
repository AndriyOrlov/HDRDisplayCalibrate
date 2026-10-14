// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HDRDisplayCalibrate.Core
{
    public static class Paths
    {
        public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HDRDisplayCalibrate");
        public static readonly string Docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HDRDisplayCalibrate");
        static Paths() { Directory.CreateDirectory(Home); Directory.CreateDirectory(Docs); }

        /// <summary>New folder for one session's measurements and profiles.</summary>
        public static string Session(string name)
        {
            var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Replace(' ', '_');
            var d = Path.Combine(Docs, $"{DateTime.Now:yyyy-MM-dd_HHmm}_{safe}");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static class Log
    {
        public static event Action<string> Line;
        static readonly object Gate = new();
        public static void Write(string msg)
        {
            var s = $"{DateTime.Now:HH:mm:ss}  {msg}";
            lock (Gate) try { File.AppendAllText(Path.Combine(Paths.Home, "log.txt"), $"{DateTime.Now:yyyy-MM-dd} {s}\n", Encoding.UTF8); } catch { }
            Line?.Invoke(s);
        }
    }

    /// <summary>App settings in %APPDATA%\HDRDisplayCalibrate\settings.json (kept as loose JSON).</summary>
    public sealed class Settings
    {
        static string FilePath => Path.Combine(Paths.Home, "settings.json");
        public JsonObject J = new();
        public static Settings Load()
        {
            var s = new Settings();
            try { s.J = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? new JsonObject(); } catch { }
            return s;
        }
        public void Save() => File.WriteAllText(FilePath, J.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        public string Str(string k, string def = "") => J[k]?.GetValue<string>() ?? def;
        // values set in code (int) and parsed from JSON read the same way
        public double Num(string k, double def) =>
            J[k] is JsonValue v && double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : def;
        public bool Bool(string k, bool def) => J[k] is JsonValue v && v.TryGetValue(out bool b) ? b : def;
        public void Set(string k, JsonNode v) => J[k] = v;
    }
}
