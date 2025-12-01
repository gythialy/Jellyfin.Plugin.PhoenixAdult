using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteManyVids : IProviderBase
    {
        private async Task<JObject> GetJSONfromPage(string url, CancellationToken cancellationToken)
        {
            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var ldJsonNode = HTML.ElementFromString(http.Content).SelectSingleNode("//script[@type='application/ld+json']");
                if (ldJsonNode != null)
                {
                    return JObject.Parse(ldJsonNode.InnerText);
                }
            }

            return null;
        }

        private async Task<JObject> GetDataFromAPI(string url, CancellationToken cancellationToken)
        {
            var http = await HTTP.Request(url, cancellationToken);
            return http.IsOK ? (JObject)JObject.Parse(http.Content)["data"] : null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string sceneID = searchTitle.Split(' ')[0];
            if (!int.TryParse(sceneID, out _))
            {
                return result;
            }

            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/video/{sceneID}";
            var searchResult = await GetJSONfromPage(sceneUrl, cancellationToken);
            if (searchResult == null)
            {
                return result;
            }

            string titleNoFormatting = (string)searchResult["name"];
            string curID = Helper.Encode(sceneUrl);
            string searchID = sceneUrl.Split('/').Last().Split('-')[0];
            string subSite = searchResult.SelectToken("creator.name")?.ToString();
            string releaseDate = string.Empty;
            if (DateTime.TryParse((string)searchResult["uploadDate"], out var parsedDate))
            {
                releaseDate = parsedDate.ToString("yyyy-MM-dd");
            }

            result.Add(new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}" } },
                Name = $"{titleNoFormatting} [ManyVids/{subSite}] {releaseDate}",
                SearchProviderName = Plugin.Instance.Name,
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneURL = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            string videoID = sceneURL.Split('/').Last().Split('-')[0];
            var videoPageElements = await GetDataFromAPI($"https://www.manyvids.com/bff/store/video/{videoID}", cancellationToken);
            if (videoPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneURL;
            movie.Name = (string)videoPageElements["title"]?.ToString().Trim();
            movie.Overview = (string)videoPageElements["description"]?.ToString().Trim();
            movie.AddStudio("ManyVids");

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var releaseDate))
            {
                movie.PremiereDate = releaseDate;
                movie.ProductionYear = releaseDate.Year;
            }

            if (videoPageElements["tagList"] != null)
            {
                foreach (var genreLink in videoPageElements["tagList"])
                {
                    movie.AddGenre((string)genreLink["label"]);
                }
            }

            var actor = videoPageElements["model"];
            ((List<PersonInfo>)result.People).Add(new PersonInfo
            {
                Name = (string)actor["displayName"],
                ImageUrl = (string)actor["avatar"],
                Type = PersonKind.Actor,
            });

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string videoID = Helper.Decode(sceneID[0].Split('|')[0]).Split('/').Last().Split('-')[0];
            var videoPageElements = await GetDataFromAPI($"https://www.manyvids.com/bff/store/video/{videoID}", cancellationToken);
            if (videoPageElements == null)
            {
                return result;
            }

            string imgUrl = (string)videoPageElements["screenshot"];
            if (!string.IsNullOrEmpty(imgUrl))
            {
                result.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Primary });
                result.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Backdrop });
            }

            return result;
        }
    }
}