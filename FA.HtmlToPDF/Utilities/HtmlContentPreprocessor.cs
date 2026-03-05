using System;
using System.IO;
using System.Text.RegularExpressions;
using FA.HtmlToPDF.Models;

namespace FA.HtmlToPDF.Utilities
{
    internal static class HtmlContentPreprocessor
    {
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
            // Match <link ... href="file:///C:/..."  or href="C:\..." ... />
            return Regex.Replace(
                html,
                @"<link\b[^>]*\bhref\s*=\s*(?:[""'])(?<uri>(?:file:///|[A-Za-z]:/|[A-Za-z]:\\)[^""'?#]+)[^>]*/?>",
                m =>
                {
                    var uri = m.Groups["uri"].Value;
                    // Convert file URI → local path if needed
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

                    // Keep the tag only if the file actually exists
                    return File.Exists(localPath) ? m.Value : string.Empty;
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string EnsureBrowserCompatibility(string html)
        {
            if (Regex.IsMatch(html, "<meta\\s+[^>]*http-equiv\\s*=\\s*['\"]X-UA-Compatible['\"]", RegexOptions.IgnoreCase))
            {
                return html;
            }

            const string compatibilityMeta = "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />";

            if (Regex.IsMatch(html, "<head[^>]*>", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(html, "<head[^>]*>", "$0" + compatibilityMeta, RegexOptions.IgnoreCase);
            }

            if (Regex.IsMatch(html, "<html[^>]*>", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(html, "<html[^>]*>", "$0<head>" + compatibilityMeta + "</head>", RegexOptions.IgnoreCase);
            }

            return "<html><head>" + compatibilityMeta + "</head><body>" + html + "</body></html>";
        }

        private static string InjectBaseTag(string html, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return html;
            }

            if (Regex.IsMatch(html, "<base\\s+[^>]*href\\s*=", RegexOptions.IgnoreCase))
            {
                return html;
            }

            var safeBase = System.Security.SecurityElement.Escape(baseUrl);
            var baseTag = "<base href=\"" + safeBase + "\" />";

            if (Regex.IsMatch(html, "<head[^>]*>", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(html, "<head[^>]*>", "$0" + baseTag, RegexOptions.IgnoreCase);
            }

            if (Regex.IsMatch(html, "<html[^>]*>", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(html, "<html[^>]*>", "$0<head>" + baseTag + "</head>", RegexOptions.IgnoreCase);
            }

            return "<html><head>" + baseTag + "</head><body>" + html + "</body></html>";
        }

        private static string NormalizeLocalFilePaths(string html)
        {
            var pattern = "(?<attr>href|src)\\s*=\\s*(?<quote>[\"'])(?<value>[A-Za-z]:\\\\[^\"']+)(\\k<quote>)";
            return Regex.Replace(html, pattern, LocalPathToFileUriMatch, RegexOptions.IgnoreCase);
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
