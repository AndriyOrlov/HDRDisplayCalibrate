// HDRDisplayCalibrate — SDR/HDR display calibration for Windows.
// Copyright 2026 Andrii Orlov (DeadSue). Licensed under the Apache License, Version 2.0.
// Added 2026 by DMD61 (Victory61): "Export report" — the result cards (values, tables and charts) saved as PDF or HTML.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace HDRDisplayCalibrate.Pages
{
    /// <summary>
    /// Saves what a page shows as results: each card is rendered as a picture (as it looks in the app, dark theme),
    /// then written into a PDF (one page per card, made here without external programs) or a single HTML file.
    /// </summary>
    public static class ReportExport
    {
        static readonly Color Bg = Color.FromRgb(32, 32, 36);

        public static void Export(string title, string defaultName, IEnumerable<UIElement> cards)
        {
            var parts = cards.OfType<FrameworkElement>().Where(c => c.ActualWidth > 0 && c.ActualHeight > 0).ToList();
            if (parts.Count == 0) { MessageBox.Show(S.T("There are no results to export yet.", "Поки немає результатів для експорту."), "HDRDisplayCalibrate"); return; }
            var dlg = new SaveFileDialog
            {
                Title = S.T("Export report", "Експортувати звіт"),
                Filter = "PDF (*.pdf)|*.pdf|HTML (*.html)|*.html", FilterIndex = 1,
                FileName = defaultName, InitialDirectory = Core.Paths.Docs, AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var header = Header(title, parts.Max(p => p.ActualWidth));
                var images = new List<BitmapSource> { header };
                images.AddRange(parts.Select(Render));
                bool html = dlg.FilterIndex == 2 || dlg.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || dlg.FileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
                if (html) WriteHtml(dlg.FileName, title, images); else WritePdf(dlg.FileName, images);
                Core.Log.Write("report exported: " + dlg.FileName);
                if (MessageBox.Show(S.T("Report saved. Open it now?", "Звіт збережено. Відкрити зараз?"), "HDRDisplayCalibrate", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception e) { Core.Log.Write("ERROR export " + e); MessageBox.Show(e.Message, "HDRDisplayCalibrate"); }
        }

        /// <summary>Saves the cards as PDF or HTML without asking (used at the end of the HDR wizard). Returns false on error.</summary>
        public static bool SaveAuto(string file, bool html, string title, IEnumerable<UIElement> cards)
        {
            try
            {
                var parts = cards.OfType<FrameworkElement>().ToList();
                foreach (var p in parts) p.UpdateLayout();
                parts = parts.Where(c => c.ActualWidth > 0 && c.ActualHeight > 0).ToList();
                if (parts.Count == 0) return false;
                var images = new List<BitmapSource> { Header(title, parts.Max(p => p.ActualWidth)) };
                images.AddRange(parts.Select(Render));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                if (html) WriteHtml(file, title, images); else WritePdf(file, images);
                Core.Log.Write("report saved: " + file);
                return true;
            }
            catch (Exception e) { Core.Log.Write("ERROR report " + e); return false; }
        }

        /// <summary>Title, display and date drawn as the first picture of the report.</summary>
        static BitmapSource Header(string title, double width)
        {
            var d = AppState.I.Display;
            var sp = new StackPanel { Width = width, Background = new SolidColorBrush(Bg) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(16, 14, 16, 2) });
            sp.Children.Add(new TextBlock { Text = $"{d?.Name} ({d?.Model}) · {DateTime.Now.ToString("g", CultureInfo.CurrentCulture)} · HDRDisplayCalibrate (mod by DMD61)", FontSize = 12, Foreground = Ui.Muted, Margin = new Thickness(16, 0, 16, 14) });
            sp.Measure(new Size(width, double.PositiveInfinity));
            sp.Arrange(new Rect(0, 0, width, sp.DesiredSize.Height));
            sp.UpdateLayout();
            // not on screen: render it directly (it sits at 0,0)
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(sp.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            rtb.Render(sp); rtb.Freeze();
            return rtb;
        }

        /// <summary>
        /// Draws an element directly (a VisualBrush stays empty for cards that were never on screen).
        /// RenderTargetBitmap includes the element's offset inside its parent, so the bitmap is made that much
        /// larger and then cropped back to the element.
        /// </summary>
        static BitmapSource Render(FrameworkElement el)
        {
            const double scale = 2;   // sharper text and lines in the file
            var off = VisualTreeHelper.GetOffset(el);
            double w = Math.Ceiling(el.ActualWidth), h = Math.Ceiling(el.ActualHeight);
            int fw = (int)Math.Ceiling((off.X + w) * scale), fh = (int)Math.Ceiling((off.Y + h) * scale);
            var rtb = new RenderTargetBitmap(fw, fh, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Bg), null, new Rect(0, 0, off.X + w, off.Y + h));
            rtb.Render(bg);
            rtb.Render(el);
            var crop = new Int32Rect((int)Math.Floor(off.X * scale), (int)Math.Floor(off.Y * scale), (int)(w * scale), (int)(h * scale));
            crop.Width = Math.Min(crop.Width, fw - crop.X); crop.Height = Math.Min(crop.Height, fh - crop.Y);
            var cb = new CroppedBitmap(rtb, crop);
            cb.Freeze();
            return cb;
        }

        static byte[] Encode(BitmapSource b, bool jpeg)
        {
            BitmapEncoder enc = jpeg ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
            // JPEG has no transparency: flatten on the background colour first
            enc.Frames.Add(BitmapFrame.Create(jpeg ? new FormatConvertedBitmap(b, PixelFormats.Bgr24, null, 0) : b));
            using var ms = new MemoryStream(); enc.Save(ms); return ms.ToArray();
        }

        static void WriteHtml(string file, string title, List<BitmapSource> images)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(System.Net.WebUtility.HtmlEncode(title)).Append("</title>");
            sb.Append("<style>body{background:#202024;margin:0;padding:24px}img{display:block;max-width:100%;margin:0 auto 16px auto}</style></head><body>");
            foreach (var img in images)
                sb.Append("<img alt=\"\" width=\"").Append((int)(img.PixelWidth / 2)).Append("\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(Encode(img, false))).Append("\">");
            sb.Append("</body></html>");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Minimal PDF 1.4: one page per picture, page width 595 pt (A4), height following the picture.</summary>
        static void WritePdf(string file, List<BitmapSource> images)
        {
            using var fs = File.Create(file);
            var offsets = new List<long>();
            void Raw(string s) { var b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }
            void Obj(int n) { while (offsets.Count < n) offsets.Add(0); offsets[n - 1] = fs.Position; Raw($"{n} 0 obj\n"); }
            string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

            int count = images.Count;
            // objects: 1 catalog, 2 pages, then for each picture: page, content, image
            Raw("%PDF-1.4\n");
            Obj(1); Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            Obj(2); Raw("<< /Type /Pages /Count " + count + " /Kids [" + string.Join(" ", Enumerable.Range(0, count).Select(i => (3 + i * 3) + " 0 R")) + "] >>\nendobj\n");
            for (int i = 0; i < count; i++)
            {
                var img = images[i];
                int page = 3 + i * 3, content = page + 1, image = page + 2;
                double pw = 595, ph = Math.Round(pw * img.PixelHeight / img.PixelWidth, 2);
                Obj(page);
                Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pw)} {N(ph)}] /Resources << /XObject << /Im{i} {image} 0 R >> >> /Contents {content} 0 R >>\nendobj\n");
                var draw = $"q {N(pw)} 0 0 {N(ph)} 0 0 cm /Im{i} Do Q\n";
                Obj(content); Raw($"<< /Length {draw.Length} >>\nstream\n{draw}endstream\nendobj\n");
                var jpg = Encode(img, true);
                Obj(image);
                Raw($"<< /Type /XObject /Subtype /Image /Width {img.PixelWidth} /Height {img.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpg.Length} >>\nstream\n");
                fs.Write(jpg, 0, jpg.Length);
                Raw("\nendstream\nendobj\n");
            }
            long xref = fs.Position;
            Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
            foreach (var o in offsets) Raw(o.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        }
    }
}
