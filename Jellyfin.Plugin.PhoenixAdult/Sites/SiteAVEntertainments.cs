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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// AVEntertainments（aventertainments.com）：面向海外的日系无修正 DVD/配信商店。
    /// 条目 ID 为站内数字商品 id（详情 /dvd/detail?pro={id}）。
    /// 字段用 div.single-info > span.title(标签) + span.value/value-category 读取；
    /// 封面取 og:image，场景截图取 div.gallery-block a.lightbox。
    /// 注意：站内 /dvd/search 会 302 到 /cart/verifybot（Google reCAPTCHA v2），
    /// 无法程序化通过，因此搜索仅在未被拦截时可用，否则返回空列表。
    /// </summary>
    public class SiteAVEntertainments : IProviderBase
    {
        private const string SiteName = "AVEntertainments";

        private static readonly Regex ProIdRegex = new Regex(
            @"(?:pro=|product_id=|AVE[-_\s]?)?(?<id>[0-9]{4,8})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RuntimeMinutesRegex = new Regex(
            @"(?<min>[0-9]+)\s*Min",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex HrefProRegex = new Regex(
            @"[?&](?:pro|product_id)=(?<id>[0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            try
            {
                var proId = ExtractProId(searchTitle);
                if (!string.IsNullOrEmpty(proId))
                {
                    var detailUrl = BuildDetailUrl(siteNum, proId);
                    var doc = await LoadDocument(detailUrl, cancellationToken).ConfigureAwait(false);
                    var title = doc == null ? string.Empty : ExtractTitle(doc);
                    if (!string.IsNullOrEmpty(title))
                    {
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, Helper.Encode(detailUrl) } },
                            Name = $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = ExtractCover(doc, detailUrl),
                        });
                    }

                    return result;
                }

                var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}?lang=2&cat=29&culture=ja-JP&keyword={Uri.EscapeDataString(searchTitle.Trim())}&searchby=keyword";
                var searchPage = await LoadPage(searchUrl, cancellationToken).ConfigureAwait(false);
                if (searchPage == null || searchPage.Value.Blocked)
                {
                    Logger.Info($"AVE search blocked by verifybot, skip: \"{searchTitle}\"");
                    return result;
                }

                var searchDoc = HTML.ElementFromString(searchPage.Value.Content);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in searchDoc.SelectNodesSafe("//div[contains(@class,'single-slider-product')]"))
                {
                    var href = item.SelectSingleText(".//div[1]/a/@href");
                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var url = MakeAbsolute(searchUrl, href);
                    var match = HrefProRegex.Match(url);
                    if (!match.Success || !seen.Add(match.Groups["id"].Value))
                    {
                        continue;
                    }

                    var title = item.SelectSingleText(".//div[2]/p[@class='product-title']/a");
                    if (string.IsNullOrEmpty(title))
                    {
                        title = item.SelectSingleText(".//img/@alt");
                    }

                    var thumb = MakeAbsolute(searchUrl, item.SelectSingleText(".//div[1]/a/img/@src"));

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(url) } },
                        Name = string.IsNullOrEmpty(title) ? $"{match.Groups["id"].Value} [{SiteName}]" : $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = thumb,
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error($"AVE search error: \"{searchTitle}\" ({e.Message})");
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

            var sceneURL = ResolveUrl(siteNum, sceneID[0]);
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            var doc = await LoadDocument(sceneURL, cancellationToken).ConfigureAwait(false);
            if (doc == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            var people = (List<PersonInfo>)result.People;
            movie.ExternalId = sceneURL;
            movie.SetProviderId(Plugin.Instance.Name, Helper.Encode(sceneURL));

            var fields = ReadFields(doc);

            var title = ExtractTitle(doc);
            if (!string.IsNullOrEmpty(title))
            {
                movie.Name = Helper.ParseTitle(title, siteNum);
                movie.OriginalTitle = title;
            }

            if (fields.TryGetValue("\u5546\u54c1\u756a\u53f7", out var number) && !string.IsNullOrEmpty(number))
            {
                movie.OriginalTitle = number;
            }

            var studio = fields.TryGetValue("\u30b9\u30bf\u30b8\u30aa", out var studioValue) ? studioValue : string.Empty;
            movie.AddStudio(string.IsNullOrEmpty(studio) ? SiteName : studio);
            if (!string.IsNullOrEmpty(studio))
            {
                movie.AddCollection(studio);
            }

            if (fields.TryGetValue("\u30b7\u30ea\u30fc\u30ba", out var series) && !string.IsNullOrEmpty(series))
            {
                movie.AddTag(series);
                movie.AddCollection(series);
            }

            if (fields.TryGetValue("\u30ab\u30c6\u30b4\u30ea", out var categories))
            {
                foreach (var genre in SplitValues(categories))
                {
                    movie.AddGenre(genre);
                }
            }

            if (fields.TryGetValue("\u4e3b\u6f14\u5973\u512a", out var actresses))
            {
                foreach (var actress in SplitValues(actresses))
                {
                    people.Add(new PersonInfo { Name = actress, Type = PersonKind.Actor });
                }
            }

            if (fields.TryGetValue("\u767a\u58f2\u65e5", out var released) && TryParseDate(released, out var published))
            {
                movie.PremiereDate = published;
                movie.ProductionYear = published.Year;
            }

            if (fields.TryGetValue("\u53ce\u9332\u6642\u9593", out var runtime))
            {
                var match = RuntimeMinutesRegex.Match(runtime);
                if (match.Success && int.TryParse(match.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
                {
                    movie.RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks;
                }
            }

            var overview = doc.SelectSingleText("//meta[@property='og:description']/@content");
            if (!string.IsNullOrEmpty(overview))
            {
                movie.Overview = overview;
            }

            var cover = ExtractCover(doc, sceneURL);
            if (!string.IsNullOrEmpty(cover))
            {
                movie.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
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

            var sceneURL = ResolveUrl(siteNum, sceneID[0]);
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            var doc = await LoadDocument(sceneURL, cancellationToken).ConfigureAwait(false);
            if (doc == null)
            {
                return result;
            }

            var cover = ExtractCover(doc, sceneURL);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var node in doc.SelectNodesSafe("//div[contains(@class,'gallery-block')]//a[contains(@class,'lightbox')]/@href"))
            {
                var url = MakeAbsolute(sceneURL, node.GetAttributeValue("href", string.Empty));
                if (string.IsNullOrEmpty(url) || result.Any(o => string.Equals(o.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
            }

            return result;
        }

        private static string ExtractProId(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var href = HrefProRegex.Match(input);
            if (href.Success)
            {
                return href.Groups["id"].Value;
            }

            // 纯数字才算站内商品 id（避免把 HEY-063 里的 063 当成 id）
            var trimmed = input.Trim();
            return Regex.IsMatch(trimmed, @"^[0-9]{4,8}$", RegexOptions.CultureInvariant) ? trimmed : string.Empty;
        }

        private static string BuildDetailUrl(int[] siteNum, string proId)
            => $"{Helper.GetSearchBaseURL(siteNum)}/dvd/detail?pro={proId}&lang=2&culture=ja-JP&cat=29";

        private static string ResolveUrl(int[] siteNum, string sceneId)
        {
            var value = Helper.Decode(sceneId);
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var proId = HrefProRegex.Match(value);
            if (proId.Success)
            {
                return BuildDetailUrl(siteNum, proId.Groups["id"].Value);
            }

            return value.StartsWith("/", StringComparison.Ordinal) ? Helper.GetSearchBaseURL(siteNum) + value : string.Empty;
        }

        private static async Task<HtmlNode> LoadDocument(string url, CancellationToken cancellationToken)
        {
            var page = await LoadPage(url, cancellationToken).ConfigureAwait(false);
            if (page == null || page.Value.Blocked || string.IsNullOrWhiteSpace(page.Value.Content))
            {
                return null;
            }

            var doc = HTML.ElementFromString(page.Value.Content);
            var title = doc.SelectSingleText("//title");
            if (string.IsNullOrEmpty(title) || title.Contains("\u30da\u30fc\u30b8\u304c\u898b\u3064\u304b\u308a\u307e\u305b\u3093"))
            {
                return null;
            }

            return doc;
        }

        private static async Task<(string Content, bool Blocked)?> LoadPage(string url, CancellationToken cancellationToken)
        {
            try
            {
                var http = await HTTP.Request(url, cancellationToken).ConfigureAwait(false);
                var finalUrl = http.ResponseUrl?.AbsoluteUri ?? string.Empty;
                var content = http.Content ?? string.Empty;

                if (IsBlocked(finalUrl, content))
                {
                    var solved = await FlareSolverr.GetHtml(url, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(solved) && !IsBlocked(string.Empty, solved))
                    {
                        return (solved, false);
                    }

                    return (content, true);
                }

                if (!http.IsOK)
                {
                    return null;
                }

                return (content, false);
            }
            catch (Exception e)
            {
                Logger.Error($"AVE load error: \"{url}\" ({e.Message})");
                return null;
            }
        }

        private static bool IsBlocked(string finalUrl, string content)
        {
            if (!string.IsNullOrEmpty(finalUrl) && finalUrl.IndexOf("verifybot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return !string.IsNullOrEmpty(content)
                && (content.IndexOf("VerifyBot", StringComparison.OrdinalIgnoreCase) >= 0
                    || content.IndexOf("g-recaptcha", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ExtractTitle(HtmlNode doc)
        {
            var title = doc.SelectSingleText("//*[@id='MyBody']//div[@class='section-title']/h3");
            if (string.IsNullOrEmpty(title))
            {
                title = doc.SelectSingleText("//meta[@property='og:title']/@content");
            }

            if (string.IsNullOrEmpty(title))
            {
                title = doc.SelectSingleText("//title");
            }

            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            title = title.Trim();
            var cut = title.IndexOf(" | AV", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
            {
                title = title.Substring(0, cut);
            }

            var actorCut = title.LastIndexOf(" : ", StringComparison.Ordinal);
            if (actorCut > 0)
            {
                title = title.Substring(0, actorCut);
            }

            return title.Trim();
        }

        private static string ExtractCover(HtmlNode doc, string sceneURL)
        {
            var cover = doc.SelectSingleText("//meta[@property='og:image']/@content");
            if (string.IsNullOrEmpty(cover))
            {
                cover = doc.SelectSingleText("//*[@id='PlayerCover']/img/@src");
            }

            return MakeAbsolute(sceneURL, cover);
        }

        /// <summary>读取 div.single-info 里的标签/值对（商品番号用 span.tag-title）。</summary>
        private static Dictionary<string, string> ReadFields(HtmlNode doc)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in doc.SelectNodesSafe("//div[contains(@class,'single-info')]"))
            {
                var label = block.SelectSingleText(".//span[@class='title']").Trim().TrimEnd(':', '\uff1a').Trim();
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                var valueNode = block.SelectSingleNode(".//span[@class='value' or @class='value-category' or @class='tag-title']");
                var value = valueNode == null ? string.Empty : valueNode.InnerText.Trim();
                var links = block.SelectNodesSafe(".//a").Select(o => o.InnerText.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToList();
                if (links.Count > 0)
                {
                    value = string.Join(", ", links);
                }

                if (!string.IsNullOrEmpty(value) && !fields.ContainsKey(label))
                {
                    fields[label] = value;
                }
            }

            return fields;
        }

        private static IEnumerable<string> SplitValues(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value.Split(new[] { ',', '/', '\u3001', '\uff5c', '|', '\u00b7' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim())
                .Where(o => !string.IsNullOrEmpty(o))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim().Split(' ')[0];
            var formats = new[] { "MM/dd/yyyy", "yyyy/MM/dd", "yyyy-MM-dd", "yyyy\u5e74MM\u6708dd\u65e5" };
            return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static string MakeAbsolute(string baseUrl, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return Uri.TryCreate(new Uri(baseUrl), url, out var absolute) ? absolute.AbsoluteUri : url;
        }
    }
}
