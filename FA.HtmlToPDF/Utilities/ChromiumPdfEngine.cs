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
        // 1 PDF point = 1/72 inch
        private const double PointsToInches = 1.0 / 72.0;

        // Shared HttpClient — creating per-call causes socket exhaustion and ~50ms overhead each time
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        // 64 KB receive buffer reused per-thread — avoids a 64 KB heap allocation for every CDP message
        [ThreadStatic]
        private static byte[] _receiveBuffer;
        private static byte[] GetReceiveBuffer() =>
            _receiveBuffer ?? (_receiveBuffer = new byte[65536]);
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
                        // Memory / startup reduction flags
                        "--no-first-run " +
                        "--disable-default-apps " +
                        "--disable-sync " +
                        "--disable-translate " +
                        "--disable-dev-shm-usage " +
                        "--disable-background-networking " +
                        "--metrics-recording-only " +
                        "--disable-client-side-phishing-detection " +
                        "--disable-hang-monitor " +
                        "--disable-domain-reliability " +
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

                    // Drain stdout/stderr asynchronously — prevents Chrome from blocking
                    // when its internal output buffers fill up (can cause deadlocks otherwise)
                    chromeProcess.OutputDataReceived += (_, __) => { };
                    chromeProcess.ErrorDataReceived += (_, __) => { };
                    chromeProcess.BeginOutputReadLine();
                    chromeProcess.BeginErrorReadLine();

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

                    // Paper dimensions in inches (CDP Page.printToPDF uses inches).
                    // 0 = auto-detect from HTML content after load.
                    var paperW = options.PageWidth > 0 ? options.PageWidth * PointsToInches : 0.0;
                    var paperH = options.PageHeight > 0 ? options.PageHeight * PointsToInches : 0.0;

                    pdfBytes = RunCdpSession(
                        wsUrl, htmlUri,
                        paperW, paperH,
                        options.MarginTop * PointsToInches,
                        options.MarginBottom * PointsToInches,
                        options.MarginLeft * PointsToInches,
                        options.MarginRight * PointsToInches,
                        options.TimeoutMs);

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

                // 5. Auto-detect page size from rendered content when not explicitly set.
                //    Page.getLayoutMetrics returns the full content dimensions in CSS pixels
                //    (Chrome renders at 96 PPI), independent of any viewport setting.
                if (paperW <= 0 || paperH <= 0)
                {
                    ResolveAutoPageSize(ws, cts.Token, ref paperW, ref paperH);
                }

                // 6. Page.printToPDF — printBackground:true is the critical parameter
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

                // 7. Read response — contains base64-encoded PDF in result.data
                var response = ReadUntilResponseId(ws, 5, cts.Token);
                return string.IsNullOrEmpty(response) ? null : ExtractBase64PdfData(response);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Auto page-size resolution via CDP
        // ─────────────────────────────────────────────────────────────────────────

        private static void ResolveAutoPageSize(
            ClientWebSocket ws, CancellationToken token,
            ref double paperW, ref double paperH)
        {
            // Page.getLayoutMetrics returns contentSize in CSS pixels (Chrome at 96 PPI).
            // This is the preferred CDP method — it measures the full rendered content
            // regardless of the current viewport size.
            SendCdp(ws, 6, "Page.getLayoutMetrics", "{}", token);
            var resp = ReadUntilResponseId(ws, 6, token);
            if (resp == null) return;

            // Find the "contentSize" block in the JSON response, then extract width/height.
            var csIdx = resp.IndexOf("\"contentSize\"", StringComparison.Ordinal);
            if (csIdx < 0) return;

            var w = ExtractJsonDouble(resp, "width", csIdx);
            var h = ExtractJsonDouble(resp, "height", csIdx);

            // Fallback to A4 if the query returns nothing sensible
            if (paperW <= 0) paperW = w > 0 ? w / 96.0 : 8.27;
            if (paperH <= 0) paperH = h > 0 ? h / 96.0 : 11.69;
        }

        /// <summary>
        /// Extracts the numeric value of a JSON field by scanning forward from
        /// <paramref name="startIndex"/>. No JSON library needed.
        /// </summary>
        private static double ExtractJsonDouble(string json, string fieldName, int startIndex = 0)
        {
            var marker = "\"" + fieldName + "\":";
            var pos = json.IndexOf(marker, startIndex, StringComparison.Ordinal);
            if (pos < 0) return 0;
            pos += marker.Length;
            while (pos < json.Length && json[pos] == ' ') pos++;
            var end = pos;
            while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == 'e' || json[end] == 'E' || json[end] == '+'))
                end++;
            if (end <= pos) return 0;
            double result;
            return double.TryParse(json.Substring(pos, end - pos),
                NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : 0;
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

            var buf = GetReceiveBuffer();
            var seg = new ArraySegment<byte>(buf);

            WebSocketReceiveResult result;
            try
            {
                result = ws.ReceiveAsync(seg, token).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }

            // Fast path: single-frame message — covers >99% of CDP messages
            // No MemoryStream allocation needed
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(buf, 0, result.Count);

            // Slow path: multi-frame message — accumulate frames
            // Initial capacity avoids resizing in most cases
            var ms = new MemoryStream(result.Count * 4);
            ms.Write(buf, 0, result.Count);
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

            // GetBuffer() returns the internal array directly — avoids the extra copy in ToArray()
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CDP HTTP helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static bool WaitForCdpReady(string cdpBase, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var r = _http.GetAsync(cdpBase + "/json/version").GetAwaiter().GetResult();
                    if (r.IsSuccessStatusCode) return true;
                }
                catch { }
                Thread.Sleep(150);
            }
            return false;
        }

        private static string GetFirstTabWebSocketUrl(string cdpBase)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                string json;
                try
                {
                    json = _http.GetStringAsync(cdpBase + "/json/list").GetAwaiter().GetResult();
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
                try { _http.GetStringAsync(cdpBase + "/json/new").GetAwaiter().GetResult(); } catch { }
                Thread.Sleep(300);
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
