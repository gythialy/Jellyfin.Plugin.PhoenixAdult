using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
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
    /// FC2 コンテンツマーケット（adult.contents.fc2.com）。
    /// No search page (/search 404s): take the six to eight digit number from the title or file name
    /// and fetch /article/{id}/. Missing ids answer 200 with a not-found template,
    /// so the page structure is checked. Titles carry random anti-bot spans
    /// and ** masks that must be cleaned before saving.
    /// </summary>
    public class SiteFC2 : IProviderBase
    {
        private const string DefaultBaseUrl = "https://adult.contents.fc2.com";

        /// <summary>FC2-PPV-1234567 and similar strong matches.</summary>
        private static readonly Regex PrefixedNumberRegex = new Regex(
            @"(?i)FC2[\s_\-]*(?:PPV)?[\s_\-]*(?<number>[0-9]{6,8})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Bare digits, not adjacent to other digits.</summary>
        private static readonly Regex BareNumberRegex = new Regex(
            @"(?<![0-9])(?<number>[0-9]{6,8})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ArticlePathRegex = new Regex(
            @"article/(?<number>[0-9]{4,9})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Anti-bot token, e.g. **ppqojxpos*.</summary>
        private static readonly Regex MaskTokenRegex = new Regex(
            @"\*+[a-z0-9]{2,}\*?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Leftover * mask in the title.</summary>
        private static readonly Regex MaskStarRegex = new Regex(
            @"\*+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>og:title prefix "FC2-PPV-1234567 ".</summary>
        private static readonly Regex TitlePrefixRegex = new Regex(
            @"^(?i)FC2[\s_\-]*(?:PPV)?[\s_\-]*[0-9]{6,8}\s*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WhitespaceRegex = new Regex(
            @"[\s\u3000]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RuntimeRegex = new Regex(
            @"^(?:(?<h>[0-9]{1,2}):)?(?<m>[0-9]{1,2}):(?<s>[0-9]{2})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StarScoreRegex = new Regex(
            @"(?<score>[0-9])$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            try
            {
                var number = ExtractNumber(searchTitle);
                if (string.IsNullOrEmpty(number))
                {
                    // No search entry point, so a missing number means nothing to look up.
                    Logger.Debug($"FC2 search skipped (no article number): {searchTitle}");
                    return result;
                }

                var sceneID = new[] { Helper.Encode($"/article/{number}/") };
                var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID, searchDate, cancellationToken).ConfigureAwait(false);
                if (searchResult.Any())
                {
                    result.AddRange(searchResult);
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"FC2 search failed: {e.GetType().Name}: {e.Message}");
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

            var number = ExtractNumberFromSceneID(sceneID[0]);
            if (string.IsNullOrEmpty(number))
            {
                return result;
            }

            var baseUrl = GetBaseUrl(siteNum);
            var sceneURL = $"{baseUrl}/article/{number}/";
            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            if (!IsArticlePage(sceneData, number))
            {
                // Removed items answer 200 too; a missing headerInfo means not found.
                Logger.Debug($"FC2 article not found: {number}");
                return result;
            }

            result.Item.ExternalId = sceneURL;
            result.Item.OriginalTitle = $"FC2-PPV-{number}";

            var title = ExtractTitle(sceneData);
            result.Item.Name = string.IsNullOrEmpty(title) ? result.Item.OriginalTitle : title;

            result.Item.PremiereDate = ExtractReleaseDate(sceneData);

            var runtime = ParseRuntime(sceneData.SelectSingleText("//div[@class='items_article_MainitemThumb']//p[@class='items_article_info']"));
            if (runtime.HasValue && runtime.Value.Ticks > 0)
            {
                result.Item.RunTimeTicks = runtime.Value.Ticks;
            }

            var scoreText = sceneData.SelectSingleText("//li[@class='items_article_StarA']//p/span/@class");
            var score = StarScoreRegex.Match(scoreText ?? string.Empty);
            if (score.Success
                && int.TryParse(score.Groups["score"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var starCount)
                && starCount > 0)
            {
                result.Item.CommunityRating = Math.Min(starCount * 2, 10);
            }

            var maker = sceneData.SelectSingleText("//li[contains(@class, 'items_article_writer')]//a");
            if (string.IsNullOrEmpty(maker))
            {
                maker = sceneData.SelectSingleText("//div[@class='items_article_headerInfo']//ul/li[last()]/a");
            }

            if (!string.IsNullOrEmpty(maker))
            {
                result.Item.AddStudio(maker.Trim());
            }

            foreach (var genreNode in sceneData.SelectNodesSafe("//section[contains(@class, 'items_article_TagArea')]//a"))
            {
                var genre = CleanText(genreNode.InnerText);
                if (!string.IsNullOrEmpty(genre))
                {
                    result.Item.AddGenre(genre);
                }
            }

            var overview = await GetOverview(sceneData, sceneURL, baseUrl, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(overview))
            {
                result.Item.Overview = overview;
            }

            var cover = await GetCover(siteNum, number, sceneData, cancellationToken).ConfigureAwait(false);
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

            try
            {
                var number = ExtractNumberFromSceneID(sceneID[0]);
                if (string.IsNullOrEmpty(number))
                {
                    return result;
                }

                var sceneURL = $"{GetBaseUrl(siteNum)}/article/{number}/";
                var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);

                var cover = await GetCover(siteNum, number, sceneData, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cover))
                {
                    result.Add(new RemoteImageInfo { Url = cover, Type = ImageType.Primary });
                }

                foreach (var poster in GetPreviewImages(sceneData).Take(10))
                {
                    result.Add(new RemoteImageInfo { Url = poster, Type = ImageType.Backdrop });
                }

                if (result.Count == 0)
                {
                    // Outside the list page: fall back to the main thumbnail.
                    var thumb = AbsoluteUrl(GetBaseUrl(siteNum), sceneData?.SelectSingleText("//meta[@property='og:image']/@content"));
                    if (!string.IsNullOrEmpty(thumb))
                    {
                        result.Add(new RemoteImageInfo { Url = thumb, Type = ImageType.Primary });
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"FC2 images failed: {e.GetType().Name}: {e.Message}");
            }

            return result;
        }

        private static string GetBaseUrl(int[] siteNum)
        {
            var baseUrl = siteNum == null ? string.Empty : Helper.GetSearchBaseURL(siteNum);
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = DefaultBaseUrl;
            }

            return baseUrl.TrimEnd('/');
        }

        private static bool IsArticlePage(HtmlNode sceneData, string number)
        {
            if (sceneData == null)
            {
                return false;
            }

            if (sceneData.SelectSingleNode("//div[@class='items_article_headerInfo']") != null)
            {
                return true;
            }

            var pageTitle = sceneData.SelectSingleText("//title");
            if (pageTitle.Contains("見つかりませんでした", StringComparison.Ordinal))
            {
                return false;
            }

            // og:title still carries the number when only the layout changed.
            var ogTitle = sceneData.SelectSingleText("//meta[@property='og:title']/@content");
            return ogTitle.Contains($"FC2-PPV-{number}", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractNumber(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            var prefixed = PrefixedNumberRegex.Match(title);
            if (prefixed.Success)
            {
                return prefixed.Groups["number"].Value;
            }

            foreach (Match match in BareNumberRegex.Matches(title))
            {
                var candidate = match.Groups["number"].Value;
                if (IsDateLike(candidate))
                {
                    continue;
                }

                return candidate;
            }

            return string.Empty;
        }

        /// <summary>An eight digit yyyyMMdd value is a date, not a code.</summary>
        private static bool IsDateLike(string value)
        {
            if (value == null || value.Length != 8)
            {
                return false;
            }

            if (!value.StartsWith("19", StringComparison.Ordinal) && !value.StartsWith("20", StringComparison.Ordinal))
            {
                return false;
            }

            return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        private static string ExtractNumberFromSceneID(string sceneID)
        {
            if (string.IsNullOrEmpty(sceneID))
            {
                return string.Empty;
            }

            var decoded = DecodeSafe(sceneID);
            foreach (var candidate in new[] { decoded, sceneID })
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                var match = ArticlePathRegex.Match(candidate);
                if (match.Success)
                {
                    return match.Groups["number"].Value;
                }

                if (candidate.All(char.IsDigit) && candidate.Length is >= 4 and <= 9)
                {
                    return candidate;
                }
            }

            return ExtractNumber(decoded ?? sceneID);
        }

        /// <summary>Decode the scene id; raw numbers and paths pass through.</summary>
        private static string DecodeSafe(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                var decoded = Helper.Decode(value);
                if (!string.IsNullOrWhiteSpace(decoded) && decoded.IndexOf('\uFFFD') < 0 && !decoded.Any(char.IsControl))
                {
                    return decoded.Trim();
                }
            }
            catch (Exception)
            {
                // Non-Base58 input throws FormatException; treat it as a raw value.
            }

            return value.Trim();
        }

        private static string ExtractTitle(HtmlNode sceneData)
        {
            var title = string.Empty;
            var titleNode = sceneData?.SelectSingleNode("//div[@class='items_article_headerInfo']/h3");
            if (titleNode != null)
            {
                title = CleanText(RemoveSpamNodes(titleNode));
            }

            if (string.IsNullOrEmpty(title))
            {
                title = CleanText(sceneData?.SelectSingleText("//meta[@property='og:title']/@content"));
            }

            return TitlePrefixRegex.Replace(title ?? string.Empty, string.Empty).Trim();
        }

        /// <summary>Drop the hidden anti-bot spans before reading the title.</summary>
        private static string RemoveSpamNodes(HtmlNode node)
        {
            var clone = node.CloneNode(true);
            foreach (var span in clone.SelectNodesSafe(".//span").ToList())
            {
                var style = span.GetAttributeValue("style", string.Empty) ?? string.Empty;
                if (style.Contains("zoom:0.01", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("display:none", StringComparison.OrdinalIgnoreCase)
                    || style.Contains("overflow:hidden", StringComparison.OrdinalIgnoreCase))
                {
                    span.Remove();
                }
            }

            return clone.InnerText;
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var text = HtmlEntity.DeEntitize(value);
            text = MaskTokenRegex.Replace(text, " ");
            text = MaskStarRegex.Replace(text, string.Empty);
            text = WhitespaceRegex.Replace(text, " ");
            return text.Trim();
        }

        private static DateTime? ExtractReleaseDate(HtmlNode sceneData)
        {
            if (sceneData == null)
            {
                return null;
            }

            foreach (var node in sceneData.SelectNodesSafe("//div[contains(@class, 'items_article_softDevice')]/p"))
            {
                var text = node.InnerText;
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var separatorIndex = text.IndexOfAny(new[] { ':', '：' });
                if (separatorIndex < 0)
                {
                    continue;
                }

                var key = text.Substring(0, separatorIndex).Trim();
                if (!key.Equals("販売日", StringComparison.OrdinalIgnoreCase)
                    && !key.StartsWith("Sale Day", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("配信開始日", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var date = ParseDate(text.Substring(separatorIndex + 1));
                if (date.HasValue)
                {
                    return date;
                }
            }

            // Only the old template has items_article_Releasedate.
            var released = sceneData.SelectSingleText("//div[@class='items_article_Releasedate']/p");
            if (!string.IsNullOrEmpty(released))
            {
                var separatorIndex = released.IndexOfAny(new[] { ':', '：' });
                var dateText = separatorIndex >= 0 ? released.Substring(separatorIndex + 1) : released;
                var date = ParseDate(dateText);
                if (date.HasValue)
                {
                    return date;
                }
            }

            return null;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = CleanText(value);
            string[] formats =
            {
                "yyyy/MM/dd",
                "yyyy/M/d",
                "yyyy-MM-dd",
                "yyyy-M-d",
                "yyyy.MM.dd",
            };

            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            {
                return exact;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static TimeSpan? ParseRuntime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var match = RuntimeRegex.Match(value.Trim());
            if (!match.Success)
            {
                return null;
            }

            var hours = match.Groups["h"].Success
                ? int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture)
                : 0;
            var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            var seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
            return new TimeSpan(hours, minutes, seconds);
        }

        /// <summary>
        /// The description is a lazy iframe whose token is bound to the session, so cookies are required;
        /// retry once with a fresh article page, then fall back to the meta description.
        /// </summary>
        private static async Task<string> GetOverview(HtmlNode sceneData, string articleURL, string baseUrl, CancellationToken cancellationToken)
        {
            var text = await TryGetDescription(sceneData, baseUrl, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }

            try
            {
                // Cache buster: the 60s HTTP cache may hold a page whose token was already used.
                var freshURL = $"{articleURL}?_cb={DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}";
                var freshData = await Fetcher.Html(freshURL, cancellationToken).ConfigureAwait(false);
                text = await TryGetDescription(freshData, baseUrl, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"FC2 description retry failed: {e.GetType().Name}: {e.Message}");
            }

            // Fallback: meta description (truncated by the site).
            var meta = CleanText(sceneData?.SelectSingleText("//meta[@name='description']/@content"));
            return TitlePrefixRegex.Replace(meta, string.Empty).Trim();
        }

        private static async Task<string> TryGetDescription(HtmlNode sceneData, string baseUrl, CancellationToken cancellationToken)
        {
            var iframe = sceneData?.SelectSingleText("//section[@class='items_article_Contents']//iframe/@src");
            if (string.IsNullOrEmpty(iframe))
            {
                return string.Empty;
            }

            var descriptionURL = AbsoluteUrl(baseUrl, iframe);
            descriptionURL += (descriptionURL.Contains('?') ? "&" : "?") + "_cb=" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            try
            {
                var descriptionData = await Fetcher.Html(descriptionURL, cancellationToken).ConfigureAwait(false);
                var container = descriptionData?.SelectSingleNode("//div[@data-container]");
                if (container == null)
                {
                    return string.Empty;
                }

                var text = CleanText(container.InnerText);
                if (string.IsNullOrEmpty(text) || text.Contains("読み込みに失敗しました", StringComparison.Ordinal))
                {
                    return string.Empty;
                }

                return text;
            }
            catch (Exception e)
            {
                Logger.Debug($"FC2 description failed: {e.GetType().Name}: {e.Message}");
                return string.Empty;
            }
        }

        private static List<string> GetPreviewImages(HtmlNode sceneData)
        {
            var images = new List<string>();
            if (sceneData == null)
            {
                return images;
            }

            foreach (var node in sceneData.SelectNodesSafe("//section[contains(@class, 'items_article_SampleImages')]//li/a"))
            {
                var url = AbsoluteUrl(DefaultBaseUrl, node.GetAttributeValue("href", string.Empty));
                if (!string.IsNullOrEmpty(url) && !images.Contains(url))
                {
                    images.Add(url);
                }
            }

            return images;
        }

        private static async Task<string> GetCover(int[] siteNum, string number, HtmlNode sceneData, CancellationToken cancellationToken)
        {
            try
            {
                var sample = await Fetcher.Json($"{GetBaseUrl(siteNum)}/api/v2/videos/{number}/sample", cancellationToken).ConfigureAwait(false);
                var poster = (string)sample?["poster_image_path"];
                if (!string.IsNullOrEmpty(poster))
                {
                    return AbsoluteUrl(DefaultBaseUrl, poster);
                }
            }
            catch (Exception e)
            {
                Logger.Debug($"FC2 sample api failed: {e.GetType().Name}: {e.Message}");
            }

            var ogImage = AbsoluteUrl(DefaultBaseUrl, sceneData?.SelectSingleText("//meta[@property='og:image']/@content"));
            if (!string.IsNullOrEmpty(ogImage))
            {
                return ogImage;
            }

            var previews = GetPreviewImages(sceneData);
            if (previews.Count > 0)
            {
                return previews[0];
            }

            return AbsoluteUrl(DefaultBaseUrl, sceneData?.SelectSingleText("//div[@class='items_article_MainitemThumb']//img/@src"));
        }

        private static string AbsoluteUrl(string baseURL, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var value = url.Trim();
            if (value.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + value;
            }

            if (value.StartsWith("/", StringComparison.Ordinal))
            {
                var root = string.IsNullOrEmpty(baseURL) ? DefaultBaseUrl : baseURL;
                return root.TrimEnd('/') + value;
            }

            return value;
        }
    }
}
