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
    /// TOKYO-HOT; the id looks like n1612 (letter plus digits).
    /// Detail page fields live in the dl list.
    /// The cover is a fixed path (media/{id}/jacket/{id}.jpg); the page itself has none.
    /// </summary>
    public class SiteTokyoHot : IProviderBase
    {
        private const string ProductPath = "/product/";

        private const string CoverTemplate = "https://my.cdn.tokyo-hot.com/media/{0}/jacket/{0}.jpg";

        private static readonly Regex IdRegex = new Regex(@"^(?<id>[a-z_]*[0-9]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex NumberInTextRegex = new Regex(@"\(作品番号[:：]\s*(?<num>[a-z0-9_-]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var movieID = ResolveMovieId(searchTitle);
            if (string.IsNullOrEmpty(movieID))
            {
                return result;
            }

            try
            {
                var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(movieID) + "&x=0&y=0&lang=ja";
                var searchData = await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);
                if (searchData == null)
                {
                    return result;
                }

                var items = searchData.SelectNodesSafe("//*[@id='main']//ul[contains(@class, 'list')]/li");
                if (items.Count == 0)
                {
                    items = searchData.SelectNodesSafe("//*[@id='main']//li[contains(@class, 'detail')]");
                }

                foreach (var item in items)
                {
                    var href = item.SelectSingleText(".//a/@href");
                    if (string.IsNullOrEmpty(href) || !href.Contains("/product/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var sceneURL = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? href
                        : new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), href).ToString();

                    var img = item.SelectSingleText(".//a/img/@src");
                    if (!string.IsNullOrEmpty(img) && !img.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        img = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), img).ToString();
                    }

                    var name = item.SelectSingleText(".//div[@class='title']");
                    if (string.IsNullOrEmpty(name))
                    {
                        name = item.SelectSingleText(".//a/img/@alt");
                    }

                    var number = NumberInTextRegex.Match(item.InnerText).Groups["num"].Value;
                    if (string.IsNullOrEmpty(number))
                    {
                        number = item.SelectSingleText(".//a/img/@alt");
                    }

                    if (string.IsNullOrEmpty(number))
                    {
                        number = ResolveMovieIdFromURL(sceneURL);
                    }

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneURL) } },
                        Name = string.IsNullOrEmpty(number) ? name : $"{number.ToUpperInvariant()} {name}".Trim(),
                        ImageUrl = img,
                    };

                    result.Add(res);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[TokyoHot] Search failed for '{searchTitle}': {e.Message}");
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

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            result.Item.ExternalId = sceneURL;

            var name = sceneData.SelectSingleText("//*[@id='main']//div[@class='contents']/h2");
            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//*[@id='main']//h2[1]");
            }

            if (string.IsNullOrEmpty(name))
            {
                name = sceneData.SelectSingleText("//meta[@property='og:title']/@content");
            }

            result.Item.Name = name.Trim();

            var summaryNode = sceneData.SelectSingleNode("//*[@id='main']//div[@class='sentence']");
            if (summaryNode != null)
            {
                var summary = summaryNode.GetDirectInnerText();
                if (string.IsNullOrWhiteSpace(summary))
                {
                    summary = summaryNode.InnerText;
                }

                result.Item.Overview = Regex.Replace(summary, @"\s+\n\s+", "\n").Trim();
            }

            if (string.IsNullOrEmpty(result.Item.Overview))
            {
                result.Item.Overview = sceneData.SelectSingleText("//meta[@property='og:description']/@content").Trim();
            }

            result.Item.AddStudio("TOKYO-HOT");

            foreach (var info in sceneData.SelectNodesSafe("//*[@id='main']//div[contains(@class, 'infowrapper')]/dl"))
            {
                var keys = info.SelectNodesSafe("./dt");
                var values = info.SelectNodesSafe("./dd");

                for (var i = 0; i < keys.Count && i < values.Count; i++)
                {
                    var label = NormalizeLabel(keys[i].InnerText);
                    var cell = values[i];

                    switch (label)
                    {
                        case "出演者":
                            foreach (var actorLink in cell.SelectNodesSafe(".//a"))
                            {
                                var actorName = actorLink.InnerText.Trim();
                                if (!string.IsNullOrEmpty(actorName) && actorName != "不明")
                                {
                                    result.AddPerson(new PersonInfo { Name = actorName });
                                }
                            }

                            break;

                        case "プレイ内容":
                        case "タグ":
                            foreach (var genreLink in cell.SelectNodesSafe(".//a"))
                            {
                                var genreName = genreLink.InnerText.Trim();
                                if (!string.IsNullOrEmpty(genreName))
                                {
                                    result.Item.AddGenre(genreName);
                                }
                            }

                            break;

                        case "シリーズ":
                        case "レーベル":
                            var studio = cell.InnerText.Trim();
                            if (!string.IsNullOrEmpty(studio))
                            {
                                result.Item.AddStudio(studio);
                            }

                            break;

                        case "配信開始日":
                            var dateText = cell.InnerText.Replace("/", "-", StringComparison.OrdinalIgnoreCase).Trim();
                            if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj) && sceneDateObj > DateTime.MinValue)
                            {
                                result.Item.PremiereDate = sceneDateObj;
                            }

                            break;

                        case "収録時間":
                            if (TimeSpan.TryParse(cell.InnerText.Trim(), CultureInfo.InvariantCulture, out var runtime) && runtime > TimeSpan.Zero)
                            {
                                result.Item.RunTimeTicks = runtime.Ticks;
                            }

                            break;

                        case "作品番号":
                            var number = cell.InnerText.Trim();
                            if (!string.IsNullOrEmpty(number))
                            {
                                result.Item.OriginalTitle = number.ToUpperInvariant();
                            }

                            break;
                    }
                }
            }

            var movieID = ResolveMovieIdFromURL(sceneURL);
            if (string.IsNullOrEmpty(result.Item.OriginalTitle) && !string.IsNullOrEmpty(movieID))
            {
                result.Item.OriginalTitle = movieID.ToUpperInvariant();
            }

            var cover = GetCoverURL(movieID);
            if (!string.IsNullOrEmpty(cover) && await HTTP.IsUrlLoadable(cover, cancellationToken).ConfigureAwait(false))
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
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var movieID = ResolveMovieIdFromURL(sceneURL);
            var cover = GetCoverURL(movieID);
            var hasPrimary = false;

            if (!string.IsNullOrEmpty(cover) && await HTTP.IsUrlLoadable(cover, cancellationToken).ConfigureAwait(false))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
                hasPrimary = true;
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            foreach (var node in sceneData.SelectNodesSafe("//a[@rel='cap']"))
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

                if (!hasPrimary)
                {
                    // Without a jacket, use the first 640x480 still as Primary.
                    result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = img });
                    hasPrimary = true;
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = img });
            }

            return result;
        }

        private static string GetCoverURL(string movieID)
        {
            if (string.IsNullOrEmpty(movieID))
            {
                return string.Empty;
            }

            return string.Format(CultureInfo.InvariantCulture, CoverTemplate, movieID.ToLowerInvariant());
        }

        private static string ResolveMovieId(string searchTitle)
        {
            if (string.IsNullOrEmpty(searchTitle))
            {
                return string.Empty;
            }

            var tokens = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0)
            {
                var match = IdRegex.Match(tokens[0]);
                if (match.Success)
                {
                    return match.Groups["id"].Value.ToLowerInvariant();
                }
            }

            var anyMatch = IdRegex.Match(searchTitle.Trim());
            return anyMatch.Success ? anyMatch.Groups["id"].Value.ToLowerInvariant() : string.Empty;
        }

        private static string ResolveMovieIdFromURL(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var match = Regex.Match(url, @"/product/(?<id>[a-z0-9_]+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : string.Empty;
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
    }
}
