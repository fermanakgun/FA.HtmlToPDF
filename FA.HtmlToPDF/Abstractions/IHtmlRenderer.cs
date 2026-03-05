using System.Drawing;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Abstractions
{
    public interface IHtmlRenderer
    {
        Bitmap Render(string html, HtmlToPdfOptions options);
    }
}
