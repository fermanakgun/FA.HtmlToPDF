using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Utilities
{
    internal static class ChromiumPdfEngine
    {
        // 1 PDF point = 1/72 inch; 1 inch = 25.4 mm
        private const double PointsToInches = 1.0 / 72.0;
        private const double PointsToMm = 25.4 / 72.0;

        public static bool TryConvert(string html, HtmlToPdfOptions options, out byte[] pdfBytes, out Exception failure)
        {
            pdfBytes = null;
            failure = null;

            try
            {
                var executable = ResolveChromiumExecutable(options.ChromiumExecutablePath);
                if (string.IsNullOrWhiteSpace(executable))
                {
                    return false;
                }

                // Inject print styles before writing HTML so Chrome renders borders + correct size
                var preparedHtml = InjectPrintStyles(html, options);

                var tempRoot = Path.Combine(Path.GetTempPath(), "FA.HtmlToPDF", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);

                var htmlPath = Path.Combine(tempRoot, "input.html");
                var pdfPath = Path.Combine(tempRoot, "output.pdf");

                File.WriteAllText(htmlPath, preparedHtml, new UTF8Encoding(false));

                var htmlUri = new Uri(htmlPath).AbsoluteUri;

                // Convert paper dimensions from PDF points to inches (Chrome expects inches)
                var paperWidthInches = (options.PageWidth * PointsToInches).ToString("F4", CultureInfo.InvariantCulture);
                var paperHeightInches = (options.PageHeight * PointsToInches).ToString("F4", CultureInfo.InvariantCulture);

                // Set Chrome CLI margins to 0 — @page CSS injected above controls actual margins
                var args =
                    "--headless=new " +
                    "--disable-gpu " +
                    "--no-sandbox " +
                    "--allow-file-access-from-files " +
                    "--enable-local-file-accesses " +
                    "--run-all-compositor-stages-before-draw " +
                    "--virtual-time-budget=2000 " +
                    "--print-to-pdf-no-header " +
                    "--paper-width=" + paperWidthInches + " " +
                    "--paper-height=" + paperHeightInches + " " +
                    "--margin-top=0 " +
                    "--margin-bottom=0 " +
                    "--margin-left=0 " +
                    "--margin-right=0 " +
                    "--print-to-pdf=\"" + pdfPath + "\" " +
                    "\"" + htmlUri + "\"";

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = tempRoot
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        failure = new InvalidOperationException("Chromium process could not be started.");
                        return false;
                    }

                    if (!process.WaitForExit(options.ChromiumTimeoutMs))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        failure = new TimeoutException("Chromium PDF conversion timed out.");
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        failure = new InvalidOperationException("Chromium exited with code " + process.ExitCode + ".");
                        return false;
                    }
                }

                if (!File.Exists(pdfPath))
                {
                    failure = new FileNotFoundException("Chromium did not produce output PDF.", pdfPath);
                    return false;
                }

                pdfBytes = File.ReadAllBytes(pdfPath);

                TryDeleteDirectory(tempRoot);
                return true;
            }
            catch (Exception ex)
            {
                failure = ex;
                return false;
            }
        }

        /// <summary>
        /// Injects @page CSS (correct paper size + margins) and print-color-adjust rules
        /// into the HTML so Chrome renders borders, backgrounds and scales correctly.
        /// </summary>
        private static string InjectPrintStyles(string html, HtmlToPdfOptions options)
        {
            // Convert PDF points → mm for CSS @page
            var pageWmm = (options.PageWidth * PointsToMm).ToString("F2", CultureInfo.InvariantCulture);
            var pageHmm = (options.PageHeight * PointsToMm).ToString("F2", CultureInfo.InvariantCulture);
            var mTopMm = (options.MarginTop * PointsToMm).ToString("F2", CultureInfo.InvariantCulture);
            var mBottomMm = (options.MarginBottom * PointsToMm).ToString("F2", CultureInfo.InvariantCulture);
            var mLeftMm = (options.MarginLeft * PointsToMm).ToString("F2", CultureInfo.InvariantCulture);
            var mRightMm = (options.MarginRight * PointsToMm).ToString("F2", CultureInfo.InvariantCulture);

            // Viewport width in CSS pixels: (pageWidth_pt / 72) * 96 dpi
            var viewportPx = (int)Math.Round(options.PageWidth / 72.0 * 96.0);

            var styleBlock = string.Format(CultureInfo.InvariantCulture,
                "<style type='text/css'>\n" +
                "@page {{\n" +
                "  size: {0}mm {1}mm;\n" +
                "  margin: {2}mm {5}mm {3}mm {4}mm;\n" +  // top right bottom left
                "}}\n" +
                "html, body {{\n" +
                "  width: {0}mm !important;\n" +
                "  margin: 0 !important;\n" +
                "  padding: 0 !important;\n" +
                "}}\n" +
                "* {{\n" +
                "  -webkit-print-color-adjust: exact !important;\n" +
                "  print-color-adjust: exact !important;\n" +
                "  color-adjust: exact !important;\n" +
                "}}\n" +
                "</style>",
                pageWmm, pageHmm,
                mTopMm, mBottomMm, mLeftMm, mRightMm);

            var viewportMeta = string.Format("<meta name='viewport' content='width={0}'>", viewportPx);

            // Try to inject into existing <head>
            var headCloseIdx = IndexOfIgnoreCase(html, "</head>");
            if (headCloseIdx >= 0)
            {
                return html.Substring(0, headCloseIdx)
                       + "\n" + viewportMeta + "\n" + styleBlock + "\n"
                       + html.Substring(headCloseIdx);
            }

            var headOpenIdx = IndexOfIgnoreCase(html, "<head>");
            if (headOpenIdx >= 0)
            {
                var insertAt = headOpenIdx + "<head>".Length;
                return html.Substring(0, insertAt)
                       + "\n" + viewportMeta + "\n" + styleBlock + "\n"
                       + html.Substring(insertAt);
            }

            // No <head> at all — prepend
            return viewportMeta + "\n" + styleBlock + "\n" + html;
        }

        private static int IndexOfIgnoreCase(string source, string value)
        {
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveChromiumExecutable(string preferredPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            {
                return preferredPath;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var candidates = new[]
            {
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe")
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, true);
                }
            }
            catch
            {
            }
        }
    }
}
