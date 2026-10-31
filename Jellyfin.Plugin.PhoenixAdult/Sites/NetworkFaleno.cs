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
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// Shared implementation for the two FALENO sites: 0=DAHLIA, 1=FALENO.
    /// Both use the same WordPress theme: scene id is the slug (dldss339),
    /// search {base}/?s={keyword} (hyphens break it: dldss-339 misses, dldss339 hits),
    /// detail {base}/works/{slug}/ with fields in .bar02_works / .box_works01_*.
    /// XSERVER answers 403 to CN IPs even through FlareSolverr, so a failure returns an empty result.
    /// </summary>
    public class NetworkFaleno : IProviderBase
    {
        // Code fragment from the title (DLDSS-339 / FSDSS 754 / fcdss072).
        private static readonly Regex JavIdRegex = new Regex(
            @"(?<![A-Za-z0-9])([A-Za-z]{2,10})[-_ ]?([0-9]{2,5})(?![0-9])",
            RegexOptions.CultureInvariant);

        // Slug style code: dldss339 / fsdss754.
        private static readonly Regex SlugRegex = new Regex(
            @"^([A-Za-z]+)([0-9]+)$",
            RegexOptions.CultureInvariant);

        // Runtime "120 min".
        private static readonly Regex RuntimeRegex = new Regex(
            @"([0-9]+)\s*分",
            RegexOptions.CultureInvariant);

        private static readonly string[] DateFormats =
        {
            "yyyy/M/d",
            "yyyy/MM/dd",
            "yyyy-M-d",
            "yyyy-MM-dd",
        };

        private static readonly FalenoSite[] SiteConfigs =
        {
            new FalenoSite
            {
                Maker = "DAHLIA",
            },
            new FalenoSite
            {
                Maker = "FALENO",
            },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || siteNum.Length < 2 || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            try
            {
                var baseUrl = GetBaseUrl(siteNum);
                var searchUrl = Helper.GetSearchSearchURL(siteNum);
                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(searchUrl))
                {
                    return result;
                }

                foreach (var keyword in BuildKeywords(searchTitle))
                {
                    var searchData = await GetPage(searchUrl + Uri.EscapeDataString(keyword), cancellationToken).ConfigureAwait(false);
                    if (searchData == null)
                    {
                        continue;
                    }

                    // Page unavailable (403 or network failure): no point retrying with another keyword.
                    var pageLoaded = searchData.ChildNodes != null && searchData.ChildNodes.Count > 0;

                    foreach (var node in searchData.SelectNodesSafe("//div[@class='box_kanren01']//li[.//div[@class='text_name']/a]"))
                    {
                        var href = MakeAbsolute(baseUrl, node.SelectSingleText(".//div[@class='text_name']/a/@href"));
                        if (string.IsNullOrEmpty(href))
                        {
                            continue;
                        }

                        var path = GetScenePath(href);
                        if (string.IsNullOrEmpty(path))
                        {
                            continue;
                        }

                        var name = node.SelectSingleText(".//div[@class='text_name']/a").Trim();
                        if (string.IsNullOrEmpty(name))
                        {
                            name = node.SelectSingleText(".//img/@alt").Trim();
                        }

                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        var res = new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, Helper.Encode(path) } },
                            Name = name,
                            ImageUrl = StripQuery(MakeAbsolute(baseUrl, node.SelectSingleText(".//img/@src"))),
                        };

                        var premiereDate = ParseReleaseDate(node.SelectSingleText(".//div[contains(text(), '発売開始')]"))
                            ?? searchDate;
                        if (premiereDate.HasValue)
                        {
                            res.PremiereDate = premiereDate;
                        }

                        result.Add(res);
                    }

                    if (result.Count > 0 || !pageLoaded)
                    {
                        break;
                    }
                }

                // Keyword search missed: fetch the detail page by code instead.
                if (result.Count == 0)
                {
                    var slug = ExtractSlug(searchTitle);
                    if (!string.IsNullOrEmpty(slug))
                    {
                        var scenePath = $"/works/{slug}/";
                        var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, new[] { Helper.Encode(scenePath) }, searchDate, cancellationToken).ConfigureAwait(false);
                        if (searchResult != null && searchResult.Any())
                        {
                            result.AddRange(searchResult);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"NetworkFaleno search failed ({searchTitle}): {e.Message}");
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

            if (siteNum == null || siteNum.Length < 2 || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var baseUrl = GetBaseUrl(siteNum);
            if (string.IsNullOrEmpty(baseUrl) || !TryResolveScene(Helper.Decode(sceneID[0]), baseUrl, out var sceneURL, out var slug))
            {
                return result;
            }

            var sceneData = await GetPage(sceneURL, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            // .bar02_works is the current template, .bar02 the old one.
            var title = sceneData.SelectSingleText("//div[@class='bar02_works']/h1");
            if (string.IsNullOrEmpty(title))
            {
                title = sceneData.SelectSingleText("//div[@class='bar02']/h1");
            }

            if (string.IsNullOrEmpty(title))
            {
                // 403, redesign or empty page: let the caller treat it as no metadata, do not throw.
                return result;
            }

            result.Item.Name = title.Trim();
            result.Item.ExternalId = sceneURL;
            result.Item.SetProviderId(Plugin.Instance.Name, Helper.Encode(new Uri(sceneURL).AbsolutePath));
            result.Item.OriginalTitle = NormalizeSlug(slug);

            var site = GetSiteConfig(siteNum);
            if (!string.IsNullOrEmpty(site?.Maker))
            {
                result.Item.AddStudio(site.Maker);
            }

            var summary = sceneData.SelectSingleNode("//div[@class='box_works01_text']")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(summary))
            {
                result.Item.Overview = Regex.Replace(summary, @"\s*\r?\n\s*", "<br />", RegexOptions.CultureInvariant);
            }

            var releaseDate = ParseSceneFields(sceneData, result);
            if (releaseDate.HasValue)
            {
                result.Item.PremiereDate = releaseDate;
                result.Item.ProductionYear = releaseDate.Value.Year;
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (siteNum == null || siteNum.Length < 2 || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var baseUrl = GetBaseUrl(siteNum);
            if (string.IsNullOrEmpty(baseUrl) || !TryResolveScene(Helper.Decode(sceneID[0]), baseUrl, out var sceneURL, out _))
            {
                return result;
            }

            var sceneData = await GetPage(sceneURL, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var cover = StripQuery(MakeAbsolute(baseUrl, sceneData.SelectSingleText("//div[@class='box_works01_img']/a/img/@src")));
            if (string.IsNullOrEmpty(cover))
            {
                cover = StripQuery(MakeAbsolute(baseUrl, sceneData.SelectSingleText("//div[@class='box_works01_img_ie']/img/@src")));
            }

            if (string.IsNullOrEmpty(cover))
            {
                return result;
            }

            result.Add(new RemoteImageInfo
            {
                Type = ImageType.Primary,
                Url = cover,
            });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cover };
            foreach (var node in sceneData.SelectNodesSafe("//div[@class='box_works01_ga']//li/a"))
            {
                var url = StripQuery(MakeAbsolute(baseUrl, node.Attributes["href"]?.Value));
                if (string.IsNullOrEmpty(url) || !seen.Add(url))
                {
                    continue;
                }

                result.Add(new RemoteImageInfo
                {
                    Type = ImageType.Backdrop,
                    Url = url,
                });
            }

            return result;
        }

        // Fetcher.Html throws on error pages; catch it here so Update and GetImages never throw.
        private static async Task<HtmlNode> GetPage(string url, CancellationToken cancellationToken)
        {
            try
            {
                return await Fetcher.Html(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Debug($"NetworkFaleno fetch failed ({url}): {e.Message}");
                return null;
            }
        }

        // slug to code: dldss339 -> DLDSS-339 (JavId would misread it as DLDSS3-39).
        private static string NormalizeSlug(string slug)
        {
            if (string.IsNullOrEmpty(slug))
            {
                return string.Empty;
            }

            var match = SlugRegex.Match(slug);
            return match.Success
                ? $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
                : slug.ToUpperInvariant();
        }

        private static DateTime? ParseSceneFields(HtmlNode sceneData, MetadataResult<BaseItem> result)
        {
            DateTime? releaseDate = null;
            foreach (var span in sceneData.SelectNodesSafe("//div[contains(@class, 'box_works01_list')]//span"))
            {
                if (HasHiddenAncestor(span))
                {
                    continue;
                }

                var value = span.ParentNode?.SelectSingleText(".//p").Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                switch (span.InnerText.Trim())
                {
                    case "出演女優":
                        foreach (var actorName in value.Split(new[] { '/', '／' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var name = actorName.Trim();
                            if (string.IsNullOrEmpty(name))
                            {
                                continue;
                            }

                            switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
                            {
                                case JAVActorNamingStyle.JapaneseStyle:
                                    name = string.Join(" ", name.Split().Reverse());

                                    break;
                            }

                            result.AddPerson(new PersonInfo { Name = name });
                        }

                        break;

                    case "収録時間":
                        var runtime = RuntimeRegex.Match(value);
                        if (runtime.Success
                            && int.TryParse(runtime.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                            && minutes > 0)
                        {
                            result.Item.RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks;
                        }

                        break;

                    case "配信開始日":
                        releaseDate ??= ParseDate(value);

                        break;

                    case "発売日":
                        releaseDate ??= ParseDate(value);

                        break;
                }
            }

            return releaseDate;
        }

        // Hidden rows mark unreleased items; do not read their date.
        private static bool HasHiddenAncestor(HtmlNode node)
        {
            for (var current = node.ParentNode; current != null; current = current.ParentNode)
            {
                var style = current.Attributes?["style"]?.Value;
                if (!string.IsNullOrEmpty(style) && style.Contains("display: none", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> BuildKeywords(string searchTitle)
        {
            var keywords = new List<string>();
            var value = searchTitle.Trim();

            // WordPress search dislikes hyphens: try dldss339, then the raw keyword.
            var match = JavIdRegex.Match(value);
            if (match.Success)
            {
                keywords.Add((match.Groups[1].Value + match.Groups[2].Value).ToLowerInvariant());
            }

            if (keywords.All(o => !string.Equals(o, value, StringComparison.OrdinalIgnoreCase)))
            {
                keywords.Add(value);
            }

            return keywords;
        }

        private static string ExtractSlug(string text)
        {
            var match = JavIdRegex.Match(text ?? string.Empty);
            if (!match.Success)
            {
                return string.Empty;
            }

            return (match.Groups[1].Value + match.Groups[2].Value).ToLowerInvariant();
        }

        private static bool TryResolveScene(string raw, string baseUrl, out string sceneURL, out string slug)
        {
            sceneURL = string.Empty;
            slug = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var value = raw.Trim();
            string path;
            if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                sceneURL = value;
                path = uri.AbsolutePath;
            }
            else if (value.StartsWith("/", StringComparison.Ordinal))
            {
                path = value;
                sceneURL = baseUrl + value;
            }
            else
            {
                // A slug was passed directly.
                slug = value.Trim('/').ToLowerInvariant();
                sceneURL = $"{baseUrl}/works/{slug}/";
                return !string.IsNullOrEmpty(slug);
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return false;
            }

            slug = segments[segments.Length - 1].ToLowerInvariant();
            return !string.IsNullOrEmpty(slug);
        }

        private static string GetScenePath(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : string.Empty;
        }

        private static string GetBaseUrl(int[] siteNum)
            => Helper.GetSearchBaseURL(siteNum)?.TrimEnd('/') ?? string.Empty;

        private static FalenoSite GetSiteConfig(int[] siteNum)
        {
            var index = siteNum[1];
            if (index < 0 || index >= SiteConfigs.Length)
            {
                return SiteConfigs[0];
            }

            return SiteConfigs[index];
        }

        private static string MakeAbsolute(string baseUrl, string url)
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

            return baseUrl + (url.StartsWith("/", StringComparison.Ordinal) ? url : "/" + url);
        }

        private static string StripQuery(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            var index = url.IndexOf('?', StringComparison.Ordinal);
            return index >= 0 ? url.Substring(0, index) : url;
        }

        // Release date from .view_timer on the search page.
        private static DateTime? ParseReleaseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return ParseDate(text.Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim();
            if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : null;
        }

        private sealed class FalenoSite
        {
            public string Maker { get; set; }
        }
    }
}
