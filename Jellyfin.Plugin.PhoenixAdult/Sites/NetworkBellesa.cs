using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class NetworkBellesa : IProviderBase
    {
        private async Task<JArray> GetJSONfromAPI(string type, string query, int[] siteNum, CancellationToken cancellationToken)
        {
            string url = $"{Helper.GetSearchSearchURL(siteNum)}/{type}?{query}";
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Referer", Helper.GetSearchBaseURL(siteNum) },
            };
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken, headers);
            if (!httpResult.IsOK)
            {
                return null;
            }

            return JArray.Parse(httpResult.Content);
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            if (sceneId != null)
            {
                var scenePageElements = await GetJSONfromAPI("videos", $"filter[id]={sceneId}", siteNum, cancellationToken);
                if (scenePageElements != null && scenePageElements.Any())
                {
                    var scene = scenePageElements[0];
                    string titleNoFormatting = Helper.ParseTitle(scene["title"].ToString(), siteNum);
                    string curId = scene["id"].ToString();
                    string subSite = scene["content_provider"][0]["name"].ToString();
                    long date = (long)scene["posted_on"];
                    string releaseDate = DateTimeOffset.FromUnixTimeSeconds(date).ToString("yyyy-MM-dd");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [Bellesa/{subSite}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = scene["image"]?.ToString() ?? string.Empty,
                    });
                }
            }
            else
            {
                var searchResults = await GetJSONfromAPI("search", $"limit=40&order[relevance]=DESC&q={Uri.EscapeDataString(searchTitle)}&providers=bellesa", siteNum, cancellationToken);
                if (searchResults != null)
                {
                    var videos = searchResults.FirstOrDefault()?.SelectToken("videos");
                    if (videos != null)
                    {
                        foreach (var searchResult in videos)
                        {
                            string titleNoFormatting = Helper.ParseTitle(searchResult["title"].ToString(), siteNum);
                            string curId = searchResult["id"].ToString();
                            long date = (long)searchResult["posted_on"];
                            string releaseDate = DateTimeOffset.FromUnixTimeSeconds(date).ToString("yyyy-MM-dd");

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                                Name = $"{titleNoFormatting} [Bellesa] {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
                        }
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
            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var detailsPageElements = await GetJSONfromAPI("videos", $"filter[id]={sceneId}", siteNum, cancellationToken);
            if (detailsPageElements == null || !detailsPageElements.Any())
            {
                return result;
            }

            var scene = detailsPageElements[0];

            var movie = (Movie)result.Item;
            movie.Name = Helper.ParseTitle(scene["title"].ToString(), siteNum);
            movie.Overview = scene["description"].ToString();
            movie.AddStudio("Bellesa");

            string tagline = scene["content_provider"][0]["name"].ToString();
            movie.AddStudio(tagline);
            movie.AddCollection(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genres = scene["tags"].ToString().Split(',');
            foreach (var genre in genres)
            {
                movie.AddGenre(genre.Trim());
            }

            var actors = scene["performers"];
            foreach (var actor in actors)
            {
                string actorName = actor["name"].ToString();
                string actorPhotoUrl = actor["image"]?.ToString();
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneId = sceneID[0].Split('|')[0];

            var detailsPageElements = await GetJSONfromAPI("videos", $"filter[id]={sceneId}", siteNum, cancellationToken);
            if (detailsPageElements != null && detailsPageElements.Any())
            {
                var scene = detailsPageElements[0];
                images.Add(new RemoteImageInfo { Url = scene["image"].ToString(), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
