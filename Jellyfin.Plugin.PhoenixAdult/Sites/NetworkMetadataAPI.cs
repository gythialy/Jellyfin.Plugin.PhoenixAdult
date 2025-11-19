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
            {
                return result;
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}/scenes?parse={Uri.EscapeDataString(searchTitle)}";
            var searchResults = await GetDataFromAPI(url, cancellationToken);
            var data = searchResults?.SelectToken("data");
            if (data == null || data.Type == JTokenType.Null)
            {
                return result;
            }

            foreach (var searchResult in data)
            {
                string curID = (string)searchResult["_id"];
                string titleNoFormatting = (string)searchResult["title"];
                string siteName = searchResult.SelectToken("site.name")?.ToString();
                string sceneDate = (string)searchResult["date"];
                DateTime.TryParseExact(sceneDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var releaseDate);

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = $"{titleNoFormatting} [MetadataAPI/{siteName}] {releaseDate:yyyy-MM-dd}",
                    PremiereDate = releaseDate,
                    SearchProviderName = Plugin.Instance.Name,
                    ImageUrl = (string)searchResult["poster"],
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
            var details = sceneData?.SelectToken("data");
            if (details == null || details.Type == JTokenType.Null)
            {
                return result;
            }

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
                    var networkDetails = networkData?.SelectToken("data");
                    if (networkDetails != null && networkDetails.Type != JTokenType.Null)
                    {
                        studioName = (string)networkDetails["name"];
                        collections.Add(studioName);
                    }
                }

                movie.AddStudio(studioName);
                foreach (var collection in collections)
                {
                    movie.AddTag(collection);
                }
            }

            if (DateTime.TryParseExact((string)details["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                movie.PremiereDate = sceneDateObj;
                movie.ProductionYear = sceneDateObj.Year;
            }

            if (details["tags"] != null)
            {
                foreach (var genreLink in details["tags"])
                {
                    movie.AddGenre((string)genreLink["name"]);
                }
            }

            if (details["performers"] != null)
            {
                foreach (var actorLink in details["performers"])
                {
                    string actorName = (string)actorLink["name"];
                    if (actorLink["parent"] is JObject parent)
                    {
                        actorName = (string)parent["name"] ?? actorName;
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo
                    {
                        Name = actorName,
                        ImageUrl = (string)actorLink["image"],
                        Type = PersonKind.Actor,
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
            var details = sceneData?.SelectToken("data");
            if (details == null || details.Type == JTokenType.Null)
            {
                return result;
            }

            string posterUrl = null;
            if (details["posters"] is JObject postersObject)
            {
                posterUrl = (string)postersObject["large"] ?? (string)postersObject["medium"] ?? (string)postersObject["small"];
            }

            if (string.IsNullOrEmpty(posterUrl))
            {
                posterUrl = (string)details["image"];
            }

            if (!string.IsNullOrEmpty(posterUrl))
            {
                result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
            }

            string backgroundUrl = null;
            if (details["background"] is JObject backgroundObject)
            {
                backgroundUrl = (string)backgroundObject["large"] ?? (string)backgroundObject["full"] ?? (string)backgroundObject["medium"] ?? (string)backgroundObject["small"];
                result.Add(new RemoteImageInfo { Url = backgroundUrl, Type = ImageType.Backdrop });
            }

            return result;
        }
    }
}
