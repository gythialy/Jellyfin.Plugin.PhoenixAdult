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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

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
                if (!string.IsNullOrEmpty(sceneID) && string.IsNullOrEmpty(searchTitle))
                {
                    url = $"{Helper.GetSearchSearchURL(siteNum)}/v2/releases?type={sceneType}&id={sceneID}";
                }
                else
                {
                    url = $"{Helper.GetSearchSearchURL(siteNum)}/v2/releases?type={sceneType}&search={encodedSearchTitle}";
                }

                var searchResults = await GetDataFromAPI(url, instanceToken, cancellationToken).ConfigureAwait(false);
                if (searchResults?["result"] == null)
                {
                    continue;
                }

                foreach (var searchResult in searchResults["result"])
                {
                    string titleNoFormatting = searchResult["title"].ToString().Replace("ï¿½", "'");
                    DateTime releaseDate = (DateTime)searchResult["dateReleased"];
                    string curID = searchResult["id"].ToString();
                    string siteName = searchResult["brand"].ToString();
                    string subSite = searchResult["collections"]?.FirstOrDefault()?["name"]?.ToString().Trim() ?? string.Empty;
                    string siteDisplay = !string.IsNullOrEmpty(subSite) ? $"{siteName}/{subSite}" : siteName;

                    if (sceneType == "trailer")
                    {
                        titleNoFormatting = $"[{sceneType.First().ToString().ToUpper() + sceneType.Substring(1)}] {titleNoFormatting}";
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{sceneType}" } },
                        Name = $"{titleNoFormatting} [{siteDisplay}] {releaseDate:yyyy-MM-dd}",
                        SearchProviderName = Plugin.Instance.Name,
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

            string[] providerIds = sceneID[0].Split('|');
            string curID = providerIds[0];
            string[] idParts = curID.Split('#');
            int siteGroup = int.Parse(idParts[0]);
            int siteId = int.Parse(idParts[1]);
            var siteNumArr = new int[] { siteGroup, siteId };
            string sceneType = providerIds[2];

            var instanceToken = await GetToken(siteNumArr, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(instanceToken))
            {
                return result;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNumArr)}/v2/releases?type={sceneType}&id={curID}";
            var detailsPageElements = await GetDataFromAPI(url, instanceToken, cancellationToken).ConfigureAwait(false);
            if (detailsPageElements?["result"]?.FirstOrDefault() == null)
            {
                return result;
            }

            var details = detailsPageElements["result"][0];

            var movie = (Movie)result.Item;
            movie.Name = details["title"].ToString().Replace("ï¿½", "'");

            string description = details["description"]?.ToString();
            if (string.IsNullOrEmpty(description))
            {
                description = details["parent"]?["description"]?.ToString();
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
            if (details["parent"]?["title"] != null)
            {
                seriesNames.Add(details["parent"]["title"].ToString());
            }

            string mainSiteName = Helper.GetSearchSiteName(siteNumArr);
            if (!seriesNames.Contains(mainSiteName))
            {
                movie.AddTag(mainSiteName);
            }

            foreach (var seriesName in seriesNames)
            {
                movie.AddTag(seriesName);
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
                    var actorPageURL = $"{Helper.GetSearchSearchURL(siteNumArr)}/v1/actors?id={actorLink["id"]}";
                    var actorData = await GetDataFromAPI(actorPageURL, instanceToken, cancellationToken);
                    if (actorData?["result"]?.FirstOrDefault() == null)
                    {
                        continue;
                    }

                    var actorDetails = actorData["result"][0];
                    string actorPhotoUrl = actorDetails["images"]?["profile"]?["0"]?["xs"]?["url"]?.ToString() ?? string.Empty;
                    result.People.Add(new PersonInfo { Name = actorDetails["name"].ToString(), Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            string[] providerIds = sceneID[0].Split('|');
            string curID = providerIds[0];
            string[] idParts = curID.Split('#');
            int siteGroup = int.Parse(idParts[0]);
            int siteId = int.Parse(idParts[1]);
            var siteNumArr = new int[] { siteGroup, siteId };
            string sceneType = providerIds[2];

            var instanceToken = await GetToken(siteNumArr, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(instanceToken))
            {
                return images;
            }

            var url = $"{Helper.GetSearchSearchURL(siteNumArr)}/v2/releases?type={sceneType}&id={curID}";
            var detailsPageElements = await GetDataFromAPI(url, instanceToken, cancellationToken).ConfigureAwait(false);
            if (detailsPageElements?["result"]?.FirstOrDefault() == null)
            {
                return images;
            }

            var details = detailsPageElements["result"][0];
            var imageUrls = new List<string>();

            foreach (var imageType in new[] { "poster", "cover" })
            {
                if (details["images"]?[imageType] != null)
                {
                    var sortedImages = details["images"][imageType].Values<JProperty>().OrderBy(p => p.Name);
                    foreach (var image in sortedImages)
                    {
                        imageUrls.Add(image.Value["xx"]["url"].ToString());
                    }
                }
            }

            bool first = true;
            foreach(var imageUrl in imageUrls.Distinct())
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
