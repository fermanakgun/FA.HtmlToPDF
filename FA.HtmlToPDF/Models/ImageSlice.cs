using System;

namespace FA.HtmlToPDF.Models
{
    internal sealed class ImageSlice : IDisposable
    {
        public ImageSlice(byte[] imageBytes, string filterName, int widthPx, int heightPx, float displayWidthPt, float displayHeightPt)
        {
            ImageBytes = imageBytes;
            FilterName = filterName;
            WidthPx = widthPx;
            HeightPx = heightPx;
            DisplayWidthPt = displayWidthPt;
            DisplayHeightPt = displayHeightPt;
        }

        public byte[] ImageBytes { get; private set; }
        public string FilterName { get; }
        public int WidthPx { get; }
        public int HeightPx { get; }
        public float DisplayWidthPt { get; }
        public float DisplayHeightPt { get; }

        public void Dispose()
        {
            ImageBytes = null;
        }
    }
}
