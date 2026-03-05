using System;
using System.IO;
using FA.HtmlToPDF.Abstractions;
using FA.HtmlToPDF.Models;
using FA.HtmlToPDF.Utilities;

namespace FA.HtmlToPDF.Services
{
    public sealed class HtmlToPdfService
    {
        private readonly IHtmlRenderer _htmlRenderer;
        private readonly IPdfDocumentBuilder _pdfDocumentBuilder;

        public HtmlToPdfService(IHtmlRenderer htmlRenderer, IPdfDocumentBuilder pdfDocumentBuilder)
        {
            _htmlRenderer = htmlRenderer ?? throw new ArgumentNullException(nameof(htmlRenderer));
            _pdfDocumentBuilder = pdfDocumentBuilder ?? throw new ArgumentNullException(nameof(pdfDocumentBuilder));
        }

        public byte[] ConvertToBytes(string html, HtmlToPdfOptions options = null)
        {
            if (html == null)
            {
                throw new ArgumentNullException(nameof(html));
            }

            var effectiveOptions = options ?? new HtmlToPdfOptions();
            effectiveOptions.Validate();

            var preparedHtml = HtmlContentPreprocessor.Prepare(html, effectiveOptions);

            if (effectiveOptions.PreferChromium)
            {
                if (ChromiumPdfEngine.TryConvert(preparedHtml, effectiveOptions, out var chromiumPdfBytes, out var chromiumFailure))
                {
                    return chromiumPdfBytes;
                }

                if (!effectiveOptions.FallbackToLegacyRenderer)
                {
                    throw new InvalidOperationException("Chromium ile PDF üretimi başarısız oldu.", chromiumFailure);
                }
            }

            using (var bitmap = _htmlRenderer.Render(preparedHtml, effectiveOptions))
            {
                return _pdfDocumentBuilder.Build(bitmap, effectiveOptions);
            }
        }

        public void SaveToFile(string html, string outputFilePath, HtmlToPdfOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                throw new ArgumentException("Output file path cannot be empty.", nameof(outputFilePath));
            }

            var bytes = ConvertToBytes(html, options);
            var directory = Path.GetDirectoryName(outputFilePath);

            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(outputFilePath, bytes);
        }
    }
}
