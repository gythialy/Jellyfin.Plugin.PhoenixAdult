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
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteLegalPorno : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum) + searchTitle;
            var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

            // LegalPorno 已迁移到 AnalVids：搜索词精确命中演员时会 301 到 model 页
            // （页面含 SCENES section + 相关推荐），普通搜索词返回 "Search for ..." 列表页。
            // 只取 SCENES section 内的卡片，避免把相关推荐混入结果。
            var scenesTitle = data.SelectSingleNode("//h2[contains(@class, 'section_title')][contains(text(), 'SCENES')]");
            HtmlAgilityPack.HtmlNodeCollection searchResults;
            if (scenesTitle != null)
            {
                searchResults = scenesTitle.SelectNodesSafe("./following-sibling::div//div[@data-content]");
            }
            else
            {
                searchResults = data.SelectNodesSafe("//div[@data-content]");
            }

            if (searchResults == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults)
            {
                var sceneLink = searchResult.SelectSingleNode(".//a[contains(@href, '/watch/')]");
                if (sceneLink == null)
                {
                    continue;
                }

                var sceneURL = sceneLink.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrEmpty(sceneURL))
                {
                    continue;
                }

                var sceneName = searchResult.SelectSingleText(".//div[contains(@class, 'card-scene__text')]//a").Trim();
                if (string.IsNullOrEmpty(sceneName))
                {
                    sceneName = sceneLink.GetAttributeValue("title", string.Empty).Trim();
                }

                if (string.IsNullOrEmpty(sceneName))
                {
                    continue;
                }

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneURL) } },
                    Name = sceneName,
                };

                var poster = searchResult.SelectSingleNode(".//img");
                if (poster != null)
                {
                    var imageURL = poster.GetAttributeValue("data-src", string.Empty);
                    if (string.IsNullOrEmpty(imageURL))
                    {
                        imageURL = poster.GetAttributeValue("src", string.Empty);
                    }

                    if (!string.IsNullOrEmpty(imageURL) && !imageURL.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        res.ImageUrl = imageURL;
                    }
                }

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

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = "https://www.analvids.com" + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            result.Item.ExternalId = sceneURL;
            result.Item.AddStudio("LegalPorno");

            // 标题: h1.watch__title 含嵌套的 model 链接（演员名）+ featuring span（配角），
            // 用 Split("featuring") 剥离（与 SiteAnalVids 一致），只保留标题主体
            var titleNode = sceneData.SelectSingleNode("//h1[contains(@class, 'watch__title')]");
            if (titleNode != null)
            {
                var titleText = System.Net.WebUtility.HtmlDecode(titleNode.InnerText).Trim();
                titleText = titleText.Split(new[] { "featuring" }, StringSplitOptions.None)[0].Trim();
                if (!string.IsNullOrEmpty(titleText))
                {
                    result.Item.Name = titleText;
                }
            }

            // Studio
            var studioLink = sceneData.SelectSingleNode("//a[contains(@href, '/studios/')]");
            if (studioLink != null)
            {
                var studioName = studioLink.InnerText.Trim();
                if (!string.IsNullOrEmpty(studioName))
                {
                    result.Item.AddStudio(studioName);
                }
            }

            // 日期
            var dateNode = sceneData.SelectSingleNode("//i[contains(@class, 'bi-calendar3')]");
            var sceneDate = dateNode?.InnerText.Trim() ?? string.Empty;
            if (DateTime.TryParseExact(sceneDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            // 流派
            var genreNodes = sceneData.SelectNodesSafe("//a[contains(@href, '/genre/')]");
            foreach (var genreLink in genreNodes)
            {
                var genreName = genreLink.InnerText.Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    result.Item.AddGenre(genreName);
                }
            }

            // 演员: h1 内的 model 链接（主演员 + featuring 演员）
            var actorNodes = sceneData.SelectNodesSafe("//h1[contains(@class, 'watch__title')]//a[contains(@href, '/model/')]");
            foreach (var actorLink in actorNodes)
            {
                var actorName = actorLink.InnerText.Trim();
                if (string.IsNullOrEmpty(actorName))
                {
                    continue;
                }

                var actor = new PersonInfo
                {
                    Name = actorName,
                };

                var modelURL = actorLink.GetAttributeValue("href", string.Empty);
                if (!string.IsNullOrEmpty(modelURL))
                {
                    var actorPage = await HTML.ElementFromURL(modelURL, cancellationToken).ConfigureAwait(false);
                    var actorPhoto = actorPage.SelectSingleText("//img[contains(@class, 'model')]/@src");
                    if (string.IsNullOrEmpty(actorPhoto))
                    {
                        actorPhoto = actorPage.SelectSingleText("//div[contains(@class, 'model')]//img/@src");
                    }

                    if (!string.IsNullOrEmpty(actorPhoto) && !actorPhoto.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        actor.ImageUrl = actorPhoto;
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

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = "https://www.analvids.com" + sceneURL;
            }

            var sceneData = await HTML.ElementFromURL(sceneURL, cancellationToken).ConfigureAwait(false);

            // 主图: video[data-poster]
            // CDN 签名 URL 的 query 参与签名（剥掉即 403），必须原样保留。
            // HTML 解析会把 &amp; 实体还原，但保险起见再反转义一次。
            var posterNode = sceneData.SelectSingleNode("//video[contains(@data-poster, 'http')]");
            if (posterNode != null)
            {
                var poster = System.Net.WebUtility.HtmlDecode(posterNode.GetAttributeValue("data-poster", string.Empty));
                if (!string.IsNullOrEmpty(poster))
                {
                    result.Add(new RemoteImageInfo
                    {
                        Url = poster,
                        Type = ImageType.Primary,
                    });
                    result.Add(new RemoteImageInfo
                    {
                        Url = poster,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return result;
        }
    }
}
