using System;
using System.IO;
using System.Text.RegularExpressions;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Utilities
{
    internal static class HtmlContentPreprocessor
    {
        // Pre-compiled regex instances — compiled once at class init instead of
        // being interpreted on every Prepare() call. Especially important for
        // StripMissingLocalCssLinks which has a complex pattern.
        private static readonly Regex RxCompatMeta = new Regex(
            "<meta\\s+[^>]*http-equiv\\s*=\\s*['\"]X-UA-Compatible['\"]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxHeadOpen = new Regex(
            "<head[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxHtmlOpen = new Regex(
            "<html[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxBaseTag = new Regex(
            "<base\\s+[^>]*href\\s*=",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxLocalCssLink = new Regex(
            @"<link\b[^>]*\bhref\s*=\s*(?:[""'])(?<uri>(?:file:///|[A-Za-z]:/|[A-Za-z]:\\)[^""'?#]+)[^>]*/?>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex RxLocalPath = new Regex(
            "(?<attr>href|src)\\s*=\\s*(?<quote>[\"'])(?<value>[A-Za-z]:\\\\[^\"']+)(\\k<quote>)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Prepare(string html, HtmlToPdfOptions options)
        {
            var result = html;
            result = InjectBaseTag(result, options.HtmlBaseUrl);
            result = EnsureBrowserCompatibility(result);
            result = NormalizeLocalFilePaths(result);
            // Remove <link> tags pointing to local files that do not exist on
            // this machine. Chrome tries to fetch them, gets file-not-found,
            // and falls back to browser defaults which break table layouts.
            result = StripMissingLocalCssLinks(result);
            return result;
        }

        /// <summary>
        /// Removes &lt;link rel="stylesheet"&gt; tags whose href resolves to a
        /// local file:// path that does not exist on disk.
        /// </summary>
        private static string StripMissingLocalCssLinks(string html)
        {
            return RxLocalCssLink.Replace(html, m =>
            {
                var uri = m.Groups["uri"].Value;
                string localPath;
                if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    try { localPath = new Uri(uri).LocalPath; }
                    catch { localPath = uri; }
                }
                else
                {
                    localPath = uri;
                }
                return File.Exists(localPath) ? m.Value : string.Empty;
            });
        }

        private static string EnsureBrowserCompatibility(string html)
        {
            if (RxCompatMeta.IsMatch(html))
                return html;

            const string compatibilityMeta = "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />";

            if (RxHeadOpen.IsMatch(html))
                return RxHeadOpen.Replace(html, "$0" + compatibilityMeta, 1);

            if (RxHtmlOpen.IsMatch(html))
                return RxHtmlOpen.Replace(html, "$0<head>" + compatibilityMeta + "</head>", 1);

            return "<html><head>" + compatibilityMeta + "</head><body>" + html + "</body></html>";
        }

        private static string InjectBaseTag(string html, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return html;

            if (RxBaseTag.IsMatch(html))
                return html;

            var safeBase = System.Security.SecurityElement.Escape(baseUrl);
            var baseTag = "<base href=\"" + safeBase + "\" />";

            if (RxHeadOpen.IsMatch(html))
                return RxHeadOpen.Replace(html, "$0" + baseTag, 1);

            if (RxHtmlOpen.IsMatch(html))
                return RxHtmlOpen.Replace(html, "$0<head>" + baseTag + "</head>", 1);

            return "<html><head>" + baseTag + "</head><body>" + html + "</body></html>";
        }

        private static string NormalizeLocalFilePaths(string html)
        {
            return RxLocalPath.Replace(html, LocalPathToFileUriMatch);
        }

        private static string LocalPathToFileUriMatch(Match match)
        {
            var attr = match.Groups["attr"].Value;
            var quote = match.Groups["quote"].Value;
            var value = match.Groups["value"].Value;

            if (!Path.IsPathRooted(value))
            {
                return match.Value;
            }

            try
            {
                var separatorIndex = value.IndexOfAny(new[] { '?', '#' });
                var rawPath = separatorIndex >= 0 ? value.Substring(0, separatorIndex) : value;

                var fullPath = Path.GetFullPath(rawPath);
                var uri = new Uri(fullPath).AbsoluteUri;

                return attr + "=" + quote + uri + quote;
            }
            catch
            {
                return match.Value;
            }
        }
    }
}
