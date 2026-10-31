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

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// Pcolle, a paid self-published store.
    /// Scene id is a 21 character alphanumeric value;
    /// detail /product/detail/?product_id={id} needs the AGE_CONF=1 cookie.
    /// Search entry /product/result/?keyword= yields the same ids.
    /// </summary>
    public class SitePcolle : IProviderBase
    {
        private const string SiteName = "PColle";

        private static readonly Regex ProductIdRegex = new Regex(
            @"(?:PCOLLE[-_\s]?)?(?<id>[a-z0-9]{9,32})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex HrefIdRegex = new Regex(
            @"product_id=(?<id>[A-Za-z0-9]+)",
            RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> AgeCookies = new Dictionary<string, string>
        {
            { "AGE_CONF", "1" },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            try
            {
                var productId = TryExtractProductId(searchTitle);
                if (!string.IsNullOrEmpty(productId))
                {
                    var url = BuildDetailUrl(siteNum, productId);
                    var doc = await LoadDocument(url, cancellationToken).ConfigureAwait(false);
                    var title = doc == null ? string.Empty : ExtractTitle(doc);
                    if (!string.IsNullOrEmpty(title))
                    {
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, Helper.Encode(url) } },
                            Name = $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                            SearchProviderName = Plugin.Instance.Name,
                            ImageUrl = ExtractCover(doc),
                        });
                    }

                    return result;
                }

                var keyword = Uri.EscapeDataString(searchTitle.Trim());
                var searchUrl = $"{Helper.GetSearchBaseURL(siteNum)}/product/result/?keyword={keyword}";
                var searchDoc = await LoadDocument(searchUrl, cancellationToken).ConfigureAwait(false);
                if (searchDoc == null)
                {
                    return result;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var link in searchDoc.SelectNodesSafe("//a[contains(@href,'product_id=')]"))
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    var match = HrefIdRegex.Match(href);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var id = match.Groups["id"].Value.ToLowerInvariant();
                    if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    {
                        continue;
                    }

                    var title = link.SelectSingleText(".//p[@class='desc']");
                    if (string.IsNullOrEmpty(title))
                    {
                        title = link.SelectSingleText(".//img/@alt");
                    }

                    var image = link.SelectSingleText(".//img/@src");
                    var curURL = BuildDetailUrl(siteNum, id);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, Helper.Encode(curURL) } },
                        Name = string.IsNullOrEmpty(title) ? $"{id} [{SiteName}]" : $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = image,
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Pcolle search error: \"{searchTitle}\" ({e.Message})");
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
            movie.ExternalId = sceneURL;
            movie.SetProviderId(Plugin.Instance.Name, Helper.Encode(sceneURL));

            var title = ExtractTitle(doc);
            if (!string.IsNullOrEmpty(title))
            {
                movie.Name = Helper.ParseTitle(title, siteNum);
                movie.OriginalTitle = title;
            }

            var fields = ReadFieldTable(doc);
            if (fields.TryGetValue("\u8ca9\u58f2\u4f1a\u54e1", out var seller) && !string.IsNullOrEmpty(seller))
            {
                movie.AddStudio(seller);
                movie.AddCollection(seller);
            }
            else
            {
                movie.AddStudio(SiteName);
            }

            if (fields.TryGetValue("\u30ab\u30c6\u30b4\u30ea\u30fc", out var category))
            {
                foreach (var genre in SplitValues(category))
                {
                    movie.AddGenre(genre);
                }
            }

            if (fields.TryGetValue("\u8ca9\u58f2\u958b\u59cb\u65e5", out var released) && TryParseDate(released, out var published))
            {
                movie.PremiereDate = published;
                movie.ProductionYear = published.Year;
            }

            var overview = doc.SelectSingleText("//meta[@property='og:description']/@content");
            if (string.IsNullOrEmpty(overview) && fields.TryGetValue("\u88dc\u8db3\u60c5\u5831", out var extra))
            {
                overview = extra;
            }

            if (!string.IsNullOrEmpty(overview))
            {
                movie.Overview = overview;
            }

            var cover = ExtractCover(doc);
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

            foreach (var href in ReadGallery(doc))
            {
                var type = result.Count == 0 ? ImageType.Primary : ImageType.Backdrop;
                result.Add(new RemoteImageInfo { Type = type, Url = href });
                if (type == ImageType.Primary)
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = href });
                }
            }

            return result;
        }

        private static string TryExtractProductId(string title)
        {
            var match = ProductIdRegex.Match(title ?? string.Empty);
            if (!match.Success)
            {
                return string.Empty;
            }

            // Skip all-digit values: those are code numbers from other sites.
            var id = match.Groups["id"].Value.ToLowerInvariant();
            return id.Length >= 9 && id.Any(char.IsLetter) ? id : string.Empty;
        }

        private static string BuildDetailUrl(int[] siteNum, string productId)
            => $"{Helper.GetSearchBaseURL(siteNum)}/product/detail/?product_id={Uri.EscapeDataString(productId)}";

        private static string ResolveUrl(int[] siteNum, string sceneId)
        {
            var url = Helper.Decode(sceneId);
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return url.StartsWith("/", StringComparison.Ordinal) ? Helper.GetSearchBaseURL(siteNum) + url : string.Empty;
        }

        private static async Task<HtmlNode> LoadDocument(string url, CancellationToken cancellationToken)
        {
            try
            {
                var doc = await Fetcher.Html(url, cancellationToken, null, AgeCookies).ConfigureAwait(false);
                if (doc == null)
                {
                    return null;
                }

                var title = doc.SelectSingleText("//title");
                if (string.IsNullOrEmpty(title) || title.Contains("\u30da\u30fc\u30b8\u304c\u898b\u3064\u304b\u308a\u307e\u305b\u3093"))
                {
                    return null;
                }

                return doc;
            }
            catch (Exception e)
            {
                Logger.Error($"Pcolle load error: \"{url}\" ({e.Message})");
                return null;
            }
        }

        private static string ExtractTitle(HtmlNode doc)
        {
            // The product name in the detail table is the cleanest field.
            var fields = ReadFieldTable(doc);
            if (fields.TryGetValue("\u5546\u54c1\u540d", out var productName) && !string.IsNullOrWhiteSpace(productName))
            {
                return productName.Trim();
            }

            var title = doc.SelectSingleText("//meta[@property='og:title']/@content");
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            var cut = title.IndexOf(" | Pcolle", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
            {
                title = title.Substring(0, cut);
            }

            return title.Trim();
        }

        private static string ExtractCover(HtmlNode doc)
        {
            var cover = ReadGallery(doc).FirstOrDefault();
            if (!string.IsNullOrEmpty(cover))
            {
                return cover;
            }

            return doc.SelectSingleText("//meta[@property='og:image']/@content");
        }

        private static List<string> ReadGallery(HtmlNode doc)
        {
            var images = new List<string>();
            foreach (var node in doc.SelectNodesSafe("//li/a[contains(@rel,'lightbox')]/@href"))
            {
                var href = node.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(href) && !images.Contains(href))
                {
                    images.Add(href);
                }
            }

            return images;
        }

        private static Dictionary<string, string> ReadFieldTable(HtmlNode doc)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in doc.SelectNodesSafe("//table//tr"))
            {
                var key = row.SelectSingleText(".//th").Trim().TrimEnd(':', '\uff1a').Trim();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var value = row.SelectSingleText(".//td").Trim();
                if (!string.IsNullOrEmpty(value) && !fields.ContainsKey(key))
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

            return value.Split(new[] { ',', '\u3001', '/', '\uff0f', '\u00b7', '\uff5c', '|' }, StringSplitOptions.RemoveEmptyEntries)
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

            var text = value.Trim();
            var formats = new[] { "yyyy/MM/dd", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd", "yyyy\u5e74MM\u6708dd\u65e5", "yyyy/MM/dd HH:mm" };
            return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
