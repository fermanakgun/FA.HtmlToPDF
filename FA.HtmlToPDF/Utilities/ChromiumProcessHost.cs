using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;

namespace FA.HtmlToPDF.Utilities
{
    /// <summary>
    /// Manages a single, long-lived Chrome/Edge process shared across all PDF conversion calls.
    ///
    /// Problem it solves
    /// -----------------
    /// The naive approach starts a new Chrome process per request (~3 s cold-start, ~200 MB RAM each).
    /// Under concurrent load (bank PDF export: many simultaneous requests) that causes:
    ///   • N × 3 s latency per request
    ///   • N × ~200 MB RAM
    ///   • OS process handle exhaustion
    ///
    /// This approach
    /// -------------
    ///   • One Chrome process, started on the first request and kept alive.
    ///   • Each concurrent conversion gets its own CDP tab (Target) — fully isolated.
    ///   • Tab creation via /json/new takes ~50 ms; closing via /json/close/{id} takes ~20 ms.
    ///   • Auto-restarts Chrome transparently if it crashes.
    ///   • Bounded concurrency: MaxConcurrentConversions (default = ProcessorCount × 2) sekme
    ///     aynı anda çalışır; fazlası slot boşalana kadar bekler (connection pool mantığı).
    ///   • Thread-safe; no external NuGet dependencies.
    /// </summary>
    internal sealed class ChromiumProcessHost : IDisposable
    {
        // ── Singleton ─────────────────────────────────────────────────────────

        private static readonly Lazy<ChromiumProcessHost> _lazy =
            new Lazy<ChromiumProcessHost>(
                () => new ChromiumProcessHost(),
                LazyThreadSafetyMode.ExecutionAndPublication);

        public static ChromiumProcessHost Instance => _lazy.Value;

        // ── State ─────────────────────────────────────────────────────────────

        private readonly object _lock = new object();

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // Limits simultaneous Chrome tabs. Beyond this number callers block until a slot frees.
        // Chrome renders are CPU-bound; exceeding ~ProcessorCount×2 loses throughput to
        // context-switching and Chrome renderer process contention.
        private SemaphoreSlim _slots;

        private Process _process;
        private string _cdpBase;             // e.g. "http://127.0.0.1:9222"
        private string _resolvedExecutable;  // Cached path to chrome.exe / msedge.exe

        // ── Constructor ───────────────────────────────────────────────────────

        /// <summary>
        /// Maximum number of PDF conversions that may run at the same time.
        /// Tune this before the first conversion (before Instance is first accessed).
        /// Default: min(ProcessorCount, 8).
        /// Beyond this value Chrome renderer processes contend for CPU and throughput drops.
        /// </summary>
        public static int MaxConcurrentConversions { get; set; } =
            Math.Min(Environment.ProcessorCount, 8);

        private ChromiumProcessHost()
        {
            // Each concurrent conversion opens its own HTTP connection to Chrome.
            // .NET Framework default is 2 connections per host — raise it so
            // simultaneous /json/new and /json/close calls don't queue on the socket layer.
            ServicePointManager.DefaultConnectionLimit = 256;

            _slots = new SemaphoreSlim(MaxConcurrentConversions, MaxConcurrentConversions);
        }

        // ── Public API ────────────────────────────────────────────────────────

        // Tracks consecutive failures across all threads to decide when to hard-restart Chrome.
        private int _consecutiveFailures;
        private const int FailuresBeforeRestart = 3;

        /// <summary>
        /// Called by <see cref="ChromiumPdfEngine"/> when a conversion attempt fails.
        /// After <see cref="FailuresBeforeRestart"/> consecutive failures Chrome is restarted.
        /// </summary>
        internal void NotifyFailure()
        {
            var count = Interlocked.Increment(ref _consecutiveFailures);
            if (count >= FailuresBeforeRestart)
            {
                lock (_lock)
                {
                    if (_consecutiveFailures >= FailuresBeforeRestart)
                    {
                        // Kill Chrome so EnsureAlive restarts it cleanly on the next AcquireTab.
                        // We do NOT call StartChrome() here to avoid holding the lock for 12 s.
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        KillProcess();
                    }
                }
            }
        }

        /// <summary>
        /// Called by <see cref="ChromiumPdfEngine"/> on a successful conversion to reset
        /// the consecutive failure counter.
        /// </summary>
        internal void NotifySuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }

        /// <summary>
        /// Waits for a free concurrency slot, then creates a new CDP tab on the shared
        /// Chrome process. Returns a <see cref="TabHandle"/> which MUST be disposed when
        /// the conversion finishes — this closes the tab and releases the slot.
        /// </summary>
        /// <param name="preferredExecutablePath">Optional path to chrome.exe / msedge.exe.</param>
        /// <param name="waitTimeoutMs">How long to wait for a free slot (default 60 s).</param>
        public TabHandle AcquireTab(string preferredExecutablePath, int waitTimeoutMs = 60000)
        {
            if (!_slots.Wait(waitTimeoutMs))
                throw new TimeoutException(
                    string.Format(
                        "No Chrome conversion slot became available within {0} ms. " +
                        "All {1} slots are busy. Increase MaxConcurrentConversions or retry later.",
                        waitTimeoutMs, MaxConcurrentConversions));
            try
            {
                EnsureAlive(preferredExecutablePath);
                var info = CreateNewTab();
                return new TabHandle(this, info.WsUrl, info.TargetId, _slots);
            }
            catch
            {
                _slots.Release();
                throw;
            }
        }

        /// <summary>
        /// Shuts down the Chrome process. Normally you do NOT need to call this;
        /// Chrome exits when the host process exits. Useful for graceful shutdown.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                KillProcess();
            }
        }

        // ── Chrome lifecycle ──────────────────────────────────────────────────

        private void EnsureAlive(string preferredExecutablePath)
        {
            lock (_lock)
            {
                if (_process != null && !_process.HasExited)
                    return;

                // Resolve path only when we actually need to start Chrome.
                if (string.IsNullOrWhiteSpace(_resolvedExecutable)
                    || !File.Exists(_resolvedExecutable))
                {
                    _resolvedExecutable = ResolveChromiumExecutable(preferredExecutablePath);
                }

                if (string.IsNullOrWhiteSpace(_resolvedExecutable))
                    throw new FileNotFoundException(
                        "No Chrome/Edge executable found. " +
                        "Install Google Chrome or Microsoft Edge, " +
                        "or set HtmlToPdfOptions.ChromiumExecutablePath.");

                StartChrome();
            }
        }

        private void StartChrome()
        {
            KillProcess();

            var port = FindFreePort();
            _cdpBase = "http://127.0.0.1:" + port;

            var psi = new ProcessStartInfo
            {
                FileName = _resolvedExecutable,
                Arguments =
                    "--headless=new " +
                    "--disable-gpu " +
                    "--no-sandbox " +
                    "--allow-file-access-from-files " +
                    "--enable-local-file-accesses " +
                    "--disable-extensions " +
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
                    "--remote-debugging-port=" + port + " " +
                    "about:blank",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _process = Process.Start(psi);
            if (_process == null)
                throw new InvalidOperationException("Failed to start Chromium process.");

            // Drain stdout/stderr asynchronously to prevent buffer deadlocks.
            _process.OutputDataReceived += (_, __) => { };
            _process.ErrorDataReceived += (_, __) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            if (!WaitForCdpReady(_cdpBase, timeoutMs: 12000))
            {
                KillProcess();
                throw new TimeoutException(
                    "Timed out waiting for Chromium CDP endpoint on port " + port + ".");
            }
        }

        private void KillProcess()
        {
            if (_process == null) return;
            try { if (!_process.HasExited) _process.Kill(); } catch { }
            try { _process.Dispose(); } catch { }
            _process = null;
        }

        // ── Tab management ────────────────────────────────────────────────────

        private (string WsUrl, string TargetId) CreateNewTab()
        {
            // Chrome 109+ requires PUT for /json/new (GET returns 405 Method Not Allowed).
            string json;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Put, _cdpBase + "/json/new");
                var response = _http.SendAsync(req).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to create a new Chrome tab. " + ex.Message, ex);
            }

            var wsUrl = ExtractStringField(json, "webSocketDebuggerUrl");
            var targetId = ExtractStringField(json, "id");

            if (string.IsNullOrEmpty(wsUrl))
                throw new InvalidOperationException(
                    "CDP did not return a WebSocket URL for the new tab. " +
                    "Chrome response: " + json);

            return (wsUrl, targetId);
        }

        internal void CloseTab(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            try
            {
                _http.GetStringAsync(_cdpBase + "/json/close/" + targetId)
                     .GetAwaiter().GetResult();
            }
            catch { /* best-effort */ }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private bool WaitForCdpReady(string cdpBase, int timeoutMs)
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

        internal static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        internal static string ResolveChromiumExecutable(string preferredPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
                return preferredPath;

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                Path.Combine(pf,    "Google",    "Chrome",  "Application", "chrome.exe"),
                Path.Combine(pf86,  "Google",    "Chrome",  "Application", "chrome.exe"),
                Path.Combine(local, "Google",    "Chrome",  "Application", "chrome.exe"),
                Path.Combine(pf,    "Microsoft", "Edge",    "Application", "msedge.exe"),
                Path.Combine(pf86,  "Microsoft", "Edge",    "Application", "msedge.exe"),
                Path.Combine(local, "Microsoft", "Edge",    "Application", "msedge.exe")
            };

            foreach (var c in candidates)
                if (File.Exists(c))
                    return c;

            return null;
        }

        private static string ExtractStringField(string json, string fieldName)
        {
            var marker = "\"" + fieldName + "\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            // skip optional whitespace between : and "
            while (start < json.Length && json[start] == ' ') start++;
            if (start >= json.Length || json[start] != '"') return null;
            start++; // skip opening quote
            var end = json.IndexOf("\"", start, StringComparison.Ordinal);
            return end > start ? json.Substring(start, end - start) : null;
        }

        // ── TabHandle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Represents a Chrome tab leased for one PDF conversion.
        /// Dispose() closes the tab in Chrome and frees its memory.
        /// </summary>
        internal sealed class TabHandle : IDisposable
        {
            private readonly ChromiumProcessHost _host;
            private readonly SemaphoreSlim _slots;
            private int _disposed;

            public string WsUrl { get; }
            public string TargetId { get; }

            internal TabHandle(
                ChromiumProcessHost host, string wsUrl, string targetId,
                SemaphoreSlim slots)
            {
                _host = host;
                WsUrl = wsUrl;
                TargetId = targetId;
                _slots = slots;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _host.CloseTab(TargetId);
                    _slots.Release();
                }
            }
        }
    }
}
