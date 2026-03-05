using System;

namespace FA.HtmlToPDF.Models
{
    /// <summary>
    /// Options for HTML to PDF conversion.
    /// All margin and page-size values are in PDF points (1 point = 1/72 inch).
    /// A4 = 595 × 842 pt. Letter = 612 × 792 pt.
    /// Set PageWidth / PageHeight to 0 (default) to auto-size the PDF to the HTML content.
    /// </summary>
    public sealed class HtmlToPdfOptions
    {
        /// <summary>
        /// Page width in PDF points. 0 = auto-detect from HTML content (default).
        /// Example fixed sizes: 595 (A4), 612 (US Letter).
        /// </summary>
        public float PageWidth { get; set; } = 0f;

        /// <summary>
        /// Page height in PDF points. 0 = auto-detect from HTML content (default).
        /// Example fixed sizes: 842 (A4), 792 (US Letter).
        /// </summary>
        public float PageHeight { get; set; } = 0f;

        /// <summary>Top margin in PDF points. Default: 0.</summary>
        public float MarginTop { get; set; } = 0f;

        /// <summary>Bottom margin in PDF points. Default: 0.</summary>
        public float MarginBottom { get; set; } = 0f;

        /// <summary>Left margin in PDF points. Default: 0.</summary>
        public float MarginLeft { get; set; } = 0f;

        /// <summary>Right margin in PDF points. Default: 0.</summary>
        public float MarginRight { get; set; } = 0f;

        /// <summary>
        /// Maximum milliseconds to wait for Chrome/Edge to produce the PDF.
        /// Default: 45000 (45 s).
        /// </summary>
        public int TimeoutMs { get; set; } = 45000;

        /// <summary>
        /// Full path to chrome.exe / msedge.exe.
        /// Leave null to auto-detect from standard install locations.
        /// </summary>
        public string ChromiumExecutablePath { get; set; }

        /// <summary>
        /// Base URL injected as &lt;base href&gt; so relative asset paths resolve correctly.
        /// Leave null when all paths are already absolute.
        /// </summary>
        public string HtmlBaseUrl { get; set; }

        /// <summary>
        /// Try Chrome/Edge CDP engine first. Default: true.
        /// Set false to force the legacy WebBrowser renderer.
        /// </summary>
        public bool PreferChromium { get; set; } = true;

        /// <summary>
        /// Fall back to the legacy WebBrowser renderer if Chrome/Edge is unavailable.
        /// Default: true.
        /// </summary>
        public bool FallbackToLegacyRenderer { get; set; } = true;

        internal void Validate()
        {
            if (PageWidth < 0 || PageHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(PageWidth), "Page dimensions cannot be negative. Use 0 for auto-size.");

            if (MarginTop < 0 || MarginBottom < 0 || MarginLeft < 0 || MarginRight < 0)
                throw new ArgumentOutOfRangeException(nameof(MarginTop), "Margins cannot be negative.");

            if (PageWidth > 0 && MarginLeft + MarginRight >= PageWidth)
                throw new ArgumentException("Left + right margins must be smaller than page width.");

            if (PageHeight > 0 && MarginTop + MarginBottom >= PageHeight)
                throw new ArgumentException("Top + bottom margins must be smaller than page height.");

            if (TimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(TimeoutMs), "TimeoutMs must be positive.");
        }
    }
}
