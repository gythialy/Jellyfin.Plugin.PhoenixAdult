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
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// SOD (ec.sod.co.jp); scene id is the JAV code.
    /// Detail is /videos/?id={id} with fields in the #v_introduction table.
    /// An age gate modal sets a cookie before the page loads.
    /// </summary>
    public class SiteSOD : IProviderBase
    {
        private const string ProductPath = "/videos/?id=";

        private static readonly IDictionary<string, string> DefaultCookies = new Dictionary<string, string>
        {
            { "adc", "1" },
        };

        private static readonly Regex RuntimeRegex = new Regex(@"(?<min>[0-9]{1,3})\s*分", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ScoreRegex = new Regex(@"(?<score>[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var movieID = ResolveMovieId(searchTitle);
            if (!string.IsNullOrEmpty(movieID))
            {
                try
                {
                    var directURL = Helper.GetSearchBaseURL(siteNum) + ProductPath + Uri.EscapeDataString(movieID);
                    var direct = await Helper.GetSearchResultsFromUpdate(this, siteNum, new[] { Helper.Encode(directURL) }, searchDate, cancellationToken).ConfigureAwait(false);
                    if (direct.Any())
                    {
                        return direct;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[SOD] Direct lookup failed for '{movieID}': {e.Message}");
                }
            }

            try
            {
                var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(string.IsNullOrEmpty(movieID) ? searchTitle : movieID);
                var searchData = await FetchPage(url, siteNum, cancellationToken).ConfigureAwait(false);

                var items = searchData.SelectNodesSafe("//*[@id='videos_s_mainbox']");
                if (items.Count == 0)
                {
                    items = searchData.SelectNodesSafe("//div[contains(@class, 'videis_s') or contains(@class, 'videos_s_item')]");
                }

                foreach (var item in items)
                {
                    var href = item.SelectSingleText(".//div[contains(@class, 'videis_s_img')]/a/@href");
                    if (string.IsNullOrEmpty(href))
                    {
                        href = item.SelectSingleText(".//a[contains(@href, 'videos/?id=')]/@href");
                    }

                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var sceneURL = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? href
                        : new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), href).ToString();

                    var thumb = item.SelectSingleText(".//div[contains(@class, 'videis_s_img')]/a/img/@src");
                    if (string.IsNullOrEmpty(thumb))
                    {
                        thumb = item.SelectSingleText(".//img/@src");
                    }

                    if (!string.IsNullOrEmpty(thumb))
                    {
                        if (!thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            thumb = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), thumb).ToString();
                        }

                        if (thumb.Contains("/thumbnail/now_", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    var name = item.SelectSingleText(".//div[contains(@class, 'videis_s_txt')]/h2/a");
                    if (string.IsNullOrEmpty(name))
                    {
                        name = item.SelectSingleText(".//h2/a");
                    }

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneURL) } },
                        Name = name,
                        ImageUrl = thumb,
                    };

                    var sceneDate = item.SelectSingleText(".//div[contains(@class, 'videis_s_star')]/p");
                    if (DateTime.TryParse(sceneDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                    {
                        res.PremiereDate = sceneDateObj;
                    }

                    var idInURL = ResolveMovieIdFromURL(sceneURL);
                    if (!string.IsNullOrEmpty(idInURL))
                    {
                        res.Name = $"{idInURL} {name}".Trim();
                    }

                    result.Add(res);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[SOD] Search failed for '{searchTitle}': {e.Message}");
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
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await FetchPage(sceneURL, siteNum, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            result.Item.ExternalId = sceneURL;

            var name = sceneData.SelectSingleText("//p[@class='product_title']");
            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//h1");
            }

            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//meta[@property='og:title']/@content");
            }

            result.Item.Name = name.Trim();

            var overview = sceneData.SelectSingleText("//div[contains(@class, 'videos_textli')]/article");
            if (string.IsNullOrEmpty(overview))
            {
                overview = sceneData.SelectSingleText("//meta[@property='og:description']/@content");
            }

            result.Item.Overview = overview.Trim();

            var movieID = ResolveMovieIdFromURL(sceneURL);
            if (!string.IsNullOrEmpty(movieID))
            {
                result.Item.OriginalTitle = movieID;
            }

            result.Item.AddStudio("SOD");

            foreach (var row in sceneData.SelectNodesSafe("//*[@id='v_introduction']//tr"))
            {
                var label = NormalizeLabel(row.SelectSingleText(".//td[1]"));
                var cell = row.SelectSingleNode(".//td[2]");
                if (string.IsNullOrEmpty(label) || cell == null)
                {
                    continue;
                }

                switch (label)
                {
                    case "品番":
                        var number = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(number))
                        {
                            result.Item.OriginalTitle = number;
                        }

                        break;

                    case "発売年月日":
                    case "配信開始日":
                        if (!result.Item.PremiereDate.HasValue)
                        {
                            var dateText = cell.InnerText.Replace("年", "-", StringComparison.OrdinalIgnoreCase)
                                .Replace("月", "-", StringComparison.OrdinalIgnoreCase)
                                .Replace("日", string.Empty, StringComparison.OrdinalIgnoreCase)
                                .Replace("/", "-", StringComparison.OrdinalIgnoreCase)
                                .Trim();
                            if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                            {
                                result.Item.PremiereDate = sceneDateObj;
                            }
                        }

                        break;

                    case "シリーズ名":
                        var series = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(series))
                        {
                            result.Item.AddStudio(series);
                        }

                        break;

                    case "出演者":
                        this.AddActors(result, cell);
                        break;

                    case "再生時間":
                        var runtime = RuntimeRegex.Match(cell.InnerText);
                        if (runtime.Success && int.TryParse(runtime.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
                        {
                            result.Item.RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks;
                        }

                        break;

                    case "監督":
                        var director = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(director))
                        {
                            result.AddPerson(new PersonInfo { Name = director, Type = PersonKind.Director });
                        }

                        break;

                    case "メーカー":
                    case "レーベル":
                        var studio = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(studio))
                        {
                            result.Item.AddStudio(studio);
                        }

                        break;

                    case "ジャンル":
                        foreach (var genreLink in cell.SelectNodesSafe(".//a"))
                        {
                            var genreName = genreLink.InnerText.Trim();
                            if (!string.IsNullOrEmpty(genreName))
                            {
                                result.Item.AddGenre(genreName);
                            }
                        }

                        break;
                }
            }

            var scoreText = sceneData.SelectSingleText("//*[@id='review_body']//div[contains(@class, 'imagestar')]/i");
            var scoreMatch = ScoreRegex.Match(scoreText);
            if (scoreMatch.Success && double.TryParse(scoreMatch.Groups["score"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var score) && score > 0 && score <= 5)
            {
                result.Item.CommunityRating = (float)score;
            }

            var coverURL = sceneData.SelectSingleText("//*[@id='videos_toptable']//div[contains(@class, 'videos_samimg')]/a[1]/@href");
            if (string.IsNullOrEmpty(coverURL))
            {
                coverURL = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(coverURL))
            {
                if (!coverURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    coverURL = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), coverURL).ToString();
                }

                result.Item.SetImage(new ItemImageInfo { Path = coverURL, Type = ImageType.Primary }, 0);
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
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await FetchPage(sceneURL, siteNum, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var cover = sceneData.SelectSingleText("//*[@id='videos_toptable']//div[contains(@class, 'videos_samimg')]/a[1]/@href");
            if (string.IsNullOrEmpty(cover))
            {
                cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(cover))
            {
                if (!cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    cover = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), cover).ToString();
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var node in sceneData.SelectNodesSafe("//*[@id='videos_samsbox']/a"))
            {
                var img = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(img))
                {
                    continue;
                }

                if (!img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    img = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), img).ToString();
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = img });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = img });
            }

            return result;
        }

        private static async Task<HtmlAgilityPack.HtmlNode> FetchPage(string url, int[] siteNum, CancellationToken cancellationToken)
        {
            var page = await Fetcher.Html(url, cancellationToken, null, DefaultCookies).ConfigureAwait(false);

            var ageLink = page?.SelectSingleText("//div[contains(@class, 'pkg_age')]//a/@href");
            if (!string.IsNullOrEmpty(ageLink))
            {
                var confirmURL = ageLink.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? ageLink
                    : new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), ageLink).ToString();

                Logger.Info($"[SOD] Age gate detected, confirming: {confirmURL}");
                try
                {
                    var headers = new Dictionary<string, string> { { "Referer", url } };
                    await HTTP.Request(confirmURL, cancellationToken, headers, null).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Error($"[SOD] Age gate confirm failed: {e.Message}");
                }

                page = await Fetcher.Html(url, cancellationToken, null, DefaultCookies).ConfigureAwait(false);
            }

            return page;
        }

        private static string ResolveMovieId(string searchTitle)
        {
            if (string.IsNullOrEmpty(searchTitle))
            {
                return string.Empty;
            }

            var tokens = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && Regex.IsMatch(tokens[0], "^[A-Za-z][A-Za-z0-9]{1,9}$") && Regex.IsMatch(tokens[1], "^[0-9]{2,5}$"))
            {
                var id = $"{tokens[0]}-{tokens[1]}";
                if (tokens.Length >= 3 && Regex.IsMatch(tokens[2], "^[A-Za-z]$"))
                {
                    id = $"{id}-{tokens[2]}";
                }

                return id.ToUpperInvariant();
            }

            if (JavId.TryParse(searchTitle, out var javId) && javId.Contains("-", StringComparison.OrdinalIgnoreCase))
            {
                return javId.ToUpperInvariant();
            }

            return string.Empty;
        }

        private static string ResolveMovieIdFromURL(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var match = Regex.Match(url, @"[?&]id=(?<id>[^&/]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return Uri.UnescapeDataString(match.Groups["id"].Value).ToUpperInvariant();
            }

            return string.Empty;
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

        private void AddActors(MetadataResult<BaseItem> result, HtmlAgilityPack.HtmlNode cell)
        {
            var names = new List<string>();
            var actorNodes = cell.SelectNodesSafe(".//a");
            if (actorNodes.Count > 0)
            {
                names.AddRange(actorNodes.Select(o => o.InnerText.Trim()));
            }
            else
            {
                names.AddRange(cell.InnerText.Split(new[] { '/', '／', '、', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(o => o.Trim()));
            }

            foreach (var actorName in names.Where(o => !string.IsNullOrEmpty(o)))
            {
                var name = actorName;
                switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
                {
                    case JAVActorNamingStyle.JapaneseStyle:
                        name = string.Join(" ", name.Split().Reverse());
                        break;
                }

                result.AddPerson(new PersonInfo { Name = name });
            }
        }
    }
}
