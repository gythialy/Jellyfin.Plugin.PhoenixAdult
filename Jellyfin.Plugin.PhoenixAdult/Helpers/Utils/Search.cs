using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using DuckDuckGoSearch;

namespace PhoenixAdult.Helpers.Utils
{
    internal static class Search
    {
        public static async Task<List<string>> GetSearchResults(string text, int[] siteNum, CancellationToken cancellationToken)
        {
            var results = new List<string>();
            string searchTerm;

            string site = null;
            if (siteNum != null)
            {
                site = new Uri(Helper.GetSearchBaseURL(siteNum)).Host;
                searchTerm = $"site:{site} {text}";
            }
            else
            {
                searchTerm = text;
            }

            if (string.IsNullOrEmpty(searchTerm))
            {
                return results;
            }

            try
            {
                // Try Google first
                var url = "https://www.google.com/search?q=" + searchTerm;
                var html = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

                var searchResults = html.SelectNodesSafe("//a[@href]");
                foreach (var searchResult in searchResults)
                {
                    var searchURL = WebUtility.HtmlDecode(searchResult.Attributes["href"].Value);
                    if (searchURL.StartsWith("/url", StringComparison.OrdinalIgnoreCase))
                    {
                        searchURL = HttpUtility.ParseQueryString(searchURL.Replace("/url", string.Empty, StringComparison.OrdinalIgnoreCase))["q"];
                    }

                    if (searchURL.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !searchURL.Contains("google", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(searchURL);
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }

            if (results.Any())
            {
                return results;
            }

            try
            {
                // Fallback to DuckDuckGo
                var client = new DuckDuckGoSearchClient();
                var searchResults = await client.GetSearchResultsAsync(searchTerm);
                if (searchResults != null)
                {
                    results.AddRange(searchResults.Select(result => result.Url));
                }
            }
            catch (Exception)
            {
                // ignored
            }

            return results;
        }

        public static async Task<List<string>> GetSearchResults(string text, CancellationToken cancellationToken)
            => await GetSearchResults(text, null, cancellationToken).ConfigureAwait(false);
    }
}
