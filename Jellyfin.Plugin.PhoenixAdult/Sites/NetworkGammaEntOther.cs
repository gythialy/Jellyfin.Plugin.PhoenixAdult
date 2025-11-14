using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkGammaEntOther : IProviderBase
    {
        private async Task<string> GetApiKey(int[] siteNum, CancellationToken cancellationToken)
        {
            string url = $"{Helper.GetSearchBaseURL(siteNum)}/en/login";
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                //Logger.Info($"[NetworkGammaEntOther] GetApiKey called. httpResult.Content: {httpResult.Content}");
                var match = Regex.Match(httpResult.Content, "\"apiKey\":\"([^\"]+)\"");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            // Fallback if login page doesn't work
            url = $"{Helper.GetSearchBaseURL(siteNum)}/en";
            httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                //Logger.Info($"[NetworkGammaEntOther] GetApiKey called. httpResult.Content fallback: {httpResult.Content}");
                var match = Regex.Match(httpResult.Content, "\"apiKey\":\"([^\"]+)\"");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        private async Task<JArray> GetAlgolia(string url, string indexName, string parameters, string referer, CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Referer", referer },
                { "Content-Type", "application/json" },
            };

            var payload = new
            {
                requests = new[]
                {
                    new
                    {
                        indexName,
                        @params = parameters,
                    },
                },
            };
            var httpResult = await HTTP.Request(url, HttpMethod.Post, new StringContent(JsonConvert.SerializeObject(payload)), headers, null, cancellationToken);
            Logger.Info($"[NetworkGammaEntOther] GetAlgolia called. httpResult.IsOK:{httpResult.IsOK}");
            Logger.Info($"[NetworkGammaEntOther] GetAlgolia called. httpResult.StatusCode: {httpResult.StatusCode}");

            //Logger.Info($"[NetworkGammaEntOther] GetAlgolia called. httpResult.Content: {httpResult.Content}");
            if (!httpResult.IsOK)
            {
                return null;
            }

            //Logger.Info($"[NetworkGammaEntOther] results: {httpResult.Content}");
            var results = JObject.Parse(httpResult.Content)["results"];
            if (results is JArray resultsArray && resultsArray.Count > 0)
            {
                return resultsArray[0]["hits"] as JArray;
            }

            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out _))
            {
                sceneId = searchTitle.Split(' ').First();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            Logger.Info($"[NetworkGammaEntOther] Search called. Id and Title: {sceneId} : {searchTitle}");

            string apiKey = await GetApiKey(siteNum, cancellationToken);
            if (apiKey == null)
            {
                return result;
            }

            Logger.Info($"[NetworkGammaEntOther] Search called. apiKey: {apiKey}");

            foreach (var sceneType in new[] { "scenes", "movies" })
            {
                string url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKey}";
                string parameters = sceneId != null
                    ? $"filters={(sceneType == "scenes" ? "clip_id" : "movie_id")}={sceneId}"
                    : $"query={Uri.EscapeDataString(searchTitle)}";

                var searchResults = await GetAlgolia(url, $"all_{sceneType}", parameters, Helper.GetSearchBaseURL(siteNum), cancellationToken);
                if (searchResults != null)
                {
                    foreach (var searchResult in searchResults)
                    {
                        string titleNoFormatting = Helper.ParseTitle(searchResult["title"].ToString(), siteNum);
                        string curId = searchResult[sceneType == "scenes" ? "clip_id" : "movie_id"].ToString();
                        string releaseDate = string.Empty;
                        if (DateTime.TryParse(searchResult[sceneType == "scenes" ? "release_date" : "date_created"].ToString(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{sceneType}|{releaseDate}" } },
                            Name = $"[{sceneType.Capitalize()}] {titleNoFormatting} {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
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
            string sceneId = providerIds[0];
            string sceneType = providerIds[1];
            string sceneDate = providerIds[2];
            string apiKey = await GetApiKey(siteNum, cancellationToken);
            if (apiKey == null)
            {
                return result;
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKey}";
            string sceneIdName = sceneType == "scenes" ? "clip_id" : "movie_id";

            var detailsPageElements = (await GetAlgolia(url, $"all_{sceneType}", $"filters={sceneIdName}={sceneId}", Helper.GetSearchBaseURL(siteNum), cancellationToken))?.FirstOrDefault();
            if (detailsPageElements == null)
            {
                return result;
            }

            Logger.Info($"[NetworkGammaEntOther] content: {detailsPageElements.ToString()}");

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(detailsPageElements["title"].ToString(), siteNum);
            movie.Overview = detailsPageElements["description"].ToString().Replace("</br>", "\n").Replace("<br>", "\n");
            movie.AddStudio(detailsPageElements["network_name"]?.ToString() ?? detailsPageElements["studio_name"]?.ToString() ?? Helper.GetSearchSiteName(siteNum));

            if (DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genre in detailsPageElements["categories"])
            {
                movie.AddGenre(genre["name"]?.ToString());
            }

            foreach (var actor in detailsPageElements["actors"])
            {
                string actorName = actor["name"]?.ToString();
                var actorData = (await GetAlgolia(url, "all_actors", $"filters=actor_id={actor["actor_id"]}", Helper.GetSearchBaseURL(siteNum), cancellationToken))?.FirstOrDefault();
                string actorPhotoUrl = string.Empty;
                var pictures = actorData?.SelectToken("pictures");
                if (pictures != null && pictures.Type != JTokenType.Null)
                {
                    var maxQuality = (pictures as JObject)?.Properties().Select(p => p.Name).OrderByDescending(q => q).FirstOrDefault();
                    if (maxQuality != null)
                    {
                        actorPhotoUrl = $"https://images-fame.gammacdn.com/actors{pictures[maxQuality]}";
                    }
                }

                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneId = providerIds[0];
            string sceneType = providerIds[1];
            string apiKey = await GetApiKey(siteNum, cancellationToken);
            if (apiKey == null)
            {
                return images;
            }

            string url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKey}";
            string sceneIdName = sceneType == "scenes" ? "clip_id" : "movie_id";

            var detailsPageElements = (await GetAlgolia(url, $"all_{sceneType}", $"filters={sceneIdName}={sceneId}", Helper.GetSearchBaseURL(siteNum), cancellationToken))?.FirstOrDefault();
            if (detailsPageElements == null)
            {
                return images;
            }

            var pictures = detailsPageElements.SelectToken("pictures");
            if (pictures is JObject)
            {
                var top = pictures.SelectToken("nsfw.top");
                if (top is JObject topObject)
                {
                    var maxQuality = topObject.Properties().Select(p => p.Name).OrderByDescending(q => q).FirstOrDefault();
                    if (maxQuality != null)
                    {
                        var picturePath = pictures[maxQuality]?.ToString();
                        if (!string.IsNullOrEmpty(picturePath))
                        {
                            string pictureUrl = $"https://images-fame.gammacdn.com/movies/{picturePath}";
                            images.Add(new RemoteImageInfo { Url = pictureUrl, Type = ImageType.Primary });
                        }
                    }
                }
            }

            return images;
        }
    }
}
