using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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

namespace PhoenixAdult.Sites
{
    public class SitePornhub : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            if ((searchTitle.StartsWith("ph", StringComparison.OrdinalIgnoreCase) || int.TryParse(searchTitle, out _)) && !searchTitle.Contains(' ', StringComparison.OrdinalIgnoreCase))
            {
                var sceneURL = new Uri(Helper.GetSearchBaseURL(siteNum) + $"/view_video.php?viewkey={searchTitle}");
                var sceneID = new string[] { Helper.Encode(sceneURL.PathAndQuery) };

                var searchResult = await Helper.GetSearchResultsFromUpdate(this, siteNum, sceneID, searchDate, cancellationToken).ConfigureAwait(false);
                if (searchResult.Any())
                {
                    result.AddRange(searchResult);
                }
            }
            else
            {
                searchTitle = searchTitle.Replace(" ", "+", StringComparison.OrdinalIgnoreCase);
                var url = Helper.GetSearchSearchURL(siteNum) + searchTitle;
                var data = await HTML.ElementFromURL(url, cancellationToken).ConfigureAwait(false);

                var searchResults = data.SelectNodesSafe("//ul[@id='videoSearchResult']/li[@data-video-vkey]");
                foreach (var searchResult in searchResults)
                {
                    var sceneURL = new Uri(Helper.GetSearchBaseURL(siteNum) + searchResult.SelectSingleText(".//a/@href"));
                    string curID = Helper.Encode(sceneURL.PathAndQuery),
                        sceneName = searchResult.SelectSingleText(".//span[@class='title']"),
                        scenePoster = searchResult.SelectSingleText(".//div[@class='phimage']//img/@data-thumb_url");

                    var res = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = sceneName,
                        ImageUrl = scenePoster,
                    };

                    result.Add(res);
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

            if (sceneID == null)
            {
                return result;
            }

            var sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            // 场景页只接受 GET：POST https://www.pornhub.com/view_video.php?viewkey=...
            // 会被站点直接返回 404 "Page Not Found"，导致标题/演员/日期/图片全部为空。
            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken).ConfigureAwait(false);
            if (!http.IsOK)
            {
                return result;
            }

            var sceneData = HTML.ElementFromStream(http.ContentStream);
            var json = sceneData.SelectSingleText("//script[@type='application/ld+json']");
            JObject sceneDataJSON = null;
            if (!string.IsNullOrEmpty(json))
            {
                sceneDataJSON = JObject.Parse(json);
            }

            result.Item.ExternalId = sceneURL;

            // 标题文本位于 h1 的子节点：<h1 class="title"><span class="inlineFree">…</span></h1>，
            // 直接取 h1 的文本会得到空串。与 TPDB networkPornhub 的
            // '//h1[@class="title"]/span/text()' 选择器保持一致，并保留回退。
            result.Item.Name = sceneData.SelectSingleText("//h1[@class='title']/span");
            if (string.IsNullOrEmpty(result.Item.Name))
            {
                result.Item.Name = sceneData.SelectSingleText("//h1[@class='title']");
            }

            var studioName = sceneData.SelectSingleText("//div[@class='userInfo']//a");
            result.Item.AddStudio("Pornhub");

            if (!string.IsNullOrEmpty(studioName))
            {
                result.Item.AddStudio(studioName);
            }

            if (sceneDataJSON != null)
            {
                var date = (string)sceneDataJSON["uploadDate"];
                if (date != null)
                {
                    if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
                    {
                        result.Item.PremiereDate = sceneDateObj;
                    }
                }
            }

            var genreNode = sceneData.SelectNodesSafe("(//div[@class='categoriesWrapper'] | //div[@class='tagsWrapper'])/a");
            foreach (var genreLink in genreNode)
            {
                var genreName = genreLink.InnerText;

                result.Item.AddGenre(genreName);
            }

            var actorsNode = sceneData.SelectNodesSafe("//div[contains(@class, 'pornstarsWrapper')]/a");
            foreach (var actorLink in actorsNode)
            {
                string actorName = actorLink.Attributes["data-mxptext"].Value,
                        actorPhotoURL = actorLink.SelectSingleText(".//img[@class='avatar']/@src");

                result.AddPerson(new PersonInfo
                {
                    Name = actorName,
                    ImageUrl = actorPhotoURL,
                });
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
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            // 场景页只接受 GET：POST https://www.pornhub.com/view_video.php?viewkey=...
            // 会被站点直接返回 404 "Page Not Found"，导致标题/演员/日期/图片全部为空。
            var http = await HTTP.Request(sceneURL, HttpMethod.Get, cancellationToken).ConfigureAwait(false);
            if (!http.IsOK)
            {
                return result;
            }

            var sceneData = HTML.ElementFromStream(http.ContentStream);

            // 场景页新版已移除 div#player 内的 <img>，改为读取社交卡片元数据。
            // 与 TPDB networkPornhub 的 '//meta[@property="og:image"]/@content|//meta[@name="twitter:image"]/@content' 一致。
            var img = sceneData.SelectSingleText("//meta[@property='og:image']/@content");
            if (string.IsNullOrEmpty(img))
            {
                img = sceneData.SelectSingleText("//meta[@name='twitter:image']/@content");
            }

            if (!string.IsNullOrEmpty(img))
            {
                result.Add(new RemoteImageInfo
                {
                    Url = img,
                    Type = ImageType.Primary,
                });
            }

            return result;
        }
    }
}
