using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkBang : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var url = $"https://www.bang.com/videos?term={Uri.EscapeDataString(searchTitle)}";
            var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            var searchResults = data.SelectNodesSafe("//a[starts-with(@href, '/video/')]");
            foreach (var searchResult in searchResults)
            {
                var href = searchResult.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(href))
                {
                    continue;
                }

                var sceneURL = new Uri(new Uri("https://www.bang.com"), href);
                var imgNode = searchResult.SelectSingleNode(".//img");
                var sceneName = imgNode?.GetAttributeValue("alt", string.Empty) ?? string.Empty;
                sceneName = sceneName.Replace("Screenshot from the porn video", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = sceneURL.Segments.LastOrDefault()?.Replace('-', ' ');
                }

                var scenePoster = imgNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;

                // curID = 完整场景 URL（与 Update/GetImages 一致）
                var curID = Helper.Encode(sceneURL.AbsolutePath);

                var item = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = sceneName,
                    ImageUrl = scenePoster,
                };

                result.Add(item);
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

            if (sceneID == null)
            {
                return result;
            }

            var scenePath = Helper.Decode(sceneID[0]);
            if (string.IsNullOrEmpty(scenePath))
            {
                return result;
            }

            if (!scenePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                scenePath = "https://www.bang.com" + scenePath;
            }

            var sceneData = await HTML.ElementFromURL(scenePath, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = scenePath;
            result.HasMetadata = true;

            var titleNode = sceneData.SelectSingleNode("//h1");
            result.Item.Name = System.Net.WebUtility.HtmlDecode(titleNode?.InnerText?.Trim());

            var descNode = sceneData.SelectSingleNode("//meta[@name='description']");
            result.Item.Overview = descNode?.GetAttributeValue("content", string.Empty)?.Trim();

            var studio = sceneData.SelectSingleText("//title").Split('-').LastOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(studio))
            {
                result.Item.AddStudio(studio);
            }

            // 日期: 场景页含 YYYY-MM-DD
            var dateText = sceneData.SelectSingleText("//script[contains(text(), 'uploadDate')]");
            var dateMatch = System.Text.RegularExpressions.Regex.Match(dateText, @"\d{4}-\d{2}-\d{2}");
            if (dateMatch.Success && DateTime.TryParseExact(dateMatch.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            // 流派: a.genres 链接
            var genreNode = sceneData.SelectNodesSafe("//a[contains(@class, 'genres')]");
            foreach (var genreLink in genreNode)
            {
                var genreName = genreLink.InnerText?.Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    result.Item.AddGenre(genreName);
                }
            }

            // 演员: /pornstar/ 链接
            var actorsNode = sceneData.SelectNodesSafe("//a[starts-with(@href, '/pornstar/')]");
            foreach (var actorLink in actorsNode)
            {
                var actorName = System.Net.WebUtility.HtmlDecode(actorLink.InnerText?.Trim());
                if (string.IsNullOrEmpty(actorName))
                {
                    continue;
                }

                var href = actorLink.GetAttributeValue("href", string.Empty);
                var actor = new PersonInfo
                {
                    Name = actorName,
                };

                if (!string.IsNullOrEmpty(href))
                {
                    var actorPage = await HTML.ElementFromURL("https://www.bang.com" + href, cancellationToken).ConfigureAwait(false);
                    var photoNode = actorPage.SelectSingleNode("//meta[@property='og:image']");
                    var photo = photoNode?.GetAttributeValue("content", string.Empty);
                    if (!string.IsNullOrEmpty(photo))
                    {
                        actor.ImageUrl = photo;
                    }
                }

                result.AddPerson(actor);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null)
            {
                return result;
            }

            var scenePath = Helper.Decode(sceneID[0]);
            if (string.IsNullOrEmpty(scenePath))
            {
                return result;
            }

            if (!scenePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                scenePath = "https://www.bang.com" + scenePath;
            }

            var sceneData = await HTML.ElementFromURL(scenePath, cancellationToken).ConfigureAwait(false);

            // 封面: og:image
            var ogImage = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (!string.IsNullOrEmpty(ogImage))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = ogImage,
                    Type = ImageType.Primary,
                });
            }

            // 截图: screenshots 图片
            var thumbs = sceneData.SelectNodes("//img[contains(@src, 'screenshots')]");
            if (thumbs != null)
            {
                var seen = new HashSet<string>();
                foreach (var img in thumbs)
                {
                    var url = img.GetAttributeValue("src", string.Empty);

                    // 去掉尺寸参数，取原始大图
                    url = url.Split('?')[0];
                    if (string.IsNullOrEmpty(url) || !seen.Add(url))
                    {
                        continue;
                    }

                    result.Add(new RemoteImageInfo
                    {
                        Url = url,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return result;
        }
    }
}
