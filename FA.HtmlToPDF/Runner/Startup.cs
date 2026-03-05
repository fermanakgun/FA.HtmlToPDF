using System;
using System.Diagnostics;
using System.IO;
using FA.HtmlToPDF;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Runner
{
    internal static class Startup
    {
        private static void Main(string[] args)
        {
            var outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output");
            Directory.CreateDirectory(outputDirectory);

            var options = new HtmlToPdfOptions
            {
                Title = "Receipt Sample",
                Author = "FA.HtmlToPDF Runner",
                Subject = "Console startup PDF export",
                Keywords = "html,pdf,receipt",
                Creator = "FA.HtmlToPDF.Runner",
                Producer = "FA.HtmlToPDF"
            };

            // ── Debug: save processed HTML and open in browser ──────────────────
            var debugHtmlPath = Path.Combine(outputDirectory, "debug-preview.html");
            var preparedHtml = HtmlToPdfConverter.GetSampleReceiptPreparedHtml(options);
            File.WriteAllText(debugHtmlPath, preparedHtml, System.Text.Encoding.UTF8);
            Console.WriteLine("Debug HTML kaydedildi: " + debugHtmlPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = debugHtmlPath,
                UseShellExecute = true   // opens with default browser
            });

            // ── PDF üretimi ─────────────────────────────────────────────────────
            var outputFilePath = Path.Combine(outputDirectory, "receipt-sample.pdf");
            HtmlToPdfConverter.SaveSampleReceiptPdf(outputFilePath, options);
            Console.WriteLine("PDF oluşturuldu: " + outputFilePath);
        }
    }
}
