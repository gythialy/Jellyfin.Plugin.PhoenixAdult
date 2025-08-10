using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class Network18 : IProviderBase
    {
        private static readonly string searchQuery = "query Search($query: String!) { search { search(input: {query: $query}) { result { type itemId name description images } } } }";
        private static readonly string findVideoQuery = "query FindVideo($videoId: ID!) { video { find(input: {videoId: $videoId}) { result { videoId title duration galleryCount description { short long } talent { type talent { talentId name } } } } } }";
        private static readonly string assetQuery = "query BatchFindAssetQuery($paths: [String!]!) { asset { batch(input: {paths: $paths}) { result { path mime size serve { type uri } } } } }";

        private static readonly Dictionary<string, List<string>> apiKeyDB = new Dictionary<string, List<string>>
        {
            { "fit18", new List<string> { "77cd9282-9d81-4ba8-8868-ca9125c76991" } },
            { "thicc18", new List<string> { "0e36c7e9-8cb7-4fa1-9454-adbc2bad15f0" } },
        };

        private static readonly Dictionary<string, List<string>> genresDB = new Dictionary<string, List<string>>
        {
            { "fit18", new List<string> { "Young", "Gym" } },
            { "thicc18", new List<string> { "Thicc" } },
        };

        private async Task<JObject> GetDataFromAPI(string queryType, string variable, object query, int siteNum, CancellationToken cancellationToken)
        {
            string siteName = Helper.GetSearchSiteName(new [] { siteNum });
            if (!apiKeyDB.ContainsKey(siteName))
                return null;

            string apiKey = apiKeyDB[siteName][0];
            string searchUrl = Helper.GetSearchSearchURL(new [] { siteNum });

            var variables = new Dictionary<string, object> { { variable, query } };
            var requestBody = new { query = queryType, variables };
            string paramsJson = JsonConvert.SerializeObject(requestBody);
            var param = new StringContent(paramsJson, Encoding.UTF8, "application/json");

            var headers = new Dictionary<string, string>
            {
                { "argonath-api-key", apiKey },
                { "Referer", searchUrl },
            };

            var http = await HTTP.Request(searchUrl, HttpMethod.Post, param, cancellationToken, headers).ConfigureAwait(false);
            return http.IsOK ? JObject.Parse(http.Content) : null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
                return result;

            var searchResults = await GetDataFromAPI(searchQuery, "query", searchTitle, siteNum[0], cancellationToken);
            if (searchResults?["data"]?["search"]?["search"]?["result"] == null)
                return result;

            foreach (var searchResult in searchResults["data"]["search"]["search"]["result"])
            {
                if (searchResult["type"].ToString() == "VIDEO")
                {
                    string sceneName = searchResult["name"].ToString();
                    string curID = Helper.Encode(searchResult["itemId"].ToString());
                    string releaseDateStr = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDateStr}" } },
                        Name = $"{sceneName} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    };

                    if (searchDate.HasValue)
                        item.PremiereDate = searchDate;

                    result.Add(item);
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

            string[] providerIds = sceneID[0].Split('|');
            string videoIdEncoded = providerIds[0];
            int siteNumVal = int.Parse(providerIds[1]);
            string sceneDate = providerIds[2];

            string videoId = Helper.Decode(videoIdEncoded);

            var detailsPageElements = await GetDataFromAPI(findVideoQuery, "videoId", videoId, siteNumVal, cancellationToken);
            if (detailsPageElements?["data"]?["video"]?["find"]?["result"] == null)
                return result;

            var sceneData = detailsPageElements["data"]["video"]["find"]["result"];

            var movie = (Movie)result.Item;
            movie.Name = sceneData["title"].ToString();

            string summary = sceneData["description"]["long"].ToString().Trim();
            if (!Regex.IsMatch(summary, @"[.!?]$"))
                summary += ".";
            movie.Overview = summary;

            string studio = Helper.GetSearchSiteName(new[] { siteNumVal });
            movie.AddStudio(studio);
            movie.AddTag(studio);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var premiereDate))
            {
                movie.PremiereDate = premiereDate;
                movie.ProductionYear = premiereDate.Year;
            }

            if (genresDB.TryGetValue(studio, out var genres))
            {
                foreach (string genre in genres)
                    movie.AddGenre(genre.Trim());
            }

            foreach (var actorLink in sceneData["talent"])
            {
                string actorName = actorLink["talent"]["name"].ToString();
                string actorTalentId = actorLink["talent"]["talentId"].ToString();

                var actorPhotoPaths = new List<string> { $"/members/models/{actorTalentId}/profile-sm.jpg" };
                var actorPhotoData = await GetDataFromAPI(assetQuery, "paths", actorPhotoPaths, siteNumVal, cancellationToken);
                string actorPhotoURL = actorPhotoData?["data"]?["asset"]?["batch"]?["result"]?[0]?["serve"]?["uri"]?.ToString() ?? string.Empty;

                result.People.Add(new PersonInfo { Name = actorName, Type = PersonType.Actor, ImageUrl = actorPhotoURL });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            string[] providerIds = sceneID[0].Split('|');
            string videoIdEncoded = providerIds[0];
            int siteNumVal = int.Parse(providerIds[1]);

            string videoId = Helper.Decode(videoIdEncoded);
            var videoIdParts = videoId.Split(':');
            string modelId = videoIdParts[0];
            string scene = videoIdParts[videoIdParts.Length - 2];
            int sceneNumInt = int.Parse(Regex.Match(scene, @"\d+").Value);

            var detailsPageElements = await GetDataFromAPI(findVideoQuery, "videoId", videoId, siteNumVal, cancellationToken);
            if (detailsPageElements?["data"]?["video"]?["find"]?["result"] == null)
                return images;

            var sceneData = detailsPageElements["data"]["video"]["find"]["result"];
            string studio = Helper.GetSearchSiteName(new[] { siteNumVal });

            var imagePaths = new List<string>
            {
                $"/members/models/{modelId}/scenes/{scene}/videothumb.jpg"
            };
            int galleryCount = (int)sceneData["galleryCount"];
            for (int i = 1; i <= galleryCount; i++)
            {
                imagePaths.Add($"/members/models/{modelId}/scenes/{scene}/photos/thumbs/{studio.ToLower()}-{modelId}-{sceneNumInt}-{i}.jpg");
            }

            var imagesData = await GetDataFromAPI(assetQuery, "paths", imagePaths, siteNumVal, cancellationToken);
            if (imagesData?["data"]?["asset"]?["batch"]?["result"] != null)
            {
                bool first = true;
                foreach (var poster in imagesData["data"]["asset"]["batch"]["result"])
                {
                    if (poster?["serve"]?["uri"] != null)
                    {
                        string posterUrl = poster["serve"]["uri"].ToString();
                        var imageInfo = new RemoteImageInfo { Url = posterUrl };
                        if (first)
                        {
                            imageInfo.Type = ImageType.Primary;
                            first = false;
                        }
                        else
                        {
                            imageInfo.Type = ImageType.Backdrop;
                        }
                        images.Add(imageInfo);
                    }
                }
            }

            return images;
        }
    }
}
