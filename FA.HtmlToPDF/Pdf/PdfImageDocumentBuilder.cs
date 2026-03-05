using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using FA.HtmlToPDF.Abstractions;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Pdf
{
    internal sealed class PdfImageDocumentBuilder : IPdfDocumentBuilder
    {
        public byte[] Build(Bitmap renderedHtml, HtmlToPdfOptions options)
        {
            float pageW, pageH;
            var slices = SliceBitmapIntoPages(renderedHtml, options, out pageW, out pageH);

            try
            {
                return GeneratePdfDocument(slices, options, pageW, pageH);
            }
            finally
            {
                for (var i = 0; i < slices.Count; i++)
                {
                    slices[i].Dispose();
                }
            }
        }

        private static List<ImageSlice> SliceBitmapIntoPages(
            Bitmap source, HtmlToPdfOptions options,
            out float pageWidthPt, out float pageHeightPt)
        {
            // Auto-size: derive page dimensions from the rendered bitmap.
            // 96 DPI (CSS pixels) → 72 DPI (PDF points): multiply by 72/96 = 0.75
            pageWidthPt = options.PageWidth > 0 ? options.PageWidth : source.Width * 72f / 96f;
            pageHeightPt = options.PageHeight > 0 ? options.PageHeight : source.Height * 72f / 96f;

            var contentWidthPt = pageWidthPt - options.MarginLeft - options.MarginRight;
            var contentHeightPt = pageHeightPt - options.MarginTop - options.MarginBottom;
            var targetContentWidthPx = Math.Max(1, (int)Math.Round(contentWidthPt * 96f / 72f));

            if (contentWidthPt <= 0 || contentHeightPt <= 0)
            {
                throw new InvalidOperationException("Geçersiz sayfa içerik alanı. Margin ve sayfa boyutlarını kontrol edin.");
            }

            var scale = contentWidthPt / targetContentWidthPx;
            var sourcePageHeightPx = Math.Max(1, (int)Math.Floor(contentHeightPt / scale));
            var slices = new List<ImageSlice>();

            for (var y = 0; y < source.Height; y += sourcePageHeightPx)
            {
                var sliceHeightPx = Math.Min(sourcePageHeightPx, source.Height - y);
                using (var sliceBitmap = new Bitmap(source.Width, sliceHeightPx, PixelFormat.Format24bppRgb))
                using (var graphics = Graphics.FromImage(sliceBitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, source.Width, sliceHeightPx),
                        new Rectangle(0, y, source.Width, sliceHeightPx),
                        GraphicsUnit.Pixel);

                    var rawRgbBytes = EncodeRawRgb(sliceBitmap);
                    var displayHeightPt = sliceHeightPx * scale;

                    slices.Add(new ImageSlice(rawRgbBytes, null, source.Width, sliceHeightPx, contentWidthPt, displayHeightPt));
                }
            }

            if (slices.Count == 0)
            {
                using (var emptyBitmap = new Bitmap(2, 2))
                {
                    var emptyBytes = EncodeRawRgb(emptyBitmap);
                    slices.Add(new ImageSlice(emptyBytes, null, 2, 2, contentWidthPt, 2f * scale));
                }
            }

            return slices;
        }

        private static byte[] EncodeRawRgb(Bitmap bitmap)
        {
            var rawRgb = new byte[bitmap.Width * bitmap.Height * 3];
            var index = 0;

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    rawRgb[index++] = pixel.R;
                    rawRgb[index++] = pixel.G;
                    rawRgb[index++] = pixel.B;
                }
            }

            return rawRgb;
        }

        private static byte[] GeneratePdfDocument(
            List<ImageSlice> slices, HtmlToPdfOptions options,
            float pageWidthPt, float pageHeightPt)
        {
            var encoding = Encoding.GetEncoding("ISO-8859-1", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            var objectContents = new List<byte[]>();

            var pageCount = slices.Count;
            var pageObjectIds = new List<int>(pageCount);
            var contentObjectIds = new List<int>(pageCount);
            var imageObjectIds = new List<int>(pageCount);
            var nextId = 5;

            for (var i = 0; i < pageCount; i++)
            {
                pageObjectIds.Add(nextId++);
                contentObjectIds.Add(nextId++);
                imageObjectIds.Add(nextId++);
            }

            objectContents.Add(EncodeText("<< /Type /Catalog /Pages 2 0 R >>", encoding));
            objectContents.Add(EncodeText(BuildPagesObject(pageObjectIds), encoding));
            objectContents.Add(EncodeText("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", encoding));
            objectContents.Add(EncodeText(BuildInfoObject(options), encoding));

            for (var i = 0; i < pageCount; i++)
            {
                var imageName = "Im" + (i + 1).ToString(CultureInfo.InvariantCulture);
                var pageObj = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " +
                              pageWidthPt.ToString("0.##", CultureInfo.InvariantCulture) + " " +
                              pageHeightPt.ToString("0.##", CultureInfo.InvariantCulture) +
                              "] /Resources << /XObject << /" + imageName + " " + imageObjectIds[i] + " 0 R >> >> /Contents " +
                              contentObjectIds[i] + " 0 R >>";

                objectContents.Add(EncodeText(pageObj, encoding));

                var streamText = BuildImagePlacementStream(slices[i], options, imageName, pageHeightPt);
                objectContents.Add(BuildStreamObject(encoding.GetBytes(streamText), string.Empty, encoding));
                objectContents.Add(BuildImageObject(slices[i], encoding));
            }

            using (var ms = new MemoryStream())
            {
                WriteText(ms, "%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n", encoding);

                var offsets = new List<long> { 0L };
                for (var i = 0; i < objectContents.Count; i++)
                {
                    offsets.Add(ms.Position);
                    WriteText(ms, (i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n", encoding);
                    ms.Write(objectContents[i], 0, objectContents[i].Length);
                    WriteText(ms, "\nendobj\n", encoding);
                }

                var xrefOffset = ms.Position;
                var objectCount = objectContents.Count + 1;

                WriteText(ms, "xref\n0 " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n", encoding);
                WriteText(ms, "0000000000 65535 f \n", encoding);

                for (var i = 1; i < objectCount; i++)
                {
                    WriteText(ms, offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n", encoding);
                }

                WriteText(ms, "trailer\n", encoding);
                WriteText(ms, "<< /Size " + objectCount.ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R /Info 4 0 R >>\n", encoding);
                WriteText(ms, "startxref\n", encoding);
                WriteText(ms, xrefOffset.ToString(CultureInfo.InvariantCulture), encoding);
                WriteText(ms, "\n%%EOF", encoding);

                return ms.ToArray();
            }
        }

        private static string BuildPagesObject(List<int> pageObjectIds)
        {
            var kidsBuilder = new StringBuilder();
            for (var i = 0; i < pageObjectIds.Count; i++)
            {
                if (i > 0)
                {
                    kidsBuilder.Append(' ');
                }

                kidsBuilder.Append(pageObjectIds[i]).Append(" 0 R");
            }

            return "<< /Type /Pages /Kids [ " + kidsBuilder + " ] /Count " + pageObjectIds.Count.ToString(CultureInfo.InvariantCulture) + " >>";
        }

        private static string BuildInfoObject(HtmlToPdfOptions options)
        {
            var creationDate = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            return
                "<< " +
                "/CreationDate (D:" + creationDate + "Z) " +
                "/Producer (FA.HtmlToPDF) " +
                "/Creator (FA.HtmlToPDF) " +
                ">>";
        }

        private static string BuildImagePlacementStream(
            ImageSlice slice, HtmlToPdfOptions options,
            string imageName, float pageHeightPt)
        {
            var drawX = options.MarginLeft;
            var drawY = pageHeightPt - options.MarginTop - slice.DisplayHeightPt;

            var sb = new StringBuilder();
            sb.Append("q\n");
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0:0.###} 0 0 {1:0.###} {2:0.###} {3:0.###} cm\n",
                slice.DisplayWidthPt,
                slice.DisplayHeightPt,
                drawX,
                drawY);
            sb.Append('/').Append(imageName).Append(" Do\n");
            sb.Append("Q");
            return sb.ToString();
        }

        private static byte[] BuildImageObject(ImageSlice slice, Encoding encoding)
        {
            var filterPart = string.IsNullOrWhiteSpace(slice.FilterName)
                ? string.Empty
                : " /Filter " + slice.FilterName;

            var dict = "<< /Type /XObject /Subtype /Image /Width " +
                       slice.WidthPx.ToString(CultureInfo.InvariantCulture) +
                       " /Height " + slice.HeightPx.ToString(CultureInfo.InvariantCulture) +
                       " /ColorSpace /DeviceRGB /BitsPerComponent 8" + filterPart + " /Length " +
                       slice.ImageBytes.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n";

            var header = EncodeText(dict, encoding);
            var footer = EncodeText("\nendstream", encoding);
            var result = new byte[header.Length + slice.ImageBytes.Length + footer.Length];

            Buffer.BlockCopy(header, 0, result, 0, header.Length);
            Buffer.BlockCopy(slice.ImageBytes, 0, result, header.Length, slice.ImageBytes.Length);
            Buffer.BlockCopy(footer, 0, result, header.Length + slice.ImageBytes.Length, footer.Length);

            return result;
        }

        private static byte[] BuildStreamObject(byte[] rawStream, string extraDict, Encoding encoding)
        {
            var suffix = string.IsNullOrWhiteSpace(extraDict) ? string.Empty : " " + extraDict.Trim();
            var header = EncodeText("<< /Length " + rawStream.Length.ToString(CultureInfo.InvariantCulture) + suffix + " >>\nstream\n", encoding);
            var footer = EncodeText("\nendstream", encoding);
            var result = new byte[header.Length + rawStream.Length + footer.Length];

            Buffer.BlockCopy(header, 0, result, 0, header.Length);
            Buffer.BlockCopy(rawStream, 0, result, header.Length, rawStream.Length);
            Buffer.BlockCopy(footer, 0, result, header.Length + rawStream.Length, footer.Length);

            return result;
        }

        private static void WriteText(Stream stream, string text, Encoding encoding)
        {
            var bytes = encoding.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static byte[] EncodeText(string text, Encoding encoding)
        {
            return encoding.GetBytes(text);
        }
    }
}
