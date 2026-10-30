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
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if !__EMBY__
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// HeyDouga：ID 形如 1234-abcd（4 位厂商号 + 子号），URL /moviepages/{a}/{b}/index.html。
    /// 站点有 Cloudflare + 18 岁确认，统一走 Fetcher（自动回落 FlareSolverr）。
    /// </summary>
    public class SiteHeyDouga : IProviderBase
    {
        private static readonly Regex IdRegex = new Regex(@"^(?:heydouga[-_]\s*)?(?<a>[0-9]{2,6})[-_](?<b>[A-Za-z0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            if (!IdRegex.IsMatch(searchTitle.Trim().Split(' ').Last()))
            {
                return result;
            }

            var match = IdRegex.Match(searchTitle.Trim().Split(' ').Last());
            var sceneURL = $"{Helper.GetSearchBaseURL(siteNum)}/moviepages/{match.Groups["a"].Value}/{match.Groups["b"].Value}/index.html";
            var sceneData = await Fetcher.Html(sceneURL, cancellationToken, this.GetHeaders()).ConfigureAwait(false);

            var title = sceneData.SelectSingleText("//*[@id='title-bg']/h1").Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = sceneData.SelectSingleText("//title").Trim();
            }

            if (string.IsNullOrEmpty(title))
            {
                return result;
            }

            result.Add(new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, Helper.Encode(new Uri(sceneURL).AbsolutePath) } },
                Name = title,
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

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken, this.GetHeaders()).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            result.Item.Name = sceneData.SelectSingleText("//*[@id='title-bg']/h1").Trim();
            result.Item.ExternalId = sceneURL;
            result.Item.Overview = sceneData.SelectSingleText("//*[@id='movie-detail-mobile']/div/p[1]").Trim();
            result.Item.AddStudio(Helper.GetSearchSiteName(siteNum));

            foreach (var li in sceneData.SelectNodesSafe("//*[@id='movie-info']/ul/li"))
            {
                var label = li.SelectSingleText(".//span[1]").Trim();
                var value = li.SelectSingleText(".//span[2]").Trim();
                if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(value))
                {
                    continue;
                }

                switch (label)
                {
                    case "配信日":
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate))
                        {
                            result.Item.PremiereDate = releaseDate;
                        }

                        break;
                    case "出演者":
                        foreach (var actor in value.Split(new[] { ' ', '　', ',', '、' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
                        }

                        break;
                    case "メーカー":
                        if (!string.IsNullOrEmpty(value))
                        {
                            result.Item.AddStudio(value);
                        }

                        break;
                    case "収録時間":
                        var runtime = Regex.Match(value, @"[0-9]+");
                        if (runtime.Success && int.TryParse(runtime.Value, out var minutes))
                        {
                            result.Item.RunTimeTicks = TimeSpan.FromMinutes(minutes).Ticks;
                        }

                        break;
                }
            }

            var cover = this.GetPoster(sceneData, siteNum);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Item.SetImage(new ItemImageInfo { Path = cover, Type = ImageType.Primary }, 0);
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

            var sceneData = await Fetcher.Html(sceneURL, cancellationToken, this.GetHeaders()).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var cover = this.GetPoster(sceneData, siteNum);
            if (!string.IsNullOrEmpty(cover))
            {
                result.Add(new RemoteImageInfo { Type = ImageType.Primary, Url = cover });
                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = cover });
            }

            foreach (var a in sceneData.SelectNodesSafe("//*[@id='movie-gallery-images']//a[contains(@class, 'fancybox')]"))
            {
                var href = a.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    href = Helper.GetSearchBaseURL(siteNum) + href;
                }

                result.Add(new RemoteImageInfo { Type = ImageType.Backdrop, Url = href });
            }

            return result;
        }

        private IDictionary<string, string> GetHeaders()
            => new Dictionary<string, string> { { "Referer", "https://www.heydouga.com/" } };

        private string GetPoster(HtmlAgilityPack.HtmlNode sceneData, int[] siteNum)
        {
            var poster = string.Empty;
            foreach (var script in sceneData.SelectNodesSafe("//section[contains(@class, 'movie-player')]//script"))
            {
                var match = Regex.Match(script.InnerText, @"player_poster\s*=\s*.?'(?<url>[^']+)'", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    poster = match.Groups["url"].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(poster))
            {
                poster = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            }

            if (!string.IsNullOrEmpty(poster) && !poster.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                poster = Helper.GetSearchBaseURL(siteNum) + poster;
            }

            return poster;
        }
    }
}
