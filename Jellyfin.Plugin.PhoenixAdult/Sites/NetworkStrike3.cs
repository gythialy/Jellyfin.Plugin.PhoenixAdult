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
    public class NetworkStrike3 : IProviderBase
    {
        private readonly string searchVariables = "{{\"query\":\"{0}\",\"site\":\"{1}\",\"first\":10,\"skip\":0}}";
        private readonly string searchQuery = @"query getSearchResults($query:String!,$site:Site!,$first:Int,$skip:Int){searchVideos(input:{query:$query,site:$site,first:$first,skip:$skip}){edges{node{videoId title releaseDate slug images{listing{src}}}}}}";
        private readonly string updateVariables = "{{\"slug\":\"{0}\",\"site\":\"{1}\"}}";
        private readonly string updateQuery = @"query getSearchResults($slug:String!,$site:Site!){findOneVideo(input:{slug:$slug,site:$site}){videoId title description releaseDate models{name slug images{listing{highdpi{double}}}}directors{name}categories{name}carousel{listing{highdpi{triple}}}}}";

        public static async Task<JObject> GetDataFromAPI(string url, string query, string variables, CancellationToken cancellationToken)
        {
            // Parse variables as JSON to ensure proper format
            var variablesObj = JObject.Parse(variables);

            // Create proper JSON object for GraphQL request
            var requestBodyObj = new JObject
            {
                ["query"] = query,
                ["variables"] = variablesObj,
            };

            var requestBody = requestBodyObj.ToString(Newtonsoft.Json.Formatting.None);

            Logger.Debug($"{url}, {query}, {variables}");

            // The NetworkStrike3 GraphQL API (https://<site>.com/graphql) is protected by Cloudflare
            // based on the client TLS fingerprint: plain HTTP clients (HttpClient) get a "Just a moment..."
            // challenge and cannot reach the API, no matter the cookies. Only a real browser can pass, so
            // when FlareSolverr is configured we route the request through its browser context via a JSON
            // POST (requires the FlareSolverr "contentType: json" patch, see patches/ folder).
            if (FlareSolverr.IsConfigured)
            {
                try
                {
                    var headers = new Dictionary<string, string>
                    {
                        ["Accept"] = "application/json",
                        ["apollo-require-preflight"] = "true",
                        ["x-apollo-operation-name"] = "getSearchResults",
                    };

                    var response = await FlareSolverr.PostJson(url, requestBody, headers, cancellationToken).ConfigureAwait(false);
                    if (response?["data"] != null)
                    {
                        return (JObject)response["data"];
                    }

                    if (response != null)
                    {
                        Logger.Error($"NetworkStrike3: GraphQL Error Response: {response}");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"NetworkStrike3: FlareSolverr request failed, falling back to direct HTTP: {e.Message}");
                }
            }

            return await GetDataFromDirectHttp(url, requestBody, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var variables = string.Format(this.searchVariables, searchTitle, Helper.GetSearchSiteName(siteNum).ToUpper());
            var url = Helper.GetSearchSearchURL(siteNum);
            Logger.Debug($"search: {variables}, {url}");
            var searchResults = await GetDataFromAPI(url, this.searchQuery, variables, cancellationToken).ConfigureAwait(false);
            if (searchResults == null || searchResults["searchVideos"]?["edges"] == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults["searchVideos"]["edges"])
            {
                var node = searchResult["node"];
                if (node == null)
                {
                    continue;
                }

                string sceneURL = (string)node["slug"],
                        sceneName = (string)node["title"];
                if (string.IsNullOrEmpty(sceneURL) || string.IsNullOrEmpty(sceneName))
                {
                    continue;
                }

                var sceneDateObj = node["releaseDate"]?.ToObject<DateTime?>();

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneURL) } },
                    Name = sceneName,
                    PremiereDate = sceneDateObj,
                };

                if (node["images"]?["listing"] is JArray listing && listing.Count > 0)
                {
                    res.ImageUrl = (string)listing[0]["src"];
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

            var sceneURL = Helper.Decode(sceneID[0]).TrimStart('/');

            var variables = string.Format(this.updateVariables, sceneURL, Helper.GetSearchSiteName(siteNum).ToUpper());
            var url = Helper.GetSearchSearchURL(siteNum);
            Logger.Debug($"update: {variables}, {url}");
            var sceneData = await GetDataFromAPI(url, this.updateQuery, variables, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            sceneData = (JObject)sceneData["findOneVideo"];
            if (sceneData == null)
            {
                return result;
            }

            result.Item.ExternalId = Helper.GetSearchBaseURL(siteNum) + $"/videos/{sceneURL}";

            result.Item.Name = (string)sceneData["title"];
            result.Item.Overview = (string)sceneData["description"];
            result.Item.AddStudio(Helper.GetSearchSiteName(siteNum));

            var sceneDateObj = sceneData["releaseDate"]?.ToObject<DateTime?>();
            result.Item.PremiereDate = sceneDateObj;

            if (sceneData["categories"] is JArray categories)
            {
                foreach (var genreLink in categories)
                {
                    string genreName = (string)genreLink["name"];

                    if (!string.IsNullOrEmpty(genreName))
                    {
                        result.Item.AddGenre(genreName);
                    }
                }
            }

            if (sceneData["models"] is JArray models)
            {
                foreach (var actorLink in models)
                {
                    var actor = new PersonInfo
                    {
                        Name = (string)actorLink["name"],
                    };

                    if (string.IsNullOrEmpty(actor.Name))
                    {
                        continue;
                    }

                    if (actorLink["images"]?["listing"] is JArray listing && listing.Count > 0)
                    {
                        actor.ImageUrl = (string)listing[0]["highdpi"]?["double"];
                    }

                    result.AddPerson(actor);
                }
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

            var sceneURL = Helper.Decode(sceneID[0]).TrimStart('/');

            var variables = string.Format(this.updateVariables, sceneURL, Helper.GetSearchSiteName(siteNum).ToUpper());
            var url = Helper.GetSearchSearchURL(siteNum);
            Logger.Debug($"get images: {variables}, {url}");
            var sceneData = await GetDataFromAPI(url, this.updateQuery, variables, cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            var video = (JObject)sceneData["findOneVideo"];
            if (video == null)
            {
                return result;
            }

            if (video["carousel"] is JArray carousel)
            {
                foreach (var image in carousel)
                {
                    if (image["listing"] is JArray listing && listing.Count > 0)
                    {
                        var img = (string)listing[0]["highdpi"]?["triple"];
                        if (string.IsNullOrEmpty(img))
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
            }

            return result;
        }

        private static async Task<JObject> GetDataFromDirectHttp(string url, string requestBody, CancellationToken cancellationToken)
        {
            var param = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var http = await HTTP.Request(url, HttpMethod.Post, param, cancellationToken).ConfigureAwait(false);

            if (http.IsOK)
            {
                Logger.Debug("http.Content: " + http.Content);
                if (!string.IsNullOrEmpty(http.Content))
                {
                    var parsed = JObject.Parse(http.Content);
                    if (parsed["data"] != null)
                    {
                        Logger.Debug("content to jobject ok");
                        return (JObject)parsed["data"];
                    }

                    Logger.Error($"GraphQL Error Response: {http.Content}");
                }
            }

            return null;
        }
    }
}
