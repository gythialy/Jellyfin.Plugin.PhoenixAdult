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

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// MGS (mgstage.com); scene id is the JAV code.
    /// Detail is /product/product_detail/{id}/ with tr/th fields.
    /// Non-JP IPs get a CloudFront 403, so it uses Fetcher with adc=1 and age_check_done=1.
    /// </summary>
    public class SiteMgsStage : IProviderBase
    {
        private const string ProductPath = "/product/product_detail/";

        private static readonly IDictionary<string, string> DefaultCookies = new Dictionary<string, string>
        {
            { "adc", "1" },
            { "age_check_done", "1" },
        };

        private static readonly Regex NumberInTitleRegex = new Regex(
            @"(?<label>[A-Za-z][A-Za-z0-9]{1,9})[-_ ](?<number>[0-9]{2,5})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RuntimeRegex = new Regex(@"(?<min>[0-9]{1,3})\s*分", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                    var directURL = Helper.GetSearchBaseURL(siteNum) + ProductPath + Uri.EscapeDataString(movieID) + "/";
                    var direct = await Helper.GetSearchResultsFromUpdate(this, siteNum, new[] { Helper.Encode(directURL) }, searchDate, cancellationToken).ConfigureAwait(false);
                    if (direct.Any())
                    {
                        return direct;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"[MGSStage] Direct lookup failed for '{movieID}': {e.Message}");
                }
            }

            try
            {
                var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(movieID.Length > 0 ? movieID : searchTitle);
                var searchData = await Fetcher.Html(url, cancellationToken, null, DefaultCookies).ConfigureAwait(false);

                var items = searchData.SelectNodesSafe("//*[@id='center_column']//ul[contains(@class, 'product_list')]/li");
                if (items.Count == 0)
                {
                    items = searchData.SelectNodesSafe("//ul[contains(@class, 'product_list')]/li");
                }

                foreach (var item in items)
                {
                    var href = item.SelectSingleText(".//h5/a/@href");
                    if (string.IsNullOrEmpty(href))
                    {
                        href = item.SelectSingleText(".//a/@href");
                    }

                    if (string.IsNullOrEmpty(href))
                    {
                        continue;
                    }

                    var sceneURL = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? href
                        : new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), href).ToString();

                    var name = item.SelectSingleText(".//a[contains(@class, 'title')]");
                    if (string.IsNullOrEmpty(name))
                    {
                        name = item.SelectSingleText(".//a/p");
                    }

                    if (string.IsNullOrEmpty(name))
                    {
                        name = item.SelectSingleText(".//h5/a/img/@alt");
                    }

                    var img = item.SelectSingleText(".//h5/a/img/@src");
                    if (string.IsNullOrEmpty(img))
                    {
                        img = item.SelectSingleText(".//img/@src");
                    }

                    if (!string.IsNullOrEmpty(img) && !img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        img = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), img).ToString();
                    }

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneURL) } },
                        Name = name,
                        ImageUrl = img,
                    };

                    var idInHref = ResolveMovieIdFromURL(sceneURL);
                    if (!string.IsNullOrEmpty(idInHref))
                    {
                        res.Name = $"{idInHref} {name}".Trim();
                    }

                    result.Add(res);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[MGSStage] Search failed for '{searchTitle}': {e.Message}");
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

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken, null, DefaultCookies).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            result.Item.ExternalId = sceneURL;

            var name = sceneData.SelectSingleText("//*[@id='center_column']/div[1]/h1");
            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//h1");
            }

            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//meta[@property='og:title']/@content");
            }

            result.Item.Name = name.Trim();

            var overview = sceneData.SelectSingleText("//meta[@property='og:description']/@content");
            if (string.IsNullOrEmpty(overview))
            {
                overview = sceneData.SelectSingleText("//meta[@name='description']/@content");
            }

            result.Item.Overview = overview.Trim();

            var movieID = ResolveMovieIdFromURL(sceneURL);
            if (string.IsNullOrEmpty(movieID))
            {
                movieID = NumberInTitleRegex.Match(sceneData.SelectSingleText("//meta[@property='og:title']/@content")).Value;
            }

            if (!string.IsNullOrEmpty(movieID))
            {
                result.Item.OriginalTitle = movieID;
            }

            result.Item.AddStudio("MGS");

            foreach (var row in sceneData.SelectNodesSafe("//tr"))
            {
                var label = NormalizeLabel(row.SelectSingleText(".//th"));
                var cell = row.SelectSingleNode(".//td");
                if (string.IsNullOrEmpty(label) || cell == null)
                {
                    continue;
                }

                switch (label)
                {
                    case "出演":
                        this.AddActors(result, cell);
                        break;

                    case "メーカー":
                        var maker = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(maker) && !maker.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Item.AddStudio(maker);
                        }

                        break;

                    case "レーベル":
                        var label2 = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(label2) && !label2.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Item.AddStudio(label2);
                        }

                        break;

                    case "シリーズ":
                        var series = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(series) && !series.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Item.AddStudio(series);
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

                    case "収録時間":
                        var runtime = RuntimeRegex.Match(cell.InnerText);
                        if (runtime.Success && int.TryParse(runtime.Groups["min"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
                        {
                            result.Item.RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks;
                        }

                        break;

                    case "品番":
                        var number = cell.InnerText.Trim();
                        if (!string.IsNullOrEmpty(number))
                        {
                            result.Item.OriginalTitle = number;
                        }

                        break;

                    case "配信開始日":
                    case "商品発売日":
                    case "発売日":
                        if (!result.Item.PremiereDate.HasValue)
                        {
                            var dateText = cell.InnerText.Replace("/", "-", StringComparison.OrdinalIgnoreCase).Trim();
                            if (DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                            {
                                result.Item.PremiereDate = sceneDateObj;
                            }
                        }

                        break;

                    case "評価":
                        var scoreText = cell.InnerText.Trim();
                        if (double.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out var score) && score > 0 && score <= 5)
                        {
                            result.Item.CommunityRating = (float)score;
                        }

                        break;
                }
            }

            var coverURL = sceneData.SelectSingleText("//*[@id='EnlargeImage']/@href");
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

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken, null, DefaultCookies).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var cover = sceneData.SelectSingleText("//*[@id='EnlargeImage']/@href");
            if (string.IsNullOrEmpty(cover))
            {
                cover = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(cover) && !cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                cover = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), cover).ToString();
            }

            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var node in sceneData.SelectNodesSafe("//*[@id='sample-photo']/dd/ul/li/a"))
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

        private static string ResolveMovieId(string searchTitle)
        {
            if (string.IsNullOrEmpty(searchTitle))
            {
                return string.Empty;
            }

            if (JavId.TryParse(searchTitle, out var javId) && javId.Contains("-", StringComparison.OrdinalIgnoreCase))
            {
                return javId.ToUpperInvariant();
            }

            var tokens = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && Regex.IsMatch(tokens[0], "^[A-Za-z0-9]{2,10}$") && Regex.IsMatch(tokens[1], "^[0-9]{2,5}$"))
            {
                var id = $"{tokens[0]}-{tokens[1]}";
                if (tokens.Length >= 3 && Regex.IsMatch(tokens[2], "^[A-Za-z]$"))
                {
                    id = $"{id}-{tokens[2]}";
                }

                return id.ToUpperInvariant();
            }

            return string.Empty;
        }

        private static string ResolveMovieIdFromURL(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var segments = url.Split(new[] { '/', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var candidate = segments.LastOrDefault(o => o.Contains("-", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(o, "^[A-Za-z0-9_-]{4,20}$"));
            return candidate != null ? candidate.ToUpperInvariant() : string.Empty;
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
