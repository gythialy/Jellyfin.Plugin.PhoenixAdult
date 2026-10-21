using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
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
    public class SiteDorcelClub : IProviderBase
    {
        // 站点图片都由 <picture><source data-srcset="…_360_240_hash.jpg 1x, …_720_480_hash.jpg 2x"> 提供，
        // 文件名里的哈希是 CDN 必需的签名段：删掉后 CDN 直接返回 500（图库/封面全部取不到图）。
        private static readonly Regex ImageSizeRegex = new Regex(@"_(\d{2,4})_(\d{2,4})_", RegexOptions.Compiled);

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            // 站内搜索按词 AND 匹配，完整 searchTitle（文件名含多个演员名）→ 0 结果。
            // 文件名 Site.YY.MM.DD.Actors.Title 的标题在末尾 → 取末尾 4 词搜索。
            searchTitle = Helper.GetSearchTitle(searchTitle, 4, fromEnd: true);

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var sceneNodes = searchPageElements.SelectNodes("//div[@class='scenes list']/div[@class='items']/div[@class='scene thumbnail ']");
            if (sceneNodes != null)
            {
                foreach (var node in sceneNodes)
                {
                    var titleNode = node.SelectSingleNode(".//div[@class='textual']/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        ImageUrl = GetLargestImage(node),
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }

            var movieNodes = searchPageElements.SelectNodes("//div[@class='movies list']/div[@class='items']/a[@class='movie thumbnail']");
            if (movieNodes != null)
            {
                foreach (var node in movieNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode("./h2")?.InnerText.Trim();
                    string movieLink = node.GetAttributeValue("href", string.Empty);
                    string curId = Helper.Encode(movieLink);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} - Full Movie [{Helper.GetSearchSiteName(siteNum)}]",
                        ImageUrl = GetLargestImage(node),
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//span[@class='full']")?.InnerText.Trim();
            movie.AddStudio("Marc Dorcel");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//span[@class='publish_date']") ?? detailsPageElements.SelectSingleNode("//span[@class='out_date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Year :", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("French porn");
            var movieNameNode = detailsPageElements.SelectSingleNode("//span[@class='movie']/a");
            if (movieNameNode != null)
            {
                movie.AddGenre("Blockbuster Movie");
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='actress']/a") ?? detailsPageElements.SelectNodes("//div[@class='actor thumbnail ']/a/div[@class='name']");
            if (actorNodes != null)
            {
                if (!sceneUrl.Contains("porn-movie"))
                {
                    if (actorNodes.Count == 3)
                    {
                        movie.AddGenre("Threesome");
                    }

                    if (actorNodes.Count == 4)
                    {
                        movie.AddGenre("Foursome");
                    }

                    if (actorNodes.Count > 4)
                    {
                        movie.AddGenre("Orgy");
                    }
                }

                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//span[@class='director']");
            if (directorNode != null)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = directorNode.InnerText.Replace("Director :", string.Empty).Trim(), Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            // 场景主封面：player_container 里最大的一张（通常 1546×1030），优先作为 Primary
            var coverUrl = GetLargestImage(detailsPageElements.SelectSingleNode("//div[contains(@class, 'player_container')]"));

            // 图库照片：同一张照片有 6 档响应式尺寸，只取宽度最大的一档，避免几十条重复候选
            var photoUrls = new List<string>();
            var photoNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'photos')]//a[contains(@class, 'photo')]");
            if (photoNodes != null)
            {
                foreach (var photo in photoNodes)
                {
                    var photoUrl = GetLargestImage(photo);
                    if (!string.IsNullOrEmpty(photoUrl) && !photoUrls.Contains(photoUrl))
                    {
                        photoUrls.Add(photoUrl);
                    }
                }
            }

            if (!string.IsNullOrEmpty(coverUrl))
            {
                images.Add(new RemoteImageInfo { Url = coverUrl, Type = ImageType.Primary });
            }

            foreach (var photoUrl in photoUrls)
            {
                // 没有封面时由第一张图库照片兜底 Primary，其余作为 Backdrop
                images.Add(new RemoteImageInfo
                {
                    Url = photoUrl,
                    Type = images.Any() ? ImageType.Backdrop : ImageType.Primary,
                });
            }

            return images;
        }

        /// <summary>
        /// 从节点内的 &lt;source data-srcset&gt; 中挑宽度最大的一张图。
        /// srcset 形如 "…_1150_766_hash.jpg 1x, …_1546_1030_hash.jpg 2x"；URL 必须原样使用，
        /// 文件名末尾的哈希是 CDN 校验段，改写（例如删掉）会让 CDN 返回 500.
        /// </summary>
        private static string GetLargestImage(HtmlNode node)
        {
            var sources = node?.SelectNodes(".//source");
            if (sources == null)
            {
                return null;
            }

            string bestUrl = null;
            int bestWidth = -1;
            foreach (var source in sources)
            {
                var srcset = source.GetAttributeValue("data-srcset", string.Empty);
                if (string.IsNullOrEmpty(srcset))
                {
                    continue;
                }

                foreach (var candidate in srcset.Split(','))
                {
                    var imageUrl = candidate.Trim().Split(' ').FirstOrDefault();
                    if (string.IsNullOrEmpty(imageUrl) || !imageUrl.StartsWith("http", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var width = GetImageWidth(imageUrl);
                    if (width > bestWidth)
                    {
                        bestWidth = width;
                        bestUrl = imageUrl;
                    }
                }
            }

            return bestUrl;
        }

        /// <summary>从 CDN 文件名（…_1150_766_hash.jpg）解析宽度，解析不出时返回 0.</summary>
        private static int GetImageWidth(string imageUrl)
        {
            var fileName = imageUrl.Split('?').First().Split('/').Last();
            var match = ImageSizeRegex.Match(fileName);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }
    }
}
