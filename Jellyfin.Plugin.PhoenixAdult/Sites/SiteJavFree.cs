using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// JavFree. Scene id is "postID-FC2number".
    /// The search only accepts "FC2-PPV-{num}",
    /// so this is a supplementary source for FC2 codes only.
    /// </summary>
    public class SiteJavFree : IProviderBase
    {
        private const string Fc2Prefix = "FC2-PPV-";

        private static readonly Regex Fc2KeywordRegex = new Regex(
            @"(?i)(?:FC2)[\s\-_]*(?:PPV)?[\s\-_]*(?<num>[0-9]{6,8})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex Fc2NumberTokenRegex = new Regex(
            @"(?<![0-9])(?<num>[0-9]{6,8})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PathRegex = new Regex(
            @"/(?<post>[0-9]+)/fc2-ppv-(?<num>[0-9]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Fc2InTitleRegex = new Regex(
            @"(?i)(?:FC2[\s\-_]*(?:PPV)?[\s\-_]*)[0-9]{6,8}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex DateInTextRegex = new Regex(
            @"(?<date>[A-Z][a-z]{2,8}\s+[0-9]{1,2},\s*[0-9]{4})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            var number = ResolveFc2Number(searchTitle);
            if (string.IsNullOrEmpty(number))
            {
                // Not FC2 content, so not handled here.
                return result;
            }

            var searchURL = Helper.GetSearchSearchURL(siteNum);
            if (string.IsNullOrEmpty(searchURL))
            {
                return result;
            }

            var baseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');

            try
            {
                var url = searchURL + Uri.EscapeDataString(Fc2Prefix + number);
                var searchData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);
                if (searchData == null)
                {
                    return result;
                }

                var articles = searchData.SelectNodesSafe("//article[contains(@class, 'hentry')]");
                foreach (var article in articles.Take(10))
                {
                    var href = article.SelectSingleText(".//h2/a/@href");
                    if (string.IsNullOrEmpty(href))
                    {
                        href = article.SelectSingleText(".//a[contains(@class, 'thumbnail-link')]/@href");
                    }

                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var name = article.SelectSingleText(".//h2/a").Trim();
                    var thumb = NormalizeUrl(article.SelectSingleText(".//div[contains(@class, 'thumbnail-wrap')]//img/@src"), baseURL);

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(href) } },
                        Name = string.IsNullOrEmpty(name) ? number : name,
                        ImageUrl = thumb,
                    };

                    var dateText = article.SelectSingleText(".//span[contains(@class, 'entry-date')]");
                    var date = ParseDate(dateText);
                    if (date.HasValue)
                    {
                        res.PremiereDate = date;
                        res.ProductionYear = date.Value.Year;
                    }

                    result.Add(res);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[JavFree] Search failed for '{searchTitle}': {e.Message}");
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            if (siteNum == null || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (string.IsNullOrWhiteSpace(sceneURL))
            {
                return result;
            }

            var baseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneURL.StartsWith("/", StringComparison.Ordinal)
                    ? baseURL + sceneURL
                    : $"{baseURL}/{sceneURL}";
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var rawTitle = sceneData.SelectSingleText("//header[contains(@class, 'entry-header')]//h1");
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                rawTitle = sceneData.SelectSingleText("//h1[contains(@class, 'entry-title')]");
            }

            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                rawTitle = sceneData.SelectSingleText("//meta[@property='og:title']/@content");
            }

            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                return result;
            }

            var title = Fc2InTitleRegex.Replace(rawTitle, string.Empty, 1).Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = rawTitle.Trim();
            }

            result.Item.ExternalId = sceneURL;
            result.Item.Name = title;

            var number = ParseNumberFromURL(sceneURL);
            if (string.IsNullOrEmpty(number))
            {
                number = ResolveFc2Number(rawTitle);
            }

            if (!string.IsNullOrEmpty(number))
            {
                result.Item.OriginalTitle = Fc2Prefix + number;
            }

            var published = sceneData.SelectSingleText("//span[contains(@class, 'post-author')]");
            var date = ParseDate(published);
            if (date.HasValue)
            {
                result.Item.PremiereDate = date;
                result.Item.ProductionYear = date.Value.Year;
            }

            foreach (var genreNode in sceneData.SelectNodesSafe("//div[contains(@class, 'entry-tags')]//a[contains(@rel, 'category')]"))
            {
                var genre = genreNode.InnerText.Trim();
                if (!string.IsNullOrEmpty(genre))
                {
                    result.Item.AddGenre(genre);
                }
            }

            var cover = NormalizeUrl(sceneData.SelectSingleText("//div[contains(@class, 'entry-content')]//img/@src"), baseURL);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (siteNum == null || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (string.IsNullOrWhiteSpace(sceneURL))
            {
                return result;
            }

            var baseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = sceneURL.StartsWith("/", StringComparison.Ordinal)
                    ? baseURL + sceneURL
                    : $"{baseURL}/{sceneURL}";
            }

            HtmlAgilityPack.HtmlNode sceneData = null;
            try
            {
                sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error($"[JavFree] GetImages failed for '{sceneURL}': {e.Message}");
            }

            if (sceneData == null)
            {
                return result;
            }

            var images = sceneData.SelectNodesSafe("//div[contains(@class, 'entry-content')]//img")
                .Select(o => NormalizeUrl(o.GetAttributeValue("src", string.Empty), baseURL))
                .Where(o => !string.IsNullOrEmpty(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(15)
                .ToList();

            if (images.Count == 0)
            {
                // Fallback: og:image.
                var ogImage = NormalizeUrl(sceneData.SelectSingleText("//meta[@property='og:image']/@content"), baseURL);
                if (!string.IsNullOrEmpty(ogImage))
                {
                    images.Add(ogImage);
                }
            }

            if (images.Count > 0)
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = images[0] });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = images[0] });

                foreach (var imageURL in images.Skip(1))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = imageURL });
                }
            }

            return result;
        }

        private static string ResolveFc2Number(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var match = Fc2KeywordRegex.Match(text);
            if (match.Success)
            {
                return match.Groups["num"].Value;
            }

            var token = Fc2NumberTokenRegex.Match(text);
            return token.Success ? token.Groups["num"].Value : string.Empty;
        }

        private static string ParseNumberFromURL(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var match = PathRegex.Match(url);
            return match.Success ? match.Groups["num"].Value : string.Empty;
        }

        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = DateInTextRegex.Match(text);
            var candidate = match.Success ? match.Groups["date"].Value : text.Trim();
            if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date.Year >= 1990 && date.Year <= DateTime.UtcNow.Year + 1)
            {
                return date;
            }

            return null;
        }

        private static string NormalizeUrl(string url, string baseURL)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            url = url.Trim();
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + url;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return url.StartsWith("/", StringComparison.Ordinal)
                ? baseURL + url
                : $"{baseURL}/{url}";
        }
    }
}
