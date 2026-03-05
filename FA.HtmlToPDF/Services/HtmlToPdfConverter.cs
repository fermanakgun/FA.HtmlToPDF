using FA.HtmlToPDF.Models;
using FA.HtmlToPDF.Pdf;
using FA.HtmlToPDF.Rendering;
using FA.HtmlToPDF.Services;

namespace FA.HtmlToPDF
{
    public static class HtmlToPdfConverter
    {
        private static readonly HtmlToPdfService DefaultService = new HtmlToPdfService(new WebBrowserHtmlRenderer(), new PdfImageDocumentBuilder());

        public static byte[] Convert(string html)
        {
            return DefaultService.ConvertToBytes(html);
        }

        public static byte[] Convert(string html, HtmlToPdfOptions options)
        {
            return DefaultService.ConvertToBytes(html, options);
        }

        public static byte[] ConvertToBytes(string html)
        {
            return DefaultService.ConvertToBytes(html);
        }

        public static byte[] ConvertToBytes(string html, HtmlToPdfOptions options)
        {
            return DefaultService.ConvertToBytes(html, options);
        }

        public static void SaveToFile(string html, string outputFilePath)
        {
            DefaultService.SaveToFile(html, outputFilePath);
        }

        public static void SaveToFile(string html, string outputFilePath, HtmlToPdfOptions options)
        {
            DefaultService.SaveToFile(html, outputFilePath, options);
        }

        public static void SaveSampleReceiptPdf(string outputFilePath)
        {
            DefaultService.SaveSampleReceiptPdf(outputFilePath);
        }

        public static void SaveSampleReceiptPdf(string outputFilePath, HtmlToPdfOptions options)
        {
            DefaultService.SaveSampleReceiptPdf(outputFilePath, options);
        }

        /// <summary>
        /// Returns the preprocessed HTML string (missing CSS stripped, paths normalized)
        /// that will be fed to Chrome when generating the PDF.
        /// Save it to a .html file and open in a browser to verify layout.
        /// </summary>
        public static string GetPreparedHtml(string html, HtmlToPdfOptions options = null)
        {
            return DefaultService.GetPreparedHtml(html, options);
        }

        public static string GetSampleReceiptPreparedHtml(HtmlToPdfOptions options = null)
        {
            return DefaultService.GetSampleReceiptPreparedHtml(options);
        }
    }
}
