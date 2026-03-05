using System.Drawing;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Abstractions
{
    public interface IPdfDocumentBuilder
    {
        byte[] Build(Bitmap renderedHtml, HtmlToPdfOptions options);
    }
}
