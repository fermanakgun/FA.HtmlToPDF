using System;
using System.Globalization;
using System.IO;
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

        // 64 KB receive buffer reused per-thread — avoids a 64 KB heap allocation for every CDP message
        [ThreadStatic]
        private static byte[] _receiveBuffer;
        private static byte[] GetReceiveBuffer() =>
            _receiveBuffer ?? (_receiveBuffer = new byte[65536]);

        // How many times a single conversion is retried before giving up.
        // Each retry re-acquires a fresh tab; if Chrome crashed it is restarted.
        private const int MaxRetries = 2;

        public static bool TryConvert(string html, HtmlToPdfOptions options, out byte[] pdfBytes, out Exception failure)
        {
            pdfBytes = null;
            failure = null;

            // Write HTML to a temp file so Chrome can load it via file:// URL.
            // Each conversion gets its own directory — concurrent calls never collide.
            var tempRoot = Path.Combine(Path.GetTempPath(), "FA.HtmlToPDF", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var htmlPath = Path.Combine(tempRoot, "input.html");
            File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
            var htmlUri = new Uri(htmlPath).AbsoluteUri;

            try
            {
                Exception lastEx = null;

                for (var attempt = 0; attempt <= MaxRetries; attempt++)
                {
                    if (attempt > 0)
                    {
                        // Signal the host that the previous attempt failed.
                        // If Chrome has crashed it will be restarted before the next tab is opened.
                        ChromiumProcessHost.Instance.NotifyFailure();
                        Thread.Sleep(200 * attempt); // brief back-off before retry
                    }

                    ChromiumProcessHost.TabHandle tab;
                    try
                    {
                        // Slot-bounded: blocks until a concurrency slot is free.
                        // Timeout = options.TimeoutMs so a waiting request never outlives its SLA.
                        tab = ChromiumProcessHost.Instance.AcquireTab(
                            options.ChromiumExecutablePath, options.TimeoutMs);
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        continue; // retry
                    }

                    try
                    {
                        using (tab)
                        {
                            var paperW = options.PageWidth > 0 ? options.PageWidth * PointsToInches : 0.0;
                            var paperH = options.PageHeight > 0 ? options.PageHeight * PointsToInches : 0.0;

                            pdfBytes = RunCdpSession(
                                tab.WsUrl, htmlUri,
                                paperW, paperH,
                                options.MarginTop * PointsToInches,
                                options.MarginBottom * PointsToInches,
                                options.MarginLeft * PointsToInches,
                                options.MarginRight * PointsToInches,
                                options.TimeoutMs);
                        }

                        if (pdfBytes == null || pdfBytes.Length == 0)
                        {
                            lastEx = new InvalidOperationException("Chromium CDP returned empty PDF data.");
                            pdfBytes = null;
                            continue; // retry
                        }

                        ChromiumProcessHost.Instance.NotifySuccess(); // reset failure counter
                        return true; // success
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        // Tab is disposed by the using block — slot already released.
                    }
                }

                failure = lastEx ?? new InvalidOperationException("Chromium conversion failed after " + (MaxRetries + 1) + " attempts.");
                return false;
            }
            catch (Exception ex)
            {
                failure = ex;
                return false;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
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

                // 5. Page.printToPDF — Chrome handles everything natively (layout, page breaks, etc.).
                //    printBackground:true ensures borders/backgrounds are rendered.
                //    If explicit paper dimensions are provided they are forwarded;
                //    otherwise the parameters are omitted and Chrome uses its default (A4).
                string printParams;
                if (paperW > 0 && paperH > 0)
                {
                    printParams = string.Format(
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
                }
                else
                {
                    // No paper size → let Chrome use its default (A4: 8.27 × 11.69 in).
                    // Margins are still forwarded when non-zero.
                    printParams = string.Format(
                        CultureInfo.InvariantCulture,
                        "{{" +
                            "\"printBackground\":true," +
                            "\"displayHeaderFooter\":false," +
                            "\"marginTop\":{0}," +
                            "\"marginBottom\":{1}," +
                            "\"marginLeft\":{2}," +
                            "\"marginRight\":{3}" +
                        "}}",
                        mTop, mBot, mLeft, mRight);
                }

                SendCdp(ws, 5, "Page.printToPDF", printParams, cts.Token);

                // 7. Read response — contains base64-encoded PDF in result.data
                var response = ReadUntilResponseId(ws, 5, cts.Token);
                return string.IsNullOrEmpty(response) ? null : ExtractBase64PdfData(response);
            }
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
