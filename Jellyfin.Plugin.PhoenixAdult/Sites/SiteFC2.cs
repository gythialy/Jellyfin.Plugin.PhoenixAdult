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
    /// 站点没有搜索页（/search 直接 404），只能从标题/文件名里抽 6~8 位纯数字编号，
    /// 再直连 /article/{id}/ 拿元数据；不存在的编号同样返回 HTTP 200（notfound 模板），
    /// 所以必须靠页面结构做内容检测。标题里会随机插入反爬 &lt;span style="zoom:0.01"&gt;
    /// 与 ** 掩码，需要清洗后再入库。
    /// </summary>
    public class SiteFC2 : IProviderBase
    {
        private const string DefaultBaseUrl = "https://adult.contents.fc2.com";

        /// <summary>FC2-PPV-1234567 / FC2 PPV 1234567 / FC2_1234567 形式的强匹配。</summary>
        private static readonly Regex PrefixedNumberRegex = new Regex(
            @"(?i)FC2[\s_\-]*(?:PPV)?[\s_\-]*(?<number>[0-9]{6,8})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>裸数字编号（前后不能紧邻数字）。</summary>
        private static readonly Regex BareNumberRegex = new Regex(
            @"(?<![0-9])(?<number>[0-9]{6,8})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ArticlePathRegex = new Regex(
            @"article/(?<number>[0-9]{4,9})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>反爬垃圾 token：**ppqojxpos* 这类（星号 + 小写字母数字）。</summary>
        private static readonly Regex MaskTokenRegex = new Regex(
            @"\*+[a-z0-9]{2,}\*?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>标题里残留的 * 掩码。</summary>
        private static readonly Regex MaskStarRegex = new Regex(
            @"\*+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>og:title 前缀 "FC2-PPV-1234567 "。</summary>
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
                    // 站点没有搜索入口，抽不到编号就无法定位条目。
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
                // 下架/不存在同样返回 200，缺 headerInfo 即视为未找到。
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
                    // 列表页之外的边界情况：至少把主缩略图当封面返回。
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

            // 兜底：og:title 里带 FC2-PPV-{number} 说明条目仍然存在（模板小改版时 headerInfo 可能变）。
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

        /// <summary>yyyyMMdd 形态的 8 位数字是日期而不是番号。</summary>
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

        /// <summary>sceneID 由本 handler 编码；也兼容直接传入的裸编号/路径（手工 Identify）。</summary>
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
                // 非 Base58 输入（手工 Identify 直接给番号）会抛 FormatException，按裸值处理。
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

        /// <summary>反爬 span 会在标题里插隐藏文本（zoom:0.01 / display:none），先摘掉。</summary>
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

            // 老模板才有 items_article_Releasedate。
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
        /// 商品説明是懒加载 iframe（/widget/article/{id}/description?ac=…），ac token 与
        /// 访问主页时的会话绑定，所以必须带上 Cookie（HTTP 内共用 CookieContainer）。
        /// 失败时重新抓一次文章页刷新会话再试，最后回落到 meta description。
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
                // 带 cache-buster：插件 HTTP 缓存（60s）可能命中已消费过 ac 的旧文章页。
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

            // 回落：meta description（站点截断到 ~186 字）。
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
