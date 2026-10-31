using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// Getchu, a doujin store; the scene id is the numeric product id (/i/item{num}).
    /// Pages are EUC-JP: decoded by charset already, and re-decoded from the raw bytes when that garbles.
    /// The site search answers 403 to non-JP IPs, so Search needs the id in the title.
    /// </summary>
    public class SiteGetchu : IProviderBase
    {
        private const string SiteName = "Getchu";

        private static readonly Regex ItemIdRegex = new Regex(
            @"(?:GETCHU[-_\s]?|ITEM)?(?<num>\d{5,8})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex RuntimeRegex = new Regex(@"([0-9]+)\u5206([0-9]+)\u79d2", RegexOptions.CultureInvariant);

        private static readonly string[] GenreLabels = { "\u30ab\u30c6\u30b4\u30ea", "\u8da3\u5411", "\u4f5c\u54c1\u7a2e\u5225" };

        static SiteGetchu()
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch (Exception)
            {
                // Already registered or unsupported: ignore.
            }
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrWhiteSpace(searchTitle))
            {
                return result;
            }

            try
            {
                var itemId = ExtractItemId(searchTitle);
                if (string.IsNullOrEmpty(itemId))
                {
                    return result;
                }

                var url = BuildItemUrl(siteNum, itemId);
                var doc = await LoadDocument(url, cancellationToken).ConfigureAwait(false);
                var title = doc == null ? string.Empty : ExtractTitle(doc);
                if (string.IsNullOrEmpty(title))
                {
                    return result;
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(url) } },
                    Name = $"{Helper.ParseTitle(title, siteNum)} [{SiteName}]",
                    SearchProviderName = Plugin.Instance.Name,
                    ImageUrl = ExtractCover(doc),
                });
            }
            catch (Exception e)
            {
                Logger.Error($"Getchu search error: \"{searchTitle}\" ({e.Message})");
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

            var summary = doc.SelectSingleText("//meta[@name='description']/@content");
            if (!string.IsNullOrEmpty(summary))
            {
                movie.Overview = summary;
            }

            var studio = GetField(doc, "\u30b5\u30fc\u30af\u30eb");
            if (string.IsNullOrEmpty(studio))
            {
                studio = GetField(doc, "\u4f5c\u8005");
            }

            movie.AddStudio(string.IsNullOrEmpty(studio) ? SiteName : studio);
            if (!string.IsNullOrEmpty(studio))
            {
                movie.AddCollection(studio);
            }

            foreach (var label in GenreLabels)
            {
                foreach (var genre in GetFieldLinks(doc, label))
                {
                    movie.AddGenre(genre);
                }
            }

            var releaseDate = GetField(doc, "\u914d\u4fe1\u958b\u59cb\u65e5");
            if (DateTime.TryParseExact(releaseDate, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var published))
            {
                movie.PremiereDate = published;
                movie.ProductionYear = published.Year;
            }

            var lengthText = GetField(doc, "\u753b\u50cf\u6570&\u30da\u30fc\u30b8\u6570");
            var length = RuntimeRegex.Match(lengthText);
            if (length.Success)
            {
                movie.RunTimeTicks = TimeSpan.FromMinutes(int.Parse(length.Groups[1].Value, CultureInfo.InvariantCulture))
                    .Add(TimeSpan.FromSeconds(int.Parse(length.Groups[2].Value, CultureInfo.InvariantCulture))).Ticks;
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

            var cover = ExtractCover(doc);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var node in doc.SelectNodesSafe("//a[contains(@class,'highslide')]"))
            {
                var href = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var url = MakeAbsolute(sceneURL, href);
                if (!result.Any(o => string.Equals(o.Url, url, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
                }
            }

            return result;
        }

        private static string ExtractItemId(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            var match = ItemIdRegex.Match(title);
            return match.Success ? match.Groups["num"].Value : string.Empty;
        }

        private static string BuildItemUrl(int[] siteNum, string itemId)
            => $"{Helper.GetSearchBaseURL(siteNum)}/i/item{itemId}";

        private static string ResolveUrl(int[] siteNum, string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId))
            {
                return string.Empty;
            }

            var url = Helper.Decode(sceneId);
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            return url.StartsWith("/", StringComparison.Ordinal) ? Helper.GetSearchBaseURL(siteNum) + url : url;
        }

        private static async Task<HtmlNode> LoadDocument(string url, CancellationToken cancellationToken)
        {
            try
            {
                var http = await HTTP.Request(url, cancellationToken).ConfigureAwait(false);
                if (!http.IsOK)
                {
                    return null;
                }

                var content = DecodeContent(http);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                var doc = HTML.ElementFromString(content);
                var title = doc.SelectSingleText("//title");
                if (string.IsNullOrEmpty(title) || title.Contains("\u898b\u3064\u304b\u308a\u307e\u305b\u3093"))
                {
                    return null;
                }

                return doc;
            }
            catch (Exception e)
            {
                Logger.Error($"Getchu load error: \"{url}\" ({e.Message})");
                return null;
            }
        }

        private static string DecodeContent(HTTP.HTTPResponse http)
        {
            var content = http.Content ?? string.Empty;
            if (http.ContentStream == null)
            {
                return content;
            }

            try
            {
                var encoding = Encoding.GetEncoding("EUC-JP");
                if (http.ContentStream.CanSeek)
                {
                    http.ContentStream.Position = 0;
                }

                using (var reader = new StreamReader(http.ContentStream, encoding, true, 1024, true))
                {
                    var decoded = reader.ReadToEnd();
                    if (!string.IsNullOrEmpty(decoded) && decoded.IndexOf('\ufffd') < 0)
                    {
                        return decoded;
                    }
                }
            }
            catch (Exception)
            {
                // Codepage unavailable: keep the decoded content.
            }

            return content;
        }

        private static string ExtractTitle(HtmlNode doc)
        {
            var title = doc.SelectSingleText("//meta[@property='og:title']/@content");
            if (string.IsNullOrEmpty(title))
            {
                title = doc.SelectSingleText("//title");
                var cut = title.IndexOf("DL.Getchu.com", StringComparison.OrdinalIgnoreCase);
                if (cut > 0)
                {
                    title = title.Substring(0, cut);
                }

                title = title.Trim().TrimEnd('|', '-', ' ').Trim();
            }

            return title;
        }

        private static string ExtractCover(HtmlNode doc)
            => doc.SelectSingleText("//meta[@property='og:image']/@content");

        private static HtmlNode GetFieldNode(HtmlNode doc, string label)
        {
            var cell = doc.SelectSingleNode($"//td[contains(@class,'bluetext')][normalize-space(.)='{label}']");
            return cell?.SelectSingleNode("following-sibling::td[1]");
        }

        private static string GetField(HtmlNode doc, string label)
            => GetFieldNode(doc, label)?.InnerText.Trim() ?? string.Empty;

        private static List<string> GetFieldLinks(HtmlNode doc, string label)
            => GetFieldNode(doc, label)?.SelectNodesSafe(".//a")
                .Select(o => o.InnerText.Trim())
                .Where(o => !string.IsNullOrEmpty(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

        private static string MakeAbsolute(string baseUrl, string url)
        {
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return Uri.TryCreate(new Uri(baseUrl), url, out var absolute) ? absolute.AbsoluteUri : url;
        }
    }
}
