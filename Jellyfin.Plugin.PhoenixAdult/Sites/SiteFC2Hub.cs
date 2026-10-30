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
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// FC2Hub（javten.com）：FC2-PPV 的镜像聚合站。
    /// 条目 ID 为双段 "javtenVideoId-fc2Number"（如 4680562-4680562），
    /// 详情 /video/{a}/id{b}/；搜索 /search?kw={num}，命中时站点会 302 跳到视频页。
    /// 元数据以详情页 JSON-LD（@type=Movie/CreativeWorkSeries）为主，HTML 选择器兜底。
    /// 站点有 Cloudflare，统一走 Fetcher/FlareSolverr。
    /// </summary>
    public class SiteFC2Hub : IProviderBase
    {
        private const string SiteName = "FC2Hub";

        private static readonly Regex Fc2NumberRegex = new Regex(
            @"(?:FC2[-_\s]?(?:PPV)?[-_\s]?)?(?<num>[0-9]{6,8})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex VideoPathRegex = new Regex(
            @"/video/(?<video>[0-9]+)/id(?<num>[0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex IsoDurationRegex = new Regex(
            @"^P(?:(?<d>[0-9]+)D)?(?:T(?:(?<h>[0-9]+)H)?(?:(?<m>[0-9]+)M)?(?:(?<s>[0-9]+)S)?)?$",
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
                var hit = VideoPathRegex.Match(searchTitle);
                if (hit.Success)
                {
                    var directUrl = $"{Helper.GetSearchBaseURL(siteNum)}/video/{hit.Groups["video"].Value}/id{hit.Groups["num"].Value}/";
                    return await ParseSingleResult(siteNum, directUrl, cancellationToken).ConfigureAwait(false);
                }

                var number = ExtractNumber(searchTitle);
                if (string.IsNullOrEmpty(number))
                {
                    return result;
                }

                var searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(number);
                var page = await LoadPage(searchUrl, cancellationToken).ConfigureAwait(false);
                if (page == null)
                {
                    return result;
                }

                // 命中时 javten 会 302 到 /video/{a}/id{b}/，此时整页就是详情页。
                var finalUrl = page.Value.Url;
                if (!string.IsNullOrEmpty(finalUrl) && VideoPathRegex.IsMatch(finalUrl))
                {
                    return await ParseSingleResult(siteNum, finalUrl, cancellationToken).ConfigureAwait(false);
                }

                var doc = HTML.ElementFromString(page.Value.Content);
                if (VideoPathRegex.IsMatch(doc.SelectSingleText("//link[@rel='canonical']/@href")))
                {
                    var canonical = doc.SelectSingleText("//link[@rel='canonical']/@href");
                    return await ParseSingleResult(siteNum, MakeAbsolute(searchUrl, canonical), cancellationToken).ConfigureAwait(false);
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in doc.SelectNodesSafe("//a[contains(@href,'/video/')]"))
                {
                    var href = MakeAbsolute(searchUrl, link.GetAttributeValue("href", string.Empty));
                    var path = VideoPathRegex.Match(href);
                    if (!path.Success || !seen.Add(path.Value))
                    {
                        continue;
                    }

                    var title = link.SelectSingleText(".//img/@alt");
                    if (string.IsNullOrEmpty(title))
                    {
                        title = link.InnerText.Trim();
                    }

                    var image = MakeAbsolute(searchUrl, link.SelectSingleText(".//img/@src"));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(href) } },
                        Name = string.IsNullOrEmpty(title) ? $"FC2-{path.Groups["num"].Value} [{SiteName}]" : $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = image,
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error($"FC2Hub search error: \"{searchTitle}\" ({e.Message})");
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

            var ld = ReadJsonLd(doc);
            var title = ld.Title;
            if (string.IsNullOrEmpty(title))
            {
                title = doc.SelectSingleText("//h1");
            }

            if (!string.IsNullOrEmpty(title))
            {
                movie.Name = Helper.ParseTitle(title, siteNum);
                movie.OriginalTitle = title;
            }

            var summary = ld.Description;
            if (string.IsNullOrEmpty(summary))
            {
                summary = doc.SelectSingleText("//meta[@property='og:description']/@content");
            }

            if (!string.IsNullOrEmpty(summary))
            {
                movie.Overview = summary;
            }

            var number = ld.Number;
            if (string.IsNullOrEmpty(number))
            {
                var canonical = VideoPathRegex.Match(doc.SelectSingleText("//link[@rel='canonical']/@href"));
                if (canonical.Success)
                {
                    number = canonical.Groups["num"].Value;
                }
            }

            if (!string.IsNullOrEmpty(number))
            {
                movie.OriginalTitle = $"FC2-{number}";
            }

            var maker = ld.Maker;
            if (string.IsNullOrEmpty(maker))
            {
                maker = SiteName;
            }

            movie.AddStudio(maker);
            movie.AddCollection(maker);

            foreach (var genre in ld.Genres)
            {
                movie.AddGenre(genre);
            }

            if (ld.Rating > 0)
            {
                movie.CommunityRating = (float)ld.Rating;
            }

            foreach (var actor in ld.Actors)
            {
                people.Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
            }

            if (DateTime.TryParse(ld.ReleaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var published))
            {
                movie.PremiereDate = published;
                movie.ProductionYear = published.Year;
            }

            if (ld.Runtime != TimeSpan.Zero)
            {
                movie.RunTimeTicks = ld.Runtime.Ticks;
            }

            var cover = ld.Image;
            if (string.IsNullOrEmpty(cover))
            {
                cover = doc.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(cover))
            {
                movie.SetImage(new ItemImageInfo { Path = MakeAbsolute(sceneURL, cover), Type = ImageType.Primary }, 0);
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

            var ld = ReadJsonLd(doc);
            var cover = ld.Image;
            if (string.IsNullOrEmpty(cover))
            {
                cover = doc.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(cover))
            {
                var url = MakeAbsolute(sceneURL, cover);
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = url });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
            }

            foreach (var node in doc.SelectNodesSafe("//a[@data-fancybox='gallery']/@href"))
            {
                var url = MakeAbsolute(sceneURL, node.GetAttributeValue("href", string.Empty));
                if (!string.IsNullOrEmpty(url) && !result.Any(o => string.Equals(o.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
                }
            }

            return result;
        }

        private static async Task<List<RemoteSearchResult>> ParseSingleResult(int[] siteNum, string sceneURL, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (string.IsNullOrEmpty(sceneURL))
            {
                return result;
            }

            var doc = await LoadDocument(sceneURL, cancellationToken).ConfigureAwait(false);
            if (doc == null)
            {
                return result;
            }

            var ld = ReadJsonLd(doc);
            var title = ld.Title;
            if (string.IsNullOrEmpty(title))
            {
                title = doc.SelectSingleText("//h1");
            }

            if (string.IsNullOrEmpty(title))
            {
                return result;
            }

            result.Add(new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneURL) } },
                Name = $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                SearchProviderName = Plugin.Instance.Name,
                ImageUrl = string.IsNullOrEmpty(ld.Image) ? doc.SelectSingleText("//meta[@property='og:image']/@content") : ld.Image,
            });

            return result;
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
                Logger.Error($"FC2Hub load error: \"{url}\" ({e.Message})");
                return null;
            }
        }

        private static async Task<(string Url, string Content)?> LoadPage(string url, CancellationToken cancellationToken)
        {
            try
            {
                var http = await HTTP.Request(url, cancellationToken).ConfigureAwait(false);
                if (http.IsOK && !Fetcher.IsCloudflareChallenge(http.Content) && !string.IsNullOrWhiteSpace(http.Content))
                {
                    return (http.ResponseUrl?.AbsoluteUri ?? url, http.Content);
                }

                var solved = await FlareSolverr.GetHtml(url, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(solved) && !Fetcher.IsCloudflareChallenge(solved))
                {
                    return (url, solved);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"FC2Hub page error: \"{url}\" ({e.Message})");
            }

            return null;
        }

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

            var pair = Regex.Match(value, @"^(?<video>[0-9]+)[-_](?<num>[0-9]+)$", RegexOptions.CultureInvariant);
            if (pair.Success)
            {
                return $"{Helper.GetSearchBaseURL(siteNum)}/video/{pair.Groups["video"].Value}/id{pair.Groups["num"].Value}/{pair.Groups["num"].Value}";
            }

            return string.Empty;
        }

        private static string ExtractNumber(string input)
        {
            var match = Fc2NumberRegex.Match(input ?? string.Empty);
            return match.Success ? match.Groups["num"].Value : string.Empty;
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

        private static JsonLdInfo ReadJsonLd(HtmlNode doc)
        {
            var info = new JsonLdInfo();
            foreach (var script in doc.SelectNodesSafe("//script[@type='application/ld+json']"))
            {
                try
                {
                    var token = JToken.Parse(script.InnerText);
                    var objects = token is JArray array
                        ? array.Children<JObject>().ToList()
                        : new List<JObject> { token as JObject };
                    foreach (var item in objects)
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        var type = item.Value<string>("@type");
                        if (string.Equals(type, "Movie", StringComparison.OrdinalIgnoreCase))
                        {
                            info.Title = item.Value<string>("name") ?? info.Title;
                            info.Description = item.Value<string>("description") ?? info.Description;
                            info.Image = item.Value<string>("image") ?? info.Image;
                            info.ReleaseDate = item.Value<string>("datePublished") ?? info.ReleaseDate;
                            info.Maker = item.Value<string>("director") ?? info.Maker;
                            info.Runtime = ParseDuration(item.Value<string>("duration"));

                            foreach (var identifier in ToStrings(item["identifier"]))
                            {
                                var number = ExtractNumber(identifier);
                                if (!string.IsNullOrEmpty(number))
                                {
                                    info.Number = number;
                                    break;
                                }
                            }

                            foreach (var actor in ToStrings(item["actor"]))
                            {
                                if (!string.IsNullOrEmpty(actor) && !info.Actors.Contains(actor))
                                {
                                    info.Actors.Add(actor);
                                }
                            }

                            foreach (var genre in ToStrings(item["genre"]))
                            {
                                if (!string.IsNullOrEmpty(genre) && !info.Genres.Contains(genre))
                                {
                                    info.Genres.Add(genre);
                                }
                            }
                        }
                        else if (string.Equals(type, "CreativeWorkSeries", StringComparison.OrdinalIgnoreCase) && info.Rating <= 0)
                        {
                            info.Rating = item.SelectToken("aggregateRating.ratingValue")?.Value<double?>() ?? 0;
                        }
                    }
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    continue;
                }
            }

            return info;
        }

        private static IEnumerable<string> ToStrings(JToken token)
        {
            if (token == null)
            {
                yield break;
            }

            var items = token is JArray array
                ? array.Children().ToList()
                : new List<JToken> { token };
            foreach (var item in items)
            {
                var value = item is JObject obj ? obj.Value<string>("name") : item.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value.Trim();
                }
            }
        }

        private static TimeSpan ParseDuration(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return TimeSpan.Zero;
            }

            var match = IsoDurationRegex.Match(value.Trim());
            if (!match.Success)
            {
                return TimeSpan.Zero;
            }

            var span = TimeSpan.Zero;
            foreach (var (group, unit) in new[] { ("d", TimeSpan.FromDays(1)), ("h", TimeSpan.FromHours(1)), ("m", TimeSpan.FromMinutes(1)), ("s", TimeSpan.FromSeconds(1)) })
            {
                var part = match.Groups[group];
                if (part.Success && int.TryParse(part.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                {
                    span += TimeSpan.FromTicks(unit.Ticks * amount);
                }
            }

            return span;
        }

        private sealed class JsonLdInfo
        {
            public string Title { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

            public string Image { get; set; } = string.Empty;

            public string Number { get; set; } = string.Empty;

            public string Maker { get; set; } = string.Empty;

            public string ReleaseDate { get; set; } = string.Empty;

            public TimeSpan Runtime { get; set; }

            public double Rating { get; set; }

            public List<string> Actors { get; } = new List<string>();

            public List<string> Genres { get; } = new List<string>();
        }
    }
}
