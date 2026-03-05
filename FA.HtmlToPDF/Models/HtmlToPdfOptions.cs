using System;

namespace FA.HtmlToPDF.Models
{
    public sealed class HtmlToPdfOptions
    {
        public float PageWidth { get; set; } = 595f;
        public float PageHeight { get; set; } = 842f;
        public float MarginTop { get; set; } = 20f;
        public float MarginBottom { get; set; } = 20f;
        public float MarginLeft { get; set; } = 20f;
        public float MarginRight { get; set; } = 20f;
        public int BrowserViewportWidth { get; set; } = 980;
        public int RenderTimeoutMs { get; set; } = 20000;
        public int MaxRenderWidthPx { get; set; } = 4096;
        public int MaxRenderHeightPx { get; set; } = 24000;
        public long JpegQuality { get; set; } = 92L;
        public bool PreferChromium { get; set; } = true;
        public bool FallbackToLegacyRenderer { get; set; } = true;
        public int ChromiumTimeoutMs { get; set; } = 45000;
        public string ChromiumExecutablePath { get; set; }
        public string HtmlBaseUrl { get; set; }
        public string Title { get; set; } = "HTML to PDF";
        public string Author { get; set; }
        public string Subject { get; set; }
        public string Keywords { get; set; }
        public string Creator { get; set; } = "FA.HtmlToPDF";
        public string Producer { get; set; } = "FA.HtmlToPDF";

        internal void Validate()
        {
            if (PageWidth <= 0 || PageHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(PageWidth), "Page dimensions must be positive.");
            }

            if (MarginTop < 0 || MarginBottom < 0 || MarginLeft < 0 || MarginRight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MarginTop), "Margins cannot be negative.");
            }

            if (MarginLeft + MarginRight >= PageWidth)
            {
                throw new ArgumentException("Left + right margins must be smaller than page width.");
            }

            if (MarginTop + MarginBottom >= PageHeight)
            {
                throw new ArgumentException("Top + bottom margins must be smaller than page height.");
            }

            if (BrowserViewportWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(BrowserViewportWidth), "BrowserViewportWidth must be positive.");
            }

            if (RenderTimeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(RenderTimeoutMs), "RenderTimeoutMs must be positive.");
            }

            if (MaxRenderWidthPx <= 0 || MaxRenderHeightPx <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRenderWidthPx), "Max render size must be positive.");
            }

            if (JpegQuality < 1 || JpegQuality > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(JpegQuality), "JpegQuality must be between 1 and 100.");
            }

            if (ChromiumTimeoutMs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ChromiumTimeoutMs), "ChromiumTimeoutMs must be positive.");
            }
        }
    }
}
