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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteManyVids : IProviderBase
    {
        private async Task<JObject> GetJSONfromPage(string url, CancellationToken cancellationToken)
        {
            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                var html = HTML.ElementFromString(http.Content);
                var ldJsonNode = html.SelectSingleNode("//script[@type='application/ld+json']");
                if (ldJsonNode != null)
                {
                    try
                    {
                        return JObject.Parse(ldJsonNode.InnerText);
                    }
                    catch (Exception ex)
                    {
                        Logger.Info($"[ManyVids] Failed to parse JSON-LD from {url}: {ex.Message}");
                    }
                }
                else
                {
                    Logger.Info($"[ManyVids] JSON-LD script tag not found on page: {url}");
                }
            }
            else
            {
                Logger.Info($"[ManyVids] Failed to fetch page: {url}. Status: {http.StatusCode}");
            }

            return null;
        }

        private async Task<JObject> GetDataFromAPI(string url, CancellationToken cancellationToken)
        {
            var http = await HTTP.Request(url, cancellationToken);
            if (http.IsOK)
            {
                try
                {
                    return (JObject)JObject.Parse(http.Content)["data"];
                }
                catch (Exception ex)
                {
                    Logger.Info($"[ManyVids] Failed to parse API response from {url}: {ex.Message}");
                }
            }
            else
            {
                Logger.Info($"[ManyVids] API Request failed: {url}. Status: {http.StatusCode}");
            }

            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            Logger.Info($"[ManyVids] Searching for: '{searchTitle}'");
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string sceneID = searchTitle.Split(' ')[0];
            if (!int.TryParse(sceneID, out _))
            {
                Logger.Info($"[ManyVids] Search title '{searchTitle}' does not start with a numeric ID. ManyVids search requires the ID.");
                return result;
            }

            string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/video/{sceneID}";
            var searchResult = await GetJSONfromPage(sceneUrl, cancellationToken);
            if (searchResult == null)
            {
                Logger.Info($"[ManyVids] No data found for URL: {sceneUrl}");
                return result;
            }

            string titleNoFormatting = (string)searchResult["name"];
            string curID = Helper.Encode(sceneUrl);
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

            Logger.Info($"[ManyVids] Updating metadata for URL: {sceneURL}");

            string videoID = sceneURL.Split('/').Last().Split('-')[0];
            var videoPageElements = await GetDataFromAPI($"https://www.manyvids.com/bff/store/video/{videoID}", cancellationToken);
            if (videoPageElements == null)
            {
                Logger.Info($"[ManyVids] Failed to get API data for video ID: {videoID}");
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
            if (actor != null)
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo
                {
                    Name = (string)actor["displayName"],
                    ImageUrl = (string)actor["avatar"],
                    Type = PersonKind.Actor,
                });
            }

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