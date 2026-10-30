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
    /// FC2PPVDB（fc2ppvdb.com）：FC2-PPV 的作品数据库（女优/标签/收录时间）。
    /// 条目 ID 为纯数字 FC2 号（FC2-PPV-1234567 → 1234567），详情 /articles/{id}。
    /// 站点有 Cloudflare 强校验（直连 403，FlareSolverr 也可能被判 IP 封），
    /// 因此解析同时保留 Go 原版位置选择器与"标签：值"文本兜底两套路径。
    /// </summary>
    public class SiteFC2PPVDB : IProviderBase
    {
        private const string SiteName = "FC2PPVDB";

        private static readonly Regex Fc2NumberRegex = new Regex(
            @"(?:FC2[-_\s]?(?:PPV)?[-_\s]?)?(?<num>[0-9]{6,8})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex ArticleRegex = new Regex(
            @"/articles/(?<id>[0-9]+)",
            RegexOptions.CultureInvariant);

        private static readonly Regex RuntimeRegex = new Regex(
            @"(?:(?<h>[0-9]+)\u6642\u9593)?(?:(?<m>[0-9]+)\u5206)?(?:(?<s>[0-9]+)\u79d2)?",
            RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            try
            {
                var number = ExtractNumber(searchTitle);
                if (!string.IsNullOrEmpty(number))
                {
                    var url = BuildArticleUrl(siteNum, number);
                    var doc = await LoadDocument(url, cancellationToken).ConfigureAwait(false);
                    var title = doc == null ? string.Empty : ExtractTitle(doc);
                    if (!string.IsNullOrEmpty(title))
                    {
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, Helper.Encode(url) } },
                            Name = $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = ExtractCover(doc, url),
                        });
                    }

                    return result;
                }

                var searchUrl = $"{Helper.GetSearchBaseURL(siteNum)}/?s={Uri.EscapeDataString(searchTitle.Trim())}";
                var searchDoc = await LoadDocument(searchUrl, cancellationToken).ConfigureAwait(false);
                if (searchDoc == null)
                {
                    return result;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in searchDoc.SelectNodesSafe("//a[contains(@href,'/articles/')]"))
                {
                    var href = MakeAbsolute(searchUrl, link.GetAttributeValue("href", string.Empty));
                    var match = ArticleRegex.Match(href);
                    if (!match.Success || !seen.Add(match.Groups["id"].Value))
                    {
                        continue;
                    }

                    var title = link.SelectSingleText(".//img/@alt");
                    if (string.IsNullOrEmpty(title))
                    {
                        title = link.InnerText.Trim();
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(href) } },
                        Name = string.IsNullOrEmpty(title) ? $"FC2-{match.Groups["id"].Value} [{SiteName}]" : $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = MakeAbsolute(searchUrl, link.SelectSingleText(".//img/@src")),
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error($"FC2PPVDB search error: \"{searchTitle}\" ({e.Message})");
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

            var id = fields.TryGetValue("ID", out var value) ? value : string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                var match = ArticleRegex.Match(sceneURL);
                id = match.Success ? match.Groups["id"].Value : string.Empty;
            }

            if (!string.IsNullOrEmpty(id))
            {
                movie.OriginalTitle = $"FC2-{ExtractNumber(id) ?? id}";
            }

            var maker = fields.TryGetValue("\u8ca9\u58f2\u8005", out var seller) ? seller : string.Empty;
            movie.AddStudio(string.IsNullOrEmpty(maker) ? SiteName : maker);
            if (!string.IsNullOrEmpty(maker))
            {
                movie.AddCollection(maker);
            }

            if (fields.TryGetValue("\u5973\u512a", out var actresses))
            {
                foreach (var actress in SplitValues(actresses))
                {
                    people.Add(new PersonInfo { Name = actress, Type = PersonKind.Actor });
                }
            }

            if (fields.TryGetValue("\u30bf\u30b0", out var tags))
            {
                foreach (var tag in SplitValues(tags))
                {
                    movie.AddGenre(tag);
                }
            }

            if (fields.TryGetValue("\u8ca9\u58f2\u65e5", out var released) && TryParseDate(released, out var published))
            {
                movie.PremiereDate = published;
                movie.ProductionYear = published.Year;
            }

            if (fields.TryGetValue("\u53ce\u9332\u6642\u9593", out var runtime))
            {
                var span = ParseRuntime(runtime);
                if (span > TimeSpan.Zero)
                {
                    movie.RunTimeTicks = span.Ticks;
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

            foreach (var node in doc.SelectNodesSafe("//main//img/@src"))
            {
                var url = MakeAbsolute(sceneURL, node.GetAttributeValue("src", string.Empty));
                if (string.IsNullOrEmpty(url) || result.Any(o => string.Equals(o.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
            }

            return result;
        }

        private static string BuildArticleUrl(int[] siteNum, string id)
            => $"{Helper.GetSearchBaseURL(siteNum)}/articles/{id}";

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

            var id = ExtractNumber(value);
            return string.IsNullOrEmpty(id) ? string.Empty : BuildArticleUrl(siteNum, id);
        }

        private static async Task<HtmlNode> LoadDocument(string url, CancellationToken cancellationToken)
        {
            try
            {
                var doc = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);
                if (doc == null)
                {
                    return null;
                }

                var title = doc.SelectSingleText("//title");
                return string.IsNullOrEmpty(title) ? null : doc;
            }
            catch (Exception e)
            {
                Logger.Error($"FC2PPVDB load error: \"{url}\" ({e.Message})");
                return null;
            }
        }

        private static string ExtractNumber(string input)
        {
            var match = Fc2NumberRegex.Match(input ?? string.Empty);
            return match.Success ? match.Groups["num"].Value : string.Empty;
        }

        private static string ExtractTitle(HtmlNode doc)
        {
            var title = doc.SelectSingleText("//main//div[contains(@class,'container')]/div[1]/div[2]/h2/a");
            if (!string.IsNullOrEmpty(title))
            {
                return title.Trim();
            }

            title = doc.SelectSingleText("//meta[@property='og:title']/@content");
            if (!string.IsNullOrEmpty(title))
            {
                return title.Trim();
            }

            title = doc.SelectSingleText("//h1");
            if (!string.IsNullOrEmpty(title))
            {
                return title.Trim();
            }

            return doc.SelectSingleText("//title").Trim();
        }

        private static string ExtractCover(HtmlNode doc, string sceneURL)
        {
            var cover = doc.SelectSingleText("//main//div[contains(@class,'container')]/div[1]/div[1]/a/img/@src");
            if (!string.IsNullOrEmpty(cover))
            {
                return MakeAbsolute(sceneURL, cover);
            }

            cover = doc.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(cover))
            {
                return MakeAbsolute(sceneURL, cover);
            }

            foreach (var node in doc.SelectNodesSafe("//main//img/@src"))
            {
                var url = MakeAbsolute(sceneURL, node.GetAttributeValue("src", string.Empty));
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 字段解析：先按 Go 原版（container 内每个 div 的首个子文本节点为标签，值在 span 里）读取，
        /// 再退回"任意元素文本以 '：' 结尾 + 同层 span/链接"的通用扫描。
        /// </summary>
        private static Dictionary<string, string> ReadFields(HtmlNode doc)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in doc.SelectNodesSafe("//main//div[contains(@class,'container')]/div[1]/div[2]/div"))
            {
                var label = node.FirstChild?.NodeType == HtmlNodeType.Text ? node.FirstChild.InnerText.Trim() : string.Empty;
                if (string.IsNullOrEmpty(label) || !label.EndsWith("\uff1a", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = label.TrimEnd('\uff1a').Trim();
                var value = node.SelectSingleText(".//span");
                if (string.IsNullOrEmpty(value))
                {
                    value = node.InnerText.Replace(label, string.Empty, StringComparison.Ordinal).Trim();
                }

                if (!string.IsNullOrEmpty(key) && !fields.ContainsKey(key))
                {
                    fields[key] = value;
                }
            }

            if (fields.Count > 0)
            {
                return fields;
            }

            foreach (var node in doc.SelectNodesSafe("//main//*[self::div or self::li or self::p or self::tr]"))
            {
                var text = node.GetDirectInnerText().Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var separator = text.IndexOf('\uff1a');
                if (separator <= 0 || separator > 12)
                {
                    continue;
                }

                var key = text.Substring(0, separator).Trim();
                var value = text.Substring(separator + 1).Trim();
                if (string.IsNullOrEmpty(value))
                {
                    value = node.SelectSingleText(".//span");
                }

                if (!string.IsNullOrEmpty(key) && !fields.ContainsKey(key))
                {
                    fields[key] = value;
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

            return value.Split(new[] { ',', '\u3001', '/', '\uff0f', '\u00b7', '\uff5c', '|', '\u3000' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim())
                .Where(o => !string.IsNullOrEmpty(o) && o.Length < 40)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static TimeSpan ParseRuntime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TimeSpan.Zero;
            }

            var match = RuntimeRegex.Match(value);
            if (!match.Success || match.Length == 0)
            {
                return TimeSpan.Zero;
            }

            var span = TimeSpan.Zero;
            foreach (var (group, unit) in new[] { ("h", TimeSpan.FromHours(1)), ("m", TimeSpan.FromMinutes(1)), ("s", TimeSpan.FromSeconds(1)) })
            {
                var part = match.Groups[group];
                if (part.Success && int.TryParse(part.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                {
                    span += TimeSpan.FromTicks(unit.Ticks * amount);
                }
            }

            return span;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim().Split(' ')[0];
            var formats = new[] { "yyyy/MM/dd", "yyyy-MM-dd", "yyyy\u5e74MM\u6708dd\u65e5", "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd HH:mm" };
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
