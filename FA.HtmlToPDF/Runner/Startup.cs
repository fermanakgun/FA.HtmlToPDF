using System;
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

            var outputFilePath = Path.Combine(outputDirectory, "receipt-sample.pdf");

            var options = new HtmlToPdfOptions
            {
                Title = "Receipt Sample",
                Author = "FA.HtmlToPDF Runner",
                Subject = "Console startup PDF export",
                Keywords = "html,pdf,receipt",
                Creator = "FA.HtmlToPDF.Runner",
                Producer = "FA.HtmlToPDF"
            };

            HtmlToPdfConverter.SaveSampleReceiptPdf(outputFilePath, options);

            Console.WriteLine("PDF oluşturuldu: " + outputFilePath);
        }
    }
}
