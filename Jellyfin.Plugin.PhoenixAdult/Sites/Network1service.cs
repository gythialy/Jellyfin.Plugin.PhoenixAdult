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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class Network1service : IProviderBase
    {
        private async Task<string> GetToken(int[] siteNum, CancellationToken cancellationToken)
        {
            var result = string.Empty;

            if (siteNum == null)
            {
                return result;
            }

            var db = new JObject();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.TokenStorage))
            {
                try
                {
                    db = JObject.Parse(Plugin.Instance.Configuration.TokenStorage);
                }
                catch (JsonReaderException)
                {
                    // Ignore if parsing fails, will be overwritten
                }
            }

            var keyName = new Uri(Helper.GetSearchBaseURL(siteNum)).Host;
            if (db.ContainsKey(keyName))
            {
                string token = (string)db[keyName];
                var tokenParts = token.Split('.');
                if (tokenParts.Length > 1)
                {
                    try
                    {
                        string res = Encoding.UTF8.GetString(Helper.ConvertFromBase64String(tokenParts[1]) ?? Array.Empty<byte>());
                        if ((int)JObject.Parse(res)["exp"] > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                        {
                            result = token;
                        }
                    }
                    catch
                    {
                        // Invalid token format, will fetch a new one
                    }
                }
            }

            if (string.IsNullOrEmpty(result))
            {
                var http = await HTTP.Request(Helper.GetSearchBaseURL(siteNum), HttpMethod.Head, cancellationToken).ConfigureAwait(false);
                var instanceToken = http.Cookies?.FirstOrDefault(o => o.Name == "instance_token");
                if (instanceToken == null)
                {
                    return result;
                }

                result = instanceToken.Value;
                db[keyName] = result;

                Plugin.Instance.Configuration.TokenStorage = JsonConvert.SerializeObject(db);
                Plugin.Instance.SaveConfiguration();
            }

            return result;
        }

        private async Task<JObject> GetDataFromAPI(string url, string instance, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Instance", instance },
            };

            var http = await HTTP.Request(url, cancellationToken, headers).ConfigureAwait(false);
            if (http.IsOK)
            {
                Logger.Info($"[Network1service] GetAPI content: {http.Content}");
            }

            return http.IsOK ? JObject.Parse(http.Content) : null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var instanceToken = await GetToken(siteNum, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(instanceToken))
            {
                return result;
            }

            string sceneID = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneID = parts[0];
                searchTitle = searchTitle.Substring(sceneID.Length).Trim();
            }

            string encodedSearchTitle = Uri.EscapeDataString(searchTitle);

            foreach (var sceneType in new[] { "scene", "movie", "serie", "trailer" })
            {
                string url;
                if (!string.IsNullOrEmpty(sceneID))
                {
                    url = $"{Helper.GetSearchSearchURL(siteNum)}/v2/releases?type={sceneType}&id={sceneID}";
                }
                else
                {
                    url = $"{Helper.GetSearchSearchURL(siteNum)}/v2/releases?type={sceneType}&search={encodedSearchTitle}";
                }

                Logger.Info($"[Network1service] search url: {url}");

                var searchResults = await GetDataFromAPI(url, instanceToken, cancellationToken).ConfigureAwait(false);
                var results = searchResults?.SelectToken("result");
                if (results == null || results.Type == JTokenType.Null)
                {
                    continue;
                }

                foreach (var searchResult in results)
                {
                    string titleNoFormatting = searchResult["title"].ToString().Replace("ï¿½", "'");
                    DateTime releaseDate = (DateTime)searchResult["dateReleased"];
                    string curID = searchResult["id"].ToString();
                    string siteName = searchResult["brand"].ToString();
                    string subSite = searchResult.SelectToken("collections")?.FirstOrDefault()?.SelectToken("name")?.ToString().Trim() ?? string.Empty;
                    string siteDisplay = !string.IsNullOrEmpty(subSite) ? $"{siteName}/{subSite}" : siteName;
                    string imageUrl = string.Empty;
                    var imageToken = searchResult.SelectToken($"images.poster.0");
                    if (imageToken != null && imageToken.Type != JTokenType.Null)
                    {
                        imageUrl = imageToken.SelectToken("xx.url")?.ToString();
                    }

                    if (sceneType == "trailer")
                    {
                        titleNoFormatting = $"[{sceneType.First().ToString().ToUpper() + sceneType.Substring(1)}] {titleNoFormatting}";
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{sceneType}" } },
                        Name = $"{titleNoFormatting} [{siteDisplay}] {releaseDate:yyyy-MM-dd}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = imageUrl,
                    });
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            Logger.Info($"[Network1service] Update called. siteNum: {(siteNum != null ? string.Join(",", siteNum) : "null")}, sceneID: {(sceneID != null ? string.Join(",", sceneID) : "null")}");
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };

            string[] providerIds = sceneID[0].Split('|');
            Logger.Info($"[Network1service] Update providerIds: {string.Join(" / ", providerIds)}");
            string curID = providerIds[0];
            string sceneType = providerIds[1];

            var instanceToken = await GetToken(siteNum, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(instanceToken))
            {
                return result;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNum)}/v2/releases?type={sceneType}&id={curID}";
            var detailsPageElements = await GetDataFromAPI(url, instanceToken, cancellationToken).ConfigureAwait(false);
            var details = detailsPageElements?.SelectToken("result")?.FirstOrDefault();
            if (details == null || details.Type == JTokenType.Null)
            {
                return result;
            }

            var movie = (Movie)result.Item;

            string domain = new Uri(Helper.GetSearchBaseURL(siteNum)).Host;

            switch (domain)
            {
                case "www.brazzers.com":
                    if (sceneType.Equals("serie", StringComparison.OrdinalIgnoreCase) || sceneType.Equals("scene", StringComparison.OrdinalIgnoreCase))
                    {
                        sceneType = "video";
                    }

                    break;
            }

            var sceneURL = Helper.GetSearchBaseURL(siteNum) + $"/{sceneType}/{sceneID[0]}/0";

            result.Item.ExternalId = sceneURL;

            movie.Name = details["title"].ToString().Replace("ï¿½", "'");

            string description = details["description"]?.ToString();
            if (string.IsNullOrEmpty(description))
            {
                description = details.SelectToken("parent.description")?.ToString();
            }

            movie.Overview = description;

            movie.AddStudio(details["brand"].ToString());

            var seriesNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (details["collections"] != null)
            {
                foreach (var collection in details["collections"])
                {
                    seriesNames.Add(collection["name"].ToString());
                }
            }

            var parentTitle = details.SelectToken("parent.title")?.ToString();
            if (!string.IsNullOrEmpty(parentTitle))
            {
                seriesNames.Add(parentTitle);
            }

            string mainSiteName = Helper.GetSearchSiteName(siteNum);
            if (!seriesNames.Contains(mainSiteName))
            {
                movie.AddStudio(mainSiteName);
            }

            foreach (var seriesName in seriesNames)
            {
                movie.AddStudio(seriesName);
            }

            DateTime dateObject = (DateTime)details["dateReleased"];
            movie.PremiereDate = dateObject;
            movie.ProductionYear = dateObject.Year;

            if (details["tags"] != null)
            {
                foreach (var genreLink in details["tags"])
                {
                    movie.AddGenre(genreLink["name"].ToString());
                }
            }

            if (details["actors"] != null)
            {
                foreach (var actorLink in details["actors"])
                {
                    var actorPageURL = $"{Helper.GetSearchSearchURL(siteNum)}/v1/actors?id={actorLink["id"]}";
                    var actorData = await GetDataFromAPI(actorPageURL, instanceToken, cancellationToken);
                    var actorDetails = actorData?.SelectToken("result")?.FirstOrDefault();
                    if (actorDetails == null || actorDetails.Type == JTokenType.Null)
                    {
                        continue;
                    }

                    string actorPhotoUrl = actorDetails.SelectToken("images.profile.0.xs.url")?.ToString() ?? string.Empty;
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorDetails["name"].ToString(), Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            Logger.Info($"[Network1service] GetImages called. siteNum: {(siteNum != null ? string.Join(",", siteNum) : "null")}, sceneID: {(sceneID != null ? string.Join(",", sceneID) : "null")}");
            var images = new List<RemoteImageInfo>();

            string[] providerIds = sceneID[0].Split('|');
            Logger.Info($"[Network1service] GetImages providerIds: {string.Join(" / ", providerIds)}");
            string curID = providerIds[0];
            string sceneType = providerIds[1];

            var instanceToken = await GetToken(siteNum, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(instanceToken))
            {
                return images;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNum)}/v2/releases?type={sceneType}&id={curID}";
            var detailsPageElements = await GetDataFromAPI(url, instanceToken, cancellationToken).ConfigureAwait(false);
            var details = detailsPageElements?.SelectToken("result")?.FirstOrDefault();
            if (details == null || details.Type == JTokenType.Null)
            {
                return images;
            }

            var imageUrls = new List<string>();

            foreach (var imageType in new[] { "poster", "cover" })
            {
                var imageToken = details.SelectToken($"images.{imageType}");
                if (imageToken != null && imageToken.Type != JTokenType.Null)
                {
                    if (imageToken is JObject imageObject)
                    {
                        var imageProperties = imageObject.Properties()
                            .Where(p => int.TryParse(p.Name, out _))
                            .OrderBy(p => p.Name);

                        foreach (var imageProp in imageProperties)
                        {
                            var imageUrl = imageProp.Value?.SelectToken("xx.url")?.ToString();
                            if (!string.IsNullOrEmpty(imageUrl))
                            {
                                imageUrls.Add(imageUrl);
                            }
                        }
                    }
                    else if (imageToken is JValue)
                    {
                        imageUrls.Add(imageToken.ToString());
                    }
                }
            }

            bool first = true;

            foreach (var imageUrl in imageUrls.Distinct())
            {
                var imageInfo = new RemoteImageInfo { Url = imageUrl };
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

            return images;
        }
    }
}
