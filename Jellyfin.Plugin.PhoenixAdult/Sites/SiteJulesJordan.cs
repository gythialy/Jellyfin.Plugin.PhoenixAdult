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

namespace PhoenixAdult.Sites
{
    public class SiteJulesJordan : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // 站内搜索按词 AND 匹配，完整 searchTitle（文件名 SEO 标题串）与站点标题不同 → 0 结果。
            // 文件名 Site.YY.MM.DD.Actors.Title 的演员在开头 → 用前 2 词（演员名）搜索。
            var searchQuery = Helper.GetSearchTitle(searchTitle, 2);

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchQuery);
            var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            // 新版页面: 场景卡片 div.jj-content-card, 链接 a.jj-card-thumb, 标题 .jj-card-title, 日期 .jj-card-date
            var searchResults = data.SelectNodesSafe("//div[contains(@class, 'jj-content-card')]");
            foreach (var searchResult in searchResults)
            {
                var sceneLink = searchResult.SelectSingleNode(".//a[contains(@class, 'jj-card-thumb')]");
                if (sceneLink == null)
                {
                    continue;
                }

                var sceneHref = sceneLink.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(sceneHref))
                {
                    continue;
                }

                string curID = Helper.Encode(sceneHref),
                    sceneName = searchResult.SelectSingleText(".//div[contains(@class, 'jj-card-title')]"),
                    scenePoster = searchResult.SelectSingleText(".//img[1]/@src");

                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = searchResult.SelectSingleText(".//img[1]/@alt");
                }

                var sceneDateNode = searchResult.SelectSingleNode(".//div[contains(@class, 'jj-card-date')]");
                var res = new RemoteSearchResult
                {
                    Name = sceneName,
                    ImageUrl = scenePoster,
                };

                if (sceneDateNode != null)
                {
                    var sceneDate = sceneDateNode.InnerText.Trim();
                    sceneDate = Regex.Replace(sceneDate, "Released\\s*:\\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
                    if (DateTime.TryParse(sceneDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                    {
                        res.PremiereDate = sceneDateObj;
                        curID += $"#{sceneDateObj.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
                    }
                }

                res.ProviderIds.Add(Plugin.Instance.Name, curID);
                result.Add(res);
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

            string sceneURL = Helper.Decode(sceneID[0]),
                sceneDate = string.Empty;

            if (sceneID.Length > 1)
            {
                sceneDate = sceneID[1];
            }

            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;
            result.HasMetadata = true;

            // 新版页面: 标题在 h1
            var titleNode = sceneData.SelectSingleNode("//h1");
            result.Item.Name = System.Net.WebUtility.HtmlDecode(titleNode?.InnerText?.Trim());

            // 描述: Categories 区块之前的场景描述
            var descNode = sceneData.SelectSingleNode("//div[contains(@class, 'scene-cats')]/preceding-sibling::div[1]");
            result.Item.Overview = descNode?.InnerText?.Trim();

            result.Item.AddStudio("Jules Jordan");

            if (!string.IsNullOrEmpty(sceneDate))
            {
                if (DateTime.TryParseExact(sceneDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                {
                    result.Item.PremiereDate = sceneDateObj;
                }
            }

            // 流派: 新版 .scene-cats a.cat-tag
            var genreNode = sceneData.SelectNodesSafe("//div[contains(@class, 'scene-cats')]//a[contains(@class, 'cat-tag')]");
            foreach (var genreLink in genreNode)
            {
                var genreName = genreLink.InnerText?.Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    result.Item.AddGenre(genreName);
                }
            }

            // 演员: 主演员区在 .scene-meta（相关视频卡片里也有 update_models，需限定）
            var actorsNode = sceneData.SelectNodesSafe("//div[contains(@class, 'scene-meta')]//span[contains(@class, 'update_models')]//a");
            foreach (var actorLink in actorsNode)
            {
                var actorName = actorLink.InnerText?.Trim();
                if (string.IsNullOrEmpty(actorName))
                {
                    continue;
                }

                var actor = new PersonInfo
                {
                    Name = actorName,
                };

                var href = actorLink.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(href))
                {
                    var actorPage = await HTML.ElementFromURL(href, cancellationToken).ConfigureAwait(false);
                    var actorPhotoNode = actorPage.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')]");
                    if (actorPhotoNode != null)
                    {
                        string actorPhoto = actorPhotoNode.GetAttributeValue("src0_3x", string.Empty);
                        if (string.IsNullOrEmpty(actorPhoto))
                        {
                            actorPhoto = actorPhotoNode.GetAttributeValue("src0", string.Empty);
                        }

                        if (string.IsNullOrEmpty(actorPhoto))
                        {
                            actorPhoto = actorPhotoNode.GetAttributeValue("src", string.Empty);
                        }

                        if (!string.IsNullOrEmpty(actorPhoto))
                        {
                            if (!actorPhoto.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                actorPhoto = Helper.GetSearchBaseURL(siteNum) + actorPhoto;
                            }

                            actor.ImageUrl = actorPhoto;
                        }
                    }
                }

                result.AddPerson(actor);
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            if (sceneID == null || string.IsNullOrEmpty(item?.Name))
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

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

            // 场景图集: 页面中的 jj-thumb-img / contentthumbs 图片
            var thumbs = sceneData.SelectNodes("//img[contains(@class, 'stdimage') or contains(@class, 'thumbs')]");
            var seen = new HashSet<string>();
            if (thumbs != null)
            {
                foreach (var thumbNode in thumbs)
                {
                    var img = thumbNode.GetAttributeValue("src", string.Empty);
                    if (string.IsNullOrEmpty(img) || !img.Contains("contentthumbs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!seen.Add(img))
                    {
                        continue;
                    }

                    result.Add(new RemoteImageInfo
                    {
                        Url = img,
                        Type = ImageType.Primary,
                    });
                    result.Add(new RemoteImageInfo
                    {
                        Url = img,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return result;
        }
    }
}
