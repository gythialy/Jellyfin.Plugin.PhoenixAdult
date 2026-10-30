using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// DUGA (duga.jp)。条目 ID 是 DUGA 自家的厂牌 slug + 编号（如 glory-4262、waap-1294），
    /// 全小写，并不是标准 JAV 番号；详情页 https://duga.jp/ppv/{id}/。
    /// 站内搜索 /search/=/q={kw}/ 对机房 IP 会返回 503（JS challenge 后仍是 503），
    /// 因此 Search 先用标题里的 slug 直连详情，直连不中再尽力尝试搜索页（失败静默返回空）。
    /// </summary>
    public class SiteDuga : IProviderBase
    {
        private const string MoviePath = "/ppv/";

        // 标题里的候选 slug：厂牌 + 分隔符 + 编号（如 "Glory 4262"、"glory-4262"）。
        private static readonly Regex CandidateRegex = new Regex(
            @"(?<label>[A-Za-z][A-Za-z0-9]{1,9})[\s\-_]+(?<number>[0-9]{3,5})(?![0-9])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RuntimeRegex = new Regex(
            @"(?<min>[0-9]{1,3})\s*分(?:\s*(?<sec>[0-9]{1,2})\s*秒)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex DateRegex = new Regex(
            @"(?<year>[0-9]{4})[年/\-.](?<month>[0-9]{1,2})[月/\-.](?<day>[0-9]{1,2})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex PathIdRegex = new Regex(
            @"/ppv/(?<id>[A-Za-z0-9\-_]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // 文件名里常见的非厂牌 token，避免把 "HD 1080" 之类当成 slug。
        private static readonly HashSet<string> IgnoredLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hd", "fhd", "uhd", "sd", "xxx", "mp4", "mkv", "avi", "wmv", "mov",
            "x264", "x265", "h264", "h265", "hevc", "avc", "web", "webrip", "dl",
            "rip", "ppv", "fc2", "duga", "vol", "part", "cd", "disc", "sample",
            "uncensored", "remaster", "remastered", "version", "full", "clip",
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            var baseURL = Helper.GetSearchBaseURL(siteNum).TrimEnd('/');

            // 1) 标题中能提取出 DUGA slug（glory-4262）时直接打详情页，绕开 503 的搜索页。
            foreach (var movieId in ResolveMovieIds(searchTitle))
            {
                try
                {
                    var sceneURL = $"{baseURL}{MoviePath}{movieId}/";
                    var direct = await Helper.GetSearchResultsFromUpdate(this, siteNum, new[] { Helper.Encode(sceneURL) }, searchDate, cancellationToken).ConfigureAwait(false);
                    if (direct.Any())
                    {
                        result.AddRange(direct);
                        return result;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[DUGA] Direct lookup failed for '{movieId}': {e.Message}");
                }
            }

            // 2) 关键词搜索兜底（该端点经常 503，失败就当没有结果）。
            var searchURL = Helper.GetSearchSearchURL(siteNum);
            if (string.IsNullOrEmpty(searchURL))
            {
                return result;
            }

            try
            {
                var url = searchURL + Uri.EscapeDataString(searchTitle) + "/";
                var searchData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);
                if (searchData == null)
                {
                    return result;
                }

                var cards = searchData.SelectNodesSafe("//*[@id='searchresultarea']//div[contains(@class, 'contentslist')]");
                if (cards.Count == 0)
                {
                    cards = searchData.SelectNodesSafe("//div[contains(@class, 'contentslist')]");
                }

                // 搜索页只取 ID，详情页补全字段（与上游 Go 实现一致，最多 3 条）。
                var ids = new List<string>();
                foreach (var card in cards)
                {
                    var href = card.SelectSingleText(".//a/@href");
                    var id = ParseIdFromHref(href);
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }

                    if (ids.Count >= 3)
                    {
                        break;
                    }
                }

                foreach (var id in ids)
                {
                    try
                    {
                        var sceneURL = $"{baseURL}{MoviePath}{id}/";
                        var direct = await Helper.GetSearchResultsFromUpdate(this, siteNum, new[] { Helper.Encode(sceneURL) }, searchDate, cancellationToken).ConfigureAwait(false);
                        if (direct.Any())
                        {
                            result.AddRange(direct);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"[DUGA] Search detail lookup failed for '{id}': {e.Message}");
                    }
                }

                // 详情补全全部失败但卡片还有标题时，至少把卡片本身当结果返回。
                if (result.Count == 0 && cards.Count > 0)
                {
                    foreach (var card in cards.Take(5))
                    {
                        var id = ParseIdFromHref(card.SelectSingleText(".//a/@href"));
                        if (string.IsNullOrEmpty(id))
                        {
                            continue;
                        }

                        var name = card.SelectSingleText(".//img/@alt");
                        if (string.IsNullOrEmpty(name))
                        {
                            name = card.SelectSingleText(".//*[contains(@class, 'title')]");
                        }

                        if (string.IsNullOrEmpty(name))
                        {
                            name = id.ToUpperInvariant();
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, Helper.Encode($"{baseURL}{MoviePath}{id}/") } },
                            Name = name.Trim(),
                            ImageUrl = NormalizeUrl(card.SelectSingleText(".//img/@src"), baseURL),
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[DUGA] Search failed for '{searchTitle}': {e.Message}");
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

            var name = sceneData.SelectSingleText("//*[@id='contentsname']");
            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//meta[@property='og:title']/@content");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return result;
            }

            var movieId = ParseIdFromURL(sceneURL);
            result.Item.ExternalId = sceneURL;
            result.Item.Name = name.Trim();

            var overview = sceneData.SelectSingleText("//meta[@property='og:description']/@content");
            if (string.IsNullOrEmpty(overview))
            {
                overview = sceneData.SelectSingleText("//div[@class='summarybox']//p");
            }

            result.Item.Overview = overview.Trim();
            result.Item.AddStudio("DUGA");

            string number = null,
                   maker = null,
                   label = null,
                   series = null;

            foreach (var row in sceneData.SelectNodesSafe("//div[contains(@class, 'summaryinner')]//table//tr"))
            {
                var key = NormalizeLabel(row.SelectSingleText("./th"));
                var cell = row.SelectSingleNode("./td");
                if (string.IsNullOrEmpty(key) || cell == null)
                {
                    continue;
                }

                switch (key)
                {
                    case "発売日":
                    case "配信開始日":
                        if (!result.Item.PremiereDate.HasValue)
                        {
                            result.Item.PremiereDate = ParseJapaneseDate(cell.InnerText);
                        }

                        break;

                    case "メーカー":
                        maker = cell.InnerText.Trim();
                        break;

                    case "レーベル":
                        label = cell.InnerText.Trim();
                        break;

                    case "シリーズ":
                        series = cell.InnerText.Trim();
                        break;

                    case "メーカー品番":
                        number = cell.InnerText.Trim();
                        break;

                    case "作品ID":
                        var productId = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(productId))
                        {
                            movieId = productId.ToLowerInvariant();
                        }

                        break;
                }
            }

            if (!string.IsNullOrEmpty(maker))
            {
                result.Item.AddStudio(maker);
            }

            if (!string.IsNullOrEmpty(label))
            {
                result.Item.AddStudio(label);
            }

            if (!string.IsNullOrEmpty(series))
            {
                result.Item.AddStudio(series);
            }

            // メーカー品番 优先，缺失时回落到作品 ID（与上游 Go 一致）。
            if (string.IsNullOrEmpty(number))
            {
                number = (movieId ?? string.Empty).ToUpperInvariant();
            }

            if (!string.IsNullOrEmpty(number))
            {
                result.Item.OriginalTitle = number;
            }

            foreach (var actorNode in sceneData.SelectNodesSafe("//ul[@class='performer']//li//a"))
            {
                var actorName = actorNode.InnerText.Trim();
                if (string.IsNullOrEmpty(actorName))
                {
                    continue;
                }

                switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
                {
                    case JAVActorNamingStyle.JapaneseStyle:
                        actorName = string.Join(" ", actorName.Split().Reverse());
                        break;
                }

                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

            var director = sceneData.SelectSingleText("//ul[@class='director']//li//a");
            if (!string.IsNullOrWhiteSpace(director))
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = director.Trim(), Type = PersonKind.Director });
            }

            foreach (var genreNode in sceneData.SelectNodesSafe("//ul[@class='categorylist']//li//a"))
            {
                // 分类链的根节点 "アダルト" 不是有效标签。
                var genre = genreNode.InnerText.Trim();
                if (!string.IsNullOrEmpty(genre) && !string.Equals(genre, "アダルト", StringComparison.Ordinal))
                {
                    result.Item.AddGenre(genre);
                }
            }

            foreach (var row in sceneData.SelectNodesSafe("//div[contains(@class, 'downloadbox')]//tr"))
            {
                if (!row.SelectSingleText("./th").Contains("再生時間", StringComparison.Ordinal))
                {
                    continue;
                }

                var runtime = RuntimeRegex.Match(row.SelectSingleText("./td"));
                if (runtime.Success
                    && int.TryParse(runtime.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                    && minutes > 0)
                {
                    var seconds = 0;
                    if (runtime.Groups["sec"].Success)
                    {
                        int.TryParse(runtime.Groups["sec"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
                    }

                    result.Item.RunTimeTicks = TimeSpan.FromMinutes(minutes).Add(TimeSpan.FromSeconds(seconds)).Ticks;
                }

                break;
            }

            var cover = sceneData.SelectSingleText("//div[contains(@class, 'imagebox')]/a/@href");
            if (string.IsNullOrEmpty(cover))
            {
                cover = sceneData.SelectSingleText("//img[@id='productjpg']/@src");
            }

            if (string.IsNullOrEmpty(cover))
            {
                cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            cover = NormalizeUrl(cover, baseURL);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            if (result.Item.PremiereDate.HasValue)
            {
                result.Item.ProductionYear = result.Item.PremiereDate.Value.Year;
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
                Logger.Error($"[DUGA] GetImages failed for '{sceneURL}': {e.Message}");
            }

            if (sceneData == null)
            {
                return result;
            }

            var cover = sceneData.SelectSingleText("//div[contains(@class, 'imagebox')]/a/@href");
            if (string.IsNullOrEmpty(cover))
            {
                cover = sceneData.SelectSingleText("//img[@id='productjpg']/@src");
            }

            if (string.IsNullOrEmpty(cover))
            {
                cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            cover = NormalizeUrl(cover, baseURL);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var imageNode in sceneData.SelectNodesSafe("//*[@id='digestthumbbox']/li/a").Take(12))
            {
                var imageURL = NormalizeUrl(imageNode.GetAttributeValue("href", string.Empty), baseURL);
                if (!string.IsNullOrEmpty(imageURL))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = imageURL });
                }
            }

            return result;
        }

        private static string NormalizeLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            return raw.Replace("：", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace(":", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        private static DateTime? ParseJapaneseDate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var match = DateRegex.Match(text);
            if (!match.Success)
            {
                return null;
            }

            if (int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                && int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)
                && month >= 1 && month <= 12 && day >= 1 && day <= 31 && year >= 1900)
            {
                try
                {
                    return new DateTime(year, month, day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            return null;
        }

        private static string ParseIdFromHref(string href)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return string.Empty;
            }

            return ParseIdFromURL(href);
        }

        private static string ParseIdFromURL(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var match = PathIdRegex.Match(url);
            return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : string.Empty;
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

        /// <summary>从文件名/标题里提取所有可能的 DUGA slug（小写，去重，最多 3 个）。</summary>
        private static List<string> ResolveMovieIds(string searchTitle)
        {
            var ids = new List<string>();
            if (string.IsNullOrWhiteSpace(searchTitle))
            {
                return ids;
            }

            foreach (Match match in CandidateRegex.Matches(searchTitle))
            {
                var label = match.Groups["label"].Value;
                var number = match.Groups["number"].Value;
                if (IgnoredLabels.Contains(label))
                {
                    continue;
                }

                if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericValue)
                    && (numericValue >= 1900 && numericValue <= 2099))
                {
                    // 年份，不是编号。
                    continue;
                }

                var id = $"{label.ToLowerInvariant()}-{number}";
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }

                if (ids.Count >= 3)
                {
                    break;
                }
            }

            return ids;
        }
    }
}
