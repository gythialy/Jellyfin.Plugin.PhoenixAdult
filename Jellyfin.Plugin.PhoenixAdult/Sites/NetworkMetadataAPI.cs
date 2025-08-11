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
    public class NetworkMetadataAPI : IProviderBase
    {
        private async Task<JObject> GetDataFromAPI(string url, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.MetadataAPIToken))
            {
                headers.Add("Authorization", $"Bearer {Plugin.Instance.Configuration.MetadataAPIToken}");
                headers.Add("Accept", "application/json");
            }

            var http = await HTTP.Request(url, cancellationToken, headers).ConfigureAwait(false);
            return http.IsOK ? JObject.Parse(http.Content) : null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
                return result;

            string url = $"{Helper.GetSearchSearchURL(siteNum)}/scenes?parse={Uri.EscapeDataString(searchTitle)}";
            var searchResults = await GetDataFromAPI(url, cancellationToken);
            if (searchResults?["data"] == null)
                return result;

            foreach (var searchResult in searchResults["data"])
            {
                string curID = (string)searchResult["_id"];
                string titleNoFormatting = (string)searchResult["title"];
                string siteName = (string)searchResult["site"]?["name"];
                string sceneDate = (string)searchResult["date"];
                DateTime.TryParseExact(sceneDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate);

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = $"{titleNoFormatting} [MetadataAPI/{siteName}] {releaseDate:yyyy-MM-dd}",
                    PremiereDate = releaseDate,
                    SearchProviderName = Plugin.Instance.Name,
                    ImageUrl = (string)searchResult["poster"]
                });
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

            string url = $"{Helper.GetSearchSearchURL(siteNum)}/scenes/{sceneID[0]}";
            var sceneData = await GetDataFromAPI(url, cancellationToken);
            if (sceneData?["data"] == null)
                return result;

            var details = (JObject)sceneData["data"];
            var movie = (Movie)result.Item;

            movie.Name = (string)details["title"];
            movie.Overview = (string)details["description"];

            if (details["site"]?.Type == JTokenType.Object)
            {
                string studioName = (string)details["site"]["name"];
                var collections = new List<string> { studioName };

                int? siteId = (int?)details["site"]["id"];
                int? networkId = (int?)details["site"]["network_id"];

                if (networkId.HasValue && siteId != networkId)
                {
                    string networkUrl = $"{Helper.GetSearchSearchURL(siteNum)}/sites/{networkId}";
                    var networkData = await GetDataFromAPI(networkUrl, cancellationToken);
                    if (networkData?["data"] != null)
                    {
                        studioName = (string)networkData["data"]["name"];
                        collections.Add(studioName);
                    }
                }

                movie.AddStudio(studioName);
                foreach (var collection in collections)
                    movie.AddTag(collection);
            }

            if (DateTime.TryParseExact((string)details["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            if (details["tags"] != null)
            {
                foreach (var genreLink in details["tags"])
                    movie.AddGenre((string)genreLink["name"]);
            }

            if (details["performers"] != null)
            {
                foreach (var actorLink in details["performers"])
                {
                    string actorName = (string)actorLink["name"];
                    if (actorLink["parent"]?["name"] != null)
                        actorName = (string)actorLink["parent"]["name"];

                    result.People.Add(new PersonInfo
                    {
                        Name = actorName,
                        ImageUrl = (string)actorLink["image"],
                        Type = PersonKind.Actor
                    });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string url = $"{Helper.GetSearchSearchURL(siteNum)}/scenes/{sceneID[0]}";
            var sceneData = await GetDataFromAPI(url, cancellationToken);
            if (sceneData?["data"] == null)
                return result;

            var details = (JObject)sceneData["data"];

            string posterUrl = (string)details["posters"]?["large"];
            if (!string.IsNullOrEmpty(posterUrl))
                result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });

            string backgroundUrl = (string)details["background"]?["large"];
            if (!string.IsNullOrEmpty(backgroundUrl))
                result.Add(new RemoteImageInfo { Url = backgroundUrl, Type = ImageType.Backdrop });

            return result;
        }
    }
}
