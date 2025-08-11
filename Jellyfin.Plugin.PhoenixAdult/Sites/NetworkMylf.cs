using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PhoenixAdult.Extensions;
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
    public class NetworkMylf : IProviderBase
    {
        private static readonly Dictionary<string, string[]> GenresDB = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "MylfBoss", new[] { "Office", "Boss" } },
            { "MylfBlows", new[] { "Blowjob" } },
            { "Milfty", new[] { "Cheating" } },
            { "MomDrips", new[] { "Creampie" } },
            { "Milf Body", new[] { "Gym", "Fitness" } },
            { "Lone MILF", new[] { "Solo" } },
            { "Full Of JOI", new[] { "JOI" } },
            { "Mylfed", new[] { "Lesbian", "Girl on Girl", "GG" } },
            { "MylfDom", new[] { "BDSM" } },
        };

        private async Task<JObject> GetJSONfromPage(string url, CancellationToken cancellationToken)
        {
            var http = await HTTP.Request(url, cancellationToken, null, new Dictionary<string, string> { { "age_verified", "yes" } });
            if (http.IsOK)
            {
                var match = new Regex(@"window\.__INITIAL_STATE__ = (.*);").Match(http.Content);
                if (match.Success)
                    return (JObject)JObject.Parse(match.Groups[1].Value)["content"];
            }
            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
                return result;

            var searchResultsURLs = new HashSet<string>
            {
                $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Slugify()}"
            };

            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                if (sceneURL.Contains("/movies/"))
                    searchResultsURLs.Add(sceneURL.Split('?')[0]);
            }

            foreach (var url in searchResultsURLs)
            {
                var detailsPageElements = await GetJSONfromPage(url, cancellationToken);
                if (detailsPageElements == null) continue;

                string sceneType = null;
                foreach (var type in new[] { "moviesContent", "videosContent" })
                {
                    if (detailsPageElements[type]?.Any() == true)
                    {
                        sceneType = type;
                        break;
                    }
                }

                if (sceneType != null)
                {
                    var sceneData = (JObject)detailsPageElements[sceneType];
                    string curID = ((JProperty)sceneData.First).Name;
                    var details = (JObject)sceneData[curID];
                    string titleNoFormatting = (string)details["title"];
                    string subSite = (string)details["site"]?["name"] ?? Helper.GetSearchSiteName(siteNum);

                    string releaseDateStr = string.Empty;
                    if (details["publishedDate"] != null && DateTime.TryParse((string)details["publishedDate"], out var releaseDate))
                        releaseDateStr = releaseDate.ToString("yyyy-MM-dd");
                    else if (searchDate.HasValue)
                        releaseDateStr = searchDate.Value.ToString("yyyy-MM-dd");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDateStr}|{sceneType}" } },
                        Name = $"{titleNoFormatting} [{subSite}] {releaseDateStr}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = (string)details["img"]
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
            string sceneName = providerIds[0];
            string sceneDate = providerIds[2];
            string sceneType = providerIds[3];

            var detailsPageElements = await GetJSONfromPage($"{Helper.GetSearchSearchURL(siteNum)}{sceneName}", cancellationToken);
            if (detailsPageElements?[sceneType]?[sceneName] == null) return result;
            var details = (JObject)detailsPageElements[sceneType][sceneName];

            var movie = (Movie)result.Item;
            movie.Name = (string)details["title"];
            movie.Overview = HTML.StripHtml((string)details["description"]);
            movie.AddStudio("MYLF");

            string subSite = (string)details["site"]?["name"] ?? Helper.GetSearchSiteName(siteNum);
            movie.AddTag(subSite);

            if (DateTime.TryParse(sceneDate, out var releaseDate))
            {
                movie.PremiereDate = releaseDate;
                movie.ProductionYear = releaseDate.Year;
            }

            var genres = new List<string> { "MILF", "Mature" };
            if (details["tags"]?.Any() == true)
            {
                foreach(var tag in details["tags"])
                    genres.Add((string)tag);
            }
            if(details["models"].Count() > 1 && subSite != "Mylfed")
                genres.Add("Threesome");
            if(GenresDB.ContainsKey(subSite))
                genres.AddRange(GenresDB[subSite]);

            foreach(var genre in genres.Distinct())
                movie.AddGenre(genre);

            foreach (var actorLink in details["models"])
            {
                string actorID = (string)actorLink["modelId"] ?? (string)actorLink["id"];
                string actorName = (string)actorLink["modelName"] ?? (string)actorLink["name"];
                string actorPhotoURL = string.Empty;

                var actorData = await GetJSONfromPage($"{Helper.GetSearchBaseURL(siteNum)}/models/{actorID}", cancellationToken);
                if (actorData?["modelsContent"]?[actorID] != null)
                    actorPhotoURL = (string)actorData["modelsContent"][actorID]["img"];

                result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonType.Actor });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string sceneName = providerIds[0];
            string sceneType = providerIds[3];

            var detailsPageElements = await GetJSONfromPage($"{Helper.GetSearchSearchURL(siteNum)}{sceneName}", cancellationToken);
            if (detailsPageElements?[sceneType]?[sceneName] == null) return result;
            var details = (JObject)detailsPageElements[sceneType][sceneName];

            string img = (string)details["img"];
            result.Add(new RemoteImageInfo { Url = img, Type = ImageType.Primary });
            result.Add(new RemoteImageInfo { Url = img, Type = ImageType.Backdrop });

            return result;
        }
    }
}
