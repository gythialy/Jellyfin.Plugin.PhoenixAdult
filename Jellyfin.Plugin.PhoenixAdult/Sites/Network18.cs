using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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
    public class Network18 : IProviderBase
    {
        public static string searchQuery = "query Search($query: String!) { search { search(input: {query: $query}) { result { type itemId name description images } } } }";
        public static string findVideoQuery = "query FindVideo($videoId: ID!) { video { find(input: {videoId: $videoId}) { result { videoId title duration galleryCount description { short long } talent { type talent { talentId name } } } } } }";
        public static string assetQuery = "query BatchFindAssetQuery($paths: [String!]!) { asset { batch(input: {paths: $paths}) { result { path mime size serve { type uri } } } } }";

        public static Dictionary<string, List<string>> apiKeyDB = new Dictionary<string, List<string>>
        {
            { "fit18", new List<string> { "77cd9282-9d81-4ba8-8868-ca9125c76991" } },
            { "thicc18", new List<string> { "0e36c7e9-8cb7-4fa1-9454-adbc2bad15f0" } },
        };

        public static Dictionary<string, List<string>> genresDB = new Dictionary<string, List<string>>
        {
            { "fit18", new List<string> { "Young", "Gym" } },
            { "thicc18", new List<string> { "Thicc" } },
        };

        public static async Task<JObject> GetDataFromAPI(string queryType, string variable, string query, int[] siteNum, Dictionary<string, List<string>> apiKeyDB, CancellationToken cancellationToken)
        {
            JObject json = null;
            string name = Helper.GetSearchSiteName(siteNum);
            string apiKey = apiKeyDB.ContainsKey(name) ? apiKeyDB[name][0] : string.Empty;

            var variables = new Dictionary<string, object> { { variable, query } };
            var requestBody = new { query = queryType, variables };
            string paramsJson = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
            var param = new StringContent(paramsJson, Encoding.UTF8, "application/json");

            var headers = new Dictionary<string, string>
            {
                { "argonath-api-key", apiKey },
                { "Content-Type", "application/json" },
                { "Referer", Helper.GetSearchSearchURL(siteNum) },
            };

            var http = await HTTP.Request(Helper.GetSearchSearchURL(siteNum), HttpMethod.Post, param, cancellationToken, headers).ConfigureAwait(false);
            if (http.IsOK)
            {
                json = JObject.Parse(http.Content);
            }

            return json;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchResults = await GetDataFromAPI(searchQuery, "query", searchTitle, siteNum, apiKeyDB, cancellationToken);
            if (searchResults == null)
            {
                return result;
            }

            var videoPageElements = searchResults["data"];

            foreach (var searchResult in videoPageElements["search"]["search"]["result"])
            {
                if (searchResult["type"].ToString() == "VIDEO")
                {
                    string sceneName = searchResult["name"].ToString();
                    string curID = Helper.Encode(searchResult["itemId"].ToString());

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = sceneName,
                    };

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

            if (sceneID == null)
            {
                return result;
            }

            Logger.Info($"site: {siteNum})");
            Logger.Info($"searched ID: {sceneID}");
            Logger.Info($"cancellationToken: {cancellationToken}");

            string videoId = Helper.Decode(sceneID[0]);
            var detailsPageElements = await GetDataFromAPI(findVideoQuery, "videoId", videoId, siteNum, apiKeyDB, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var sceneData = detailsPageElements["video"]["find"]["result"];

            // Title
            result.Item.Name = sceneData["title"].ToString();

            // Summary
            string summary = sceneData["description"]["long"].ToString().Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(summary, @"(?<=(!|\.|\?))\.$"))
            {
                summary += ".";
            }

            result.Item.Overview = summary;

            // Studio
            result.Item.AddStudio(Helper.GetSearchSiteName(siteNum));

            // Tagline and Collection(s)
            /*if (metadataObj.collections == null)
                metadataObj.collections = new List<string>();

            ((List<string>)metadataObj.collections).Add(metadataObj.studio);*/

            // Release Date
            if (!string.IsNullOrEmpty(sceneDate))
            {
                DateTime date_object = DateTime.Parse(sceneDate);
                result.Item.PremiereDate = date_object;
                result.Item.ProductionYear = date_object.Year;
            }

            // Genres
            foreach (string genreLink in genresDB.ContainsKey(Helper.GetSearchSiteName(siteNum)) ? genresDB[Helper.GetSearchSiteName(siteNum)] : new List<string>())
            {
                string genreName = genreLink.Trim();
                movieGenres.addGenre(genreName);
            }

            // Actor(s)
            foreach (var actorLink in sceneData["talent"])
            {
                var actorPhoto = new List<string>();
                string actorName = actorLink["talent"]["name"].ToString();

                actorPhoto.Add($"/members/models/{actorLink["talent"]["talentId"]}/profile-sm.jpg");
                string actorPhotoURL = (await GetGraphQL(assetQuery, "paths", actorPhoto, siteNum, apiKeyDB))["asset"]["batch"]["result"][0]["serve"]["uri"].ToString();

                movieActors.addActor(actorName, actorPhotoURL);
            }

            // Posters
            var images = new List<string>();
            images.Add($"/members/models/{modelId}/scenes/{scene}/videothumb.jpg");
            for (int idx = 1; idx <= (int)detailsPageElements["galleryCount"]; idx++)
            {
                string path = $"/members/models/{modelId}/scenes/{scene}/photos/thumbs/{PAsearchSites.getSearchSiteName(siteNum).ToLower()}-{modelId}-{sceneNum}-{idx}.jpg";
                images.Add(path);
            }
            var posters = (await GetGraphQL(assetQuery, "paths", images, siteNum, apiKeyDB))["asset"]["batch"]["result"].ToObject<List<JObject>>();

            foreach (var poster in posters)
            {
                if (poster != null)
                {
                    art.Add(poster["serve"]["uri"].ToString());
                }
            }

            Console.WriteLine($"Artwork found: {art.Count}"); // Replace Log with Console.WriteLine or another logging mechanism
            for (int idx = 0; idx < art.Count; idx++)
            {
                string posterUrl = art[idx];
                // Remove Timestamp and Token from URL
                string cleanUrl = posterUrl.Split('?')[0];
                art[idx] = cleanUrl;
                if (!PAsearchSites.posterAlreadyExists(cleanUrl, metadata))
                {
                    try
                    {
                        using (var client = new HttpClient())
                        {
                            
                            // Download image file for analysis
                            byte[] imageContent = await client.GetByteArrayAsync(posterUrl);
                            
                            // Create a MemoryStream from the byte array
                            using (var memoryStream = new MemoryStream(imageContent))
                            {
                                // Use System.Drawing.Image to get the image dimensions
                                using (var image = System.Drawing.Image.FromStream(memoryStream))
                                {
                                    int width = image.Width;
                                    int height = image.Height;
                                    
                                    //reset position
                                    memoryStream.Position = 0;
                                    
                                    // Convert image bytes to base64
                                    string base64Image = Convert.ToBase64String(memoryStream.ToArray());

                                    // Add the image proxy items to the collection
                                    if (height > width)
                                    {
                                        // Item is a poster
                                        if (metadataObj.posters == null)
                                            metadataObj.posters = new Dictionary<string, string>();
                                            
                                        metadataObj.posters[cleanUrl] = base64Image; // You might want to replace Proxy.Media with a suitable alternative
                                    }
                                    if (width > height)
                                    {
                                        // Item is an art item
                                        if (metadataObj.art == null)
                                            metadataObj.art = new Dictionary<string, string>();
                                            
                                        metadataObj.art[cleanUrl] = base64Image; // You might want to replace Proxy.Media with a suitable alternative
                                    }
                                    
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error processing image: " + ex.Message);
                    }
                }
            }

            return result;
        }

    }
}
