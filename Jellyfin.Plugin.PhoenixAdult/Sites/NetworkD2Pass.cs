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
using Newtonsoft.Json.Linq;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// D2Pass 0930 家族（c0930 / h0930 / h4610）：同一套 moviepages/{id}/index.html 模板，
    /// JSON-LD + #movieInfo dl + #movieContent poster + #movieGallery 图集。
    /// 注意：2026-10 起该家族整站 403 维护，恢复后需重新验证。
    /// </summary>
    public class NetworkD2Pass : IProviderBase
    {
        private static readonly Regex IdRegex = new Regex(@"^(?:(?:c0930|h0930|h4610)[-_])?(?<id>[A-Za-z0-9]{2,20})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var match = IdRegex.Match(searchTitle.Trim().Split(' ').First());
            if (!match.Success)
            {
                return result;
            }

            var id = match.Groups["id"].Value.ToLowerInvariant();
            var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/moviepages/{id}/index.html";
            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);

            var title = this.GetTitle(sceneData);
            if (string.IsNullOrEmpty(title))
            {
                return result;
            }

            result.Add(new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, Helper.Encode(new Uri(sceneURL).AbsolutePath) } },
                Name = $"{id.ToUpperInvariant()} {title}",
            });

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            var id = new Uri(sceneURL).Segments.Reverse().Skip(1).FirstOrDefault()?.Trim('/') ?? string.Empty;

            result.Item.Name = this.GetTitle(sceneData);
            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio(Helper.GetSearchSiteName(siteNum));

            var jsonLd = this.ParseJsonLd(sceneData);
            if (jsonLd != null)
            {
                result.Item.Overview = (string)jsonLd["description"];
                result.Item.PremiereDate = ParseDate((string)jsonLd["releasedEvent"]?["startDate"]);
                var actor = (string)jsonLd["video"]?["actor"];
                if (!string.IsNullOrEmpty(actor))
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
                }

                var provider = (string)jsonLd["video"]?["provider"];
                if (!string.IsNullOrEmpty(provider))
                {
                    result.Item.AddStudio(provider);
                }
            }

            var cover = sceneData.SelectSingleText("//*[@id='movieContent']/@poster");
            if (string.IsNullOrEmpty(cover))
            {
                cover = (string)this.ParseJsonLd(sceneData)?["image"];
            }

            if (!string.IsNullOrEmpty(cover))
            {
                if (!cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    cover = Helper.GetSearchBaseURL(siteNum) + cover;
                }

                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
            }

            foreach (var dt in sceneData.SelectNodesSafe("//*[@id='movieInfo']//section/dl/dt"))
            {
                var label = dt.InnerText.Trim();
                var dd = dt.SelectSingleNode("following-sibling::dd[1]");
                var value = dd?.InnerText.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (label == "公開日" && !result.Item.PremiereDate.HasValue)
                {
                    result.Item.PremiereDate = ParseDate(value);
                }
                else if (label == "プレイ内容")
                {
                    foreach (var genre in value.Split('\u00a0'))
                    {
                        var name = genre.Trim();
                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Item.AddGenre(name);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(id))
            {
                result.Item.SetProviderId(Plugin.Instance.Name, Helper.Encode(new Uri(sceneURL).AbsolutePath));
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken).ConfigureAwait(false);
            var cover = sceneData.SelectSingleText("//*[@id='movieContent']/@poster");
            if (!string.IsNullOrEmpty(cover))
            {
                if (!cover.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    cover = Helper.GetSearchBaseURL(siteNum) + cover;
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var script in sceneData.SelectNodesSafe("//*[@id='movieGallery']//script"))
            {
                foreach (Match href in Regex.Matches(script.InnerText, "href=\"(.+?)\""))
                {
                    var url = href.Groups[1].Value;
                    if (url.Contains("member", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = url });
                }
            }

            return result;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
        }

        private string GetTitle(HtmlAgilityPack.HtmlNode node)
        {
            var title = node.SelectSingleText("//*[@id='moviePlay']//div[@class='moviePlay_title']/h1/span").Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = (string)this.ParseJsonLd(node)?["name"] ?? string.Empty;
            }

            return title.Trim();
        }

        private JObject ParseJsonLd(HtmlAgilityPack.HtmlNode node)
        {
            foreach (var script in node.SelectNodesSafe("//script[@type='application/ld+json']"))
            {
                try
                {
                    return JObject.Parse(script.InnerText.Replace("\n", " ", StringComparison.Ordinal));
                }
                catch (Newtonsoft.Json.JsonReaderException)
                {
                    continue;
                }
            }

            return null;
        }
    }
}
