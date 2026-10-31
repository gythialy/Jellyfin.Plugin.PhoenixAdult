using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;

namespace PhoenixAdult.Helpers.Utils
{
    /// <summary>
    /// Fetch entry point: direct request first, FlareSolverr on a Cloudflare challenge.
    /// </summary>
    internal static class Fetcher
    {
        private static readonly string[] CloudflareMarkers =
        {
            "Just a moment...",
            "cf-browser-verification",
            "cf-chl-",
            "challenge-platform",
            "Enable JavaScript and cookies to continue",
        };

        public static async Task<HtmlNode> Html(string url, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null)
        {
            var html = await HTML.ElementFromURL(url, cancellationToken, headers, cookies).ConfigureAwait(false);
            var text = GetHtmlText(html);

            // Direct request failed or hit the challenge: use FlareSolverr.
            if (string.IsNullOrEmpty(text) || IsCloudflareChallenge(text))
            {
                var solved = await SafeFlareSolverr(url, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(solved) && !IsCloudflareChallenge(solved))
                {
                    return HTML.ElementFromString(solved);
                }
            }

            return html;
        }

        public static async Task<string> Text(string url, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null)
        {
            var http = await HTTP.Request(url, cancellationToken, headers, cookies).ConfigureAwait(false);
            if (http.IsOK && !IsCloudflareChallenge(http.Content))
            {
                return http.Content;
            }

            return await SafeFlareSolverr(url, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<JObject> Json(string url, CancellationToken cancellationToken, IDictionary<string, string> headers = null, IDictionary<string, string> cookies = null)
        {
            var text = await Text(url, cancellationToken, headers, cookies).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                return JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                return null;
            }
        }

        public static bool IsCloudflareChallenge(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            foreach (var marker in CloudflareMarkers)
            {
                if (content.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetHtmlText(HtmlNode node)
        {
            if (node == null)
            {
                return null;
            }

            try
            {
                return node.OuterHtml;
            }
            catch (Exception)
            {
                // HtmlAgilityPack returns a detached node for non-200 responses; OuterHtml throws on it.
                try
                {
                    return node.InnerHtml;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private static async Task<string> SafeFlareSolverr(string url, CancellationToken cancellationToken)
        {
            try
            {
                return await FlareSolverr.GetHtml(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"FlareSolverr failed for {url}: {e.Message}");
                return null;
            }
        }
    }
}
