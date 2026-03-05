using System.Text;

namespace FA.HtmlToPDF.Utilities
{
    internal static class PdfEncodingHelper
    {
        public static string ToPdfUnicodeHexString(string value)
        {
            var text = value ?? string.Empty;
            var unicodeBytes = Encoding.BigEndianUnicode.GetBytes(text);
            var sb = new StringBuilder();
            sb.Append("<FEFF");

            for (var i = 0; i < unicodeBytes.Length; i++)
            {
                sb.Append(unicodeBytes[i].ToString("X2"));
            }

            sb.Append('>');
            return sb.ToString();
        }
    }
}
