// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HDRDisplayCalibrate
{
    /// <summary>
    /// UI strings. English and Ukrainian are written next to each other in the code: S.T("English", "Українська").
    /// Other languages live in embedded i18n/&lt;code&gt;.json files keyed by a short hash of the English text,
    /// so a changed or new string simply falls back to English until it is translated.
    /// </summary>
    public static class S
    {
        public static readonly (string code, string name)[] Languages =
        {
            ("en", "English"), ("uk", "Українська"), ("zh", "中文（简体）"), ("hi", "हिन्दी"), ("es", "Español"),
            ("ar", "العربية"), ("fr", "Français"), ("bn", "বাংলা"), ("pt", "Português"), ("de", "Deutsch"), ("id", "Bahasa Indonesia"),
        };

        public static string Lang { get; private set; } = "en";
        public static bool Uk => Lang == "uk";
        public static bool Rtl => Lang == "ar";
        static Dictionary<string, string> _t = new();
        static readonly Dictionary<string, string> KeyCache = new();

        /// <summary>Picks the language: HDRDC_LANG, then the saved choice, then Windows' UI language, then English.</summary>
        public static void Init(string saved)
        {
            string want = Environment.GetEnvironmentVariable("HDRDC_LANG");
            if (string.IsNullOrEmpty(want) || want == "auto") want = saved;
            if (string.IsNullOrEmpty(want) || want == "auto") want = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            Lang = Languages.Any(l => l.code == want) ? want : "en";
            _t = new();
            if (Lang is not ("en" or "uk"))
                try
                {
                    using var st = typeof(S).Assembly.GetManifestResourceStream($"HDRDisplayCalibrate.i18n.{Lang}.json");
                    if (st != null) _t = JsonSerializer.Deserialize<Dictionary<string, string>>(st) ?? new();
                }
                catch { }
            // only the UI language changes; numbers and dates keep the Windows format (a culture switch would also
            // change calendars, e.g. Hijri for Arabic, and the names of measurement folders)
            try
            {
                var c = new CultureInfo(Lang == "zh" ? "zh-CN" : Lang == "pt" ? "pt-BR" : Lang);
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture = c;
            }
            catch { }
        }

        public static string Key(string en)
        {
            lock (KeyCache)
            {
                if (KeyCache.TryGetValue(en, out var k)) return k;
                k = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(en)))[..8].ToLowerInvariant();
                return KeyCache[en] = k;
            }
        }

        public static string T(string en, string uk) =>
            Lang == "en" ? en : Lang == "uk" ? uk : _t.TryGetValue(Key(en), out var v) && v.Length > 0 ? v : en;

        /// <summary>Formatted string: the templates use {0}, {1:F1} … placeholders.</summary>
        public static string F(string en, string uk, params object[] args)
        {
            try { return string.Format(CultureInfo.CurrentCulture, T(en, uk), args); }
            catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, en, args); }   // a broken translation never breaks the UI
        }
    }
}
