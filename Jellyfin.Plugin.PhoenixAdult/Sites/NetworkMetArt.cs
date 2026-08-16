using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
#if !__EMBY__
using Jellyfin.Data.Enums;
#endif
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkMetArt : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            // MetArt API 搜索按短语/AND 严格匹配：完整词（演员+标题混串）→ 0 结果。
            // 文件名 Site.YY.MM.DD.Actors.Title 的演员在开头 → 取前 2 词搜索，日期排序选目标。
            searchTitle = Helper.GetSearchTitle(searchTitle, 2);

            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}/search-results?query%5BcontentType%5D=movies&searchPhrase={Uri.EscapeDataString(searchTitle)}";

            JObject searchResults;
            if (PhoenixAdult.Helpers.Utils.FlareSolverr.IsConfigured)
            {
                // sexart.com 等 MetArt 系列站被 Cloudflare 拦裸请求，走 FlareSolverr 浏览器上下文
                searchResults = await PhoenixAdult.Helpers.Utils.FlareSolverr.GetJson(searchUrl, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    return result;
                }

                searchResults = JObject.Parse(httpResult.Content);
            }

            if (searchResults == null)
            {
                return result;
            }

            if (searchResults["items"] != null)
            {
                foreach (var searchResult in searchResults["items"])
                {
                    string subSite = Helper.GetSearchSiteName(siteNum);
                    string titleNoFormatting = searchResult["item"]["name"].ToString();
                    var sceneUrlParts = searchResult["item"]["path"].ToString().Split('/').Skip(1).ToArray();

                    // path 形如 /model/{模型}/movie/{日期}/{场景名}
                    string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}/movie?name={sceneUrlParts[4]}&date={sceneUrlParts[3]}";
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    DateTime? premiereDate = null;
                    if (DateTime.TryParse(searchResult["item"]["publishedAt"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        premiereDate = parsedDate;
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [MetArt/{subSite}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                        PremiereDate = premiereDate,
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

            result.HasMetadata = true;

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

            var detailsPageElements = JObject.Parse(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements["name"].ToString();
            movie.Overview = detailsPageElements["description"].ToString();
            movie.AddStudio("MetArt");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (DateTime.TryParse(detailsPageElements["publishedAt"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genre in detailsPageElements["tags"])
            {
                movie.AddGenre(genre.ToString().Capitalize());
            }

            movie.AddGenre("Glamorous");

            foreach (var actor in detailsPageElements["models"])
            {
                string actorName = actor["name"].ToString();
                string actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actor["headshotImagePath"];
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            foreach (var director in detailsPageElements["photographers"])
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = director["name"].ToString(), Type = PersonKind.Director });
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

            var detailsPageElements = JObject.Parse(httpResult.Content);

            string siteUUID = detailsPageElements["siteUUID"].ToString();
            string cdnUrl = $"https://cdn.metartnetwork.com/{siteUUID}";
            images.Add(new RemoteImageInfo { Url = cdnUrl + detailsPageElements["coverImagePath"], Type = ImageType.Primary });
            images.Add(new RemoteImageInfo { Url = cdnUrl + detailsPageElements["splashImagePath"] });

            return images;
        }
    }
}
