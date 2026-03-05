using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Utilities
{
    /// <summary>
    /// Converts HTML to PDF using Chrome/Edge via Chrome DevTools Protocol (CDP).
    /// Uses Page.printToPDF with printBackground:true — exactly how Playwright does it —
    /// which correctly renders table borders, backgrounds and all CSS properties.
    /// No external NuGet dependencies; uses .NET 4.8 built-in HttpClient + ClientWebSocket.
    /// </summary>
    internal static class ChromiumPdfEngine
    {
        // 1 PDF point = 1/72 inch; 1 inch = 25.4 mm
        private const double PointsToInches = 1.0 / 72.0;

        public static bool TryConvert(string html, HtmlToPdfOptions options, out byte[] pdfBytes, out Exception failure)
        {
            pdfBytes = null;
            failure = null;

            try
            {
                var executable = ResolveChromiumExecutable(options.ChromiumExecutablePath);
                if (string.IsNullOrWhiteSpace(executable))
                {
                    failure = new FileNotFoundException(
                        "No Chrome/Edge executable found. Install Google Chrome or Microsoft Edge.");
                    return false;
                }

                var tempRoot = Path.Combine(Path.GetTempPath(), "FA.HtmlToPDF", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                var htmlPath = Path.Combine(tempRoot, "input.html");
                File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
                var htmlUri = new Uri(htmlPath).AbsoluteUri;

                // Pick a random free TCP port for Chrome remote debugging
                var debugPort = FindFreePort();

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments =
                        "--headless=new " +
                        "--disable-gpu " +
                        "--no-sandbox " +
                        "--allow-file-access-from-files " +
                        "--enable-local-file-accesses " +
                        "--disable-extensions " +
                        "--remote-debugging-port=" + debugPort + " " +
                        "about:blank",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = tempRoot
                };

                Process chromeProcess = null;
                try
                {
                    chromeProcess = Process.Start(startInfo);
                    if (chromeProcess == null)
                    {
                        failure = new InvalidOperationException("Failed to start Chromium process.");
                        return false;
                    }

                    var cdpBase = "http://127.0.0.1:" + debugPort;

                    // Wait until Chrome's CDP HTTP endpoint is ready
                    if (!WaitForCdpReady(cdpBase, timeoutMs: 12000))
                    {
                        failure = new TimeoutException(
                            "Timed out waiting for Chromium CDP endpoint on port " + debugPort + ".");
                        return false;
                    }

                    // Get the WebSocket URL of the first (about:blank) tab
                    var wsUrl = GetFirstTabWebSocketUrl(cdpBase);
                    if (string.IsNullOrEmpty(wsUrl))
                    {
                        failure = new InvalidOperationException("Could not retrieve CDP WebSocket URL.");
                        return false;
                    }

                    // Paper dimensions in inches (CDP Page.printToPDF uses inches)
                    var paperW = options.PageWidth * PointsToInches;
                    var paperH = options.PageHeight * PointsToInches;

                    pdfBytes = RunCdpSession(
                        wsUrl, htmlUri,
                        paperW, paperH,
                        options.MarginTop * PointsToInches,
                        options.MarginBottom * PointsToInches,
                        options.MarginLeft * PointsToInches,
                        options.MarginRight * PointsToInches,
                        options.ChromiumTimeoutMs);

                    if (pdfBytes == null || pdfBytes.Length == 0)
                    {
                        failure = new InvalidOperationException("Chromium CDP returned empty PDF data.");
                        return false;
                    }

                    return true;
                }
                finally
                {
                    try
                    {
                        if (chromeProcess != null && !chromeProcess.HasExited)
                            chromeProcess.Kill();
                    }
                    catch { }
                    try { chromeProcess?.Dispose(); } catch { }
                    TryDeleteDirectory(tempRoot);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CDP session: navigate → wait for loadEventFired → Page.printToPDF
        // printBackground:true is the key flag — this is exactly what Playwright uses
        // to ensure borders, backgrounds and all CSS are rendered in the PDF.
        // ─────────────────────────────────────────────────────────────────────────

        private static byte[] RunCdpSession(
            string wsUrl, string htmlUri,
            double paperW, double paperH,
            double mTop, double mBot, double mLeft, double mRight,
            int timeoutMs)
        {
            var cts = new CancellationTokenSource(timeoutMs);

            using (var ws = new ClientWebSocket())
            {
                ws.ConnectAsync(new Uri(wsUrl), cts.Token).GetAwaiter().GetResult();

                // 1. Enable the Page CDP domain
                SendCdp(ws, 1, "Page.enable", "{}", cts.Token);
                ReadUntilResponseId(ws, 1, cts.Token);

                // 2. Navigate to our local HTML file
                var navParams = "{\"url\":\"" + EscapeJson(htmlUri) + "\"}";
                SendCdp(ws, 2, "Page.navigate", navParams, cts.Token);

                // 3. Wait for Page.loadEventFired — page + CSS fully loaded
                WaitForCdpEvent(ws, "Page.loadEventFired", timeoutMs: 15000, cts.Token);

                // 4. Wait for all fonts to finish loading via document.fonts.ready.
                //    This handles both local and web fonts (Google Fonts, CDN, etc.).
                //    awaitPromise:true tells Chrome to resolve the FontFaceSet promise
                //    before returning — no fixed sleep needed.
                SendCdp(ws, 4, "Runtime.evaluate",
                    "{\"expression\":\"document.fonts.ready\",\"awaitPromise\":true}",
                    cts.Token);
                ReadUntilResponseId(ws, 4, cts.Token);

                // 5. Page.printToPDF — printBackground:true is the critical parameter
                //    that tells Chrome to print borders, backgrounds and box-shadows.
                //    The CLI --print-to-pdf flag has no equivalent for this.
                var printParams = string.Format(
                    CultureInfo.InvariantCulture,
                    "{{" +
                        "\"printBackground\":true," +
                        "\"displayHeaderFooter\":false," +
                        "\"paperWidth\":{0}," +
                        "\"paperHeight\":{1}," +
                        "\"marginTop\":{2}," +
                        "\"marginBottom\":{3}," +
                        "\"marginLeft\":{4}," +
                        "\"marginRight\":{5}" +
                    "}}",
                    paperW, paperH, mTop, mBot, mLeft, mRight);

                SendCdp(ws, 5, "Page.printToPDF", printParams, cts.Token);

                // 6. Read response — contains base64-encoded PDF in result.data
                var response = ReadUntilResponseId(ws, 5, cts.Token);
                return string.IsNullOrEmpty(response) ? null : ExtractBase64PdfData(response);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // WebSocket helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static void SendCdp(
            ClientWebSocket ws, int id, string method, string paramsJson, CancellationToken token)
        {
            var msg = "{\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + paramsJson + "}";
            var bytes = Encoding.UTF8.GetBytes(msg);
            ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token)
              .GetAwaiter().GetResult();
        }

        private static string ReadUntilResponseId(
            ClientWebSocket ws, int id, CancellationToken token)
        {
            var idFragment = "\"id\":" + id;
            for (var i = 0; i < 500 && !token.IsCancellationRequested; i++)
            {
                var msg = ReadWsMessage(ws, token);
                if (msg != null && msg.Contains(idFragment))
                    return msg;
            }
            return null;
        }

        private static void WaitForCdpEvent(
            ClientWebSocket ws, string eventName, int timeoutMs, CancellationToken token)
        {
            var fragment = "\"method\":\"" + eventName + "\"";
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
            {
                var msg = ReadWsMessage(ws, token);
                if (msg != null && msg.Contains(fragment))
                    return;
            }
        }

        private static string ReadWsMessage(ClientWebSocket ws, CancellationToken token)
        {
            if (ws.State != WebSocketState.Open)
                return null;

            var ms = new MemoryStream();
            var buf = new byte[32768];
            var seg = new ArraySegment<byte>(buf);

            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = ws.ReceiveAsync(seg, token).GetAwaiter().GetResult();
                    ms.Write(buf, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch
            {
                return null;
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CDP HTTP helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static bool WaitForCdpReady(string cdpBase, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) })
            {
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var r = http.GetAsync(cdpBase + "/json/version").GetAwaiter().GetResult();
                        if (r.IsSuccessStatusCode) return true;
                    }
                    catch { }
                    Thread.Sleep(200);
                }
            }
            return false;
        }

        private static string GetFirstTabWebSocketUrl(string cdpBase)
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) })
            {
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    string json;
                    try
                    {
                        json = http.GetStringAsync(cdpBase + "/json/list").GetAwaiter().GetResult();
                    }
                    catch
                    {
                        Thread.Sleep(300);
                        continue;
                    }

                    var url = ExtractJsonStringField(json, "webSocketDebuggerUrl");
                    if (!string.IsNullOrEmpty(url))
                        return url;

                    // No tab yet — ask Chrome to open one
                    try { http.GetStringAsync(cdpBase + "/json/new").GetAwaiter().GetResult(); } catch { }
                    Thread.Sleep(400);
                }
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // JSON helpers (no external library)
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts base64 PDF bytes from the Page.printToPDF CDP response.
        /// Response format: {"id":3,"result":{"data":"JVBERi0x..."}}
        /// </summary>
        private static byte[] ExtractBase64PdfData(string json)
        {
            const string marker = "\"data\":\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            var end = json.IndexOf("\"", start, StringComparison.Ordinal);
            if (end <= start) return null;
            try { return Convert.FromBase64String(json.Substring(start, end - start)); }
            catch { return null; }
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            var marker = "\"" + fieldName + "\":\"";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            var end = json.IndexOf("\"", start, StringComparison.Ordinal);
            return end > start ? json.Substring(start, end - start) : null;
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Utilities
        // ─────────────────────────────────────────────────────────────────────────

        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string ResolveChromiumExecutable(string preferredPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
                return preferredPath;

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                Path.Combine(pf,    "Google",    "Chrome", "Application", "chrome.exe"),
                Path.Combine(pf86,  "Google",    "Chrome", "Application", "chrome.exe"),
                Path.Combine(local, "Google",    "Chrome", "Application", "chrome.exe"),
                Path.Combine(pf,    "Microsoft", "Edge",   "Application", "msedge.exe"),
                Path.Combine(pf86,  "Microsoft", "Edge",   "Application", "msedge.exe"),
                Path.Combine(local, "Microsoft", "Edge",   "Application", "msedge.exe")
            };

            foreach (var candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;

            return null;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }
    }
}
