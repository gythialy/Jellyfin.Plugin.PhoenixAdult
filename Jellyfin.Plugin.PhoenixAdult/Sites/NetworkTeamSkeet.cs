using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
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
    public class NetworkTeamSkeet : IProviderBase
    {
        private readonly Dictionary<string, string> _cookies = new Dictionary<string, string> { { "age_verified", "yes" } };
        private static readonly Dictionary<string, List<string>> genresDB = new Dictionary<string, List<string>>
        {
            { "Anal Mom", new List<string> { "Anal", "MILF" } }, { "BFFs", new List<string> { "Teen", "Group Sex" } },
            { "Black Valley Girls", new List<string> { "Teen", "Ebony" } }, { "DadCrush", new List<string> { "Step Dad", "Step Daughter" } },
            { "DaughterSwap", new List<string> { "Step Dad", "Step Daughter" } }, { "Dyked", new List<string> { "Hardcore", "Teen", "Lesbian" } },
            { "Exxxtra Small", new List<string> { "Teen", "Small Tits" } }, { "Family Strokes", new List<string> { "Taboo Family" } },
            { "Foster Tapes", new List<string> { "Taboo Sex" } }, { "Freeuse Fantasy", new List<string> { "Freeuse" } },
            { "Ginger Patch", new List<string> { "Redhead" } }, { "Innocent High", new List<string> { "School Girl" } },
            { "Little Asians", new List<string> { "Asian", "Teen" } }, { "Not My Grandpa", new List<string> { "Older/Younger" } },
            { "Oye Loca", new List<string> { "Latina" } }, { "PervMom", new List<string> { "Step Mom" } },
            { "POV Life", new List<string> { "POV" } }, { "Shoplyfter", new List<string> { "Strip" } },
            { "ShoplyfterMylf", new List<string> { "Strip", "MILF" } }, { "Sis Loves Me", new List<string> { "Step Sister" } },
            { "Teen Curves", new List<string> { "Big Ass" } }, { "Teen Pies", new List<string> { "Teen", "Creampie" } },
            { "TeenJoi", new List<string> { "Teen", "JOI" } }, { "Teens Do Porn", new List<string> { "Teen" } },
            { "Teens Love Black Cocks", new List<string> { "Teens", "BBC" } }, { "Teeny Black", new List<string> { "Teen", "Ebony" } },
            { "Thickumz", new List<string> { "Thick" } }, { "Titty Attack", new List<string> { "Big Tits" } },
        };

        private string Slugify(string phrase)
        {
            string str = phrase.ToLowerInvariant();
            str = System.Text.Encoding.ASCII.GetString(System.Text.Encoding.GetEncoding("Cyrillic").GetBytes(str));
            str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s-]", string.Empty);
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim();
            str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
            str = System.Text.RegularExpressions.Regex.Replace(str, @"\s", "-");
            return str;
        }

        private async Task<JToken> GetJsonFromPage(string url, CancellationToken cancellationToken)
        {
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken, null, _cookies);
            if (httpResult.IsOK)
            {
                var match = Regex.Match(httpResult.Content, @"window\.__INITIAL_STATE__ = (.*);");
                if (match.Success)
                {
                    return JObject.Parse(match.Groups[1].Value)["content"];
                }
            }

            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string directUrl = Slugify(searchTitle.Replace("'", string.Empty));
            directUrl = Helper.GetSearchSearchURL(siteNum) + directUrl;
            var searchResultsUrls = new List<string> { directUrl };

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            searchResultsUrls.AddRange(googleResults.Where(u => u.Contains("/movies/")));

            foreach (var sceneUrl in searchResultsUrls.Distinct())
            {
                var detailsPageElements = await GetJsonFromPage(sceneUrl, cancellationToken);
                if (detailsPageElements != null)
                {
                    string sceneType = null;
                    if (detailsPageElements["moviesContent"] != null)
                    {
                        sceneType = "moviesContent";
                    }
                    else if (detailsPageElements["videosContent"] != null)
                    {
                        sceneType = "videosContent";
                    }

                    if (sceneType != null)
                    {
                        var content = detailsPageElements[sceneType].First as JProperty;
                        var details = content.Value;
                        string curId = content.Name;
                        string titleNoFormatting = details["title"].ToString();
                        string subSite = details.SelectToken("site.name")?.ToString() ?? Helper.GetSearchSiteName(siteNum);
                        string releaseDate = string.Empty;
                        if (details["publishedDate"] != null && DateTime.TryParse(details["publishedDate"].ToString(), out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}|{sceneType.Replace("Content", string.Empty)}" } },
                            Name = $"{titleNoFormatting} [{subSite}] {releaseDate}",
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
            string sceneName = providerIds[0];
            string sceneDate = providerIds[1];
            string sceneType = providerIds[2] + "Content";

            var json = await GetJsonFromPage($"{Helper.GetSearchSearchURL(siteNum)}{sceneName}", cancellationToken);
            var detailsPageElements = json?.SelectToken($"{sceneType}.{sceneName}");
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements["title"].ToString();
            movie.Overview = detailsPageElements["description"].ToString();
            movie.AddStudio("TeamSkeet");

            string tagline = detailsPageElements.SelectToken("site.name")?.ToString() ?? Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            if (detailsPageElements["tags"] != null)
            {
                foreach (var genre in detailsPageElements["tags"])
                {
                    movie.AddGenre(genre.ToString().Trim());
                }
            }

            if (genresDB.ContainsKey(tagline))
            {
                foreach (var genre in genresDB[tagline])
                {
                    movie.AddGenre(genre);
                }
            }

            foreach (var actor in detailsPageElements["models"])
            {
                string actorId = actor["modelId"]?.ToString() ?? actor["id"]?.ToString();
                string actorName = actor["modelName"]?.ToString() ?? actor["name"]?.ToString();
                string actorPhotoUrl = string.Empty;
                var actorData = await GetJsonFromPage($"{Helper.GetSearchBaseURL(siteNum)}/models/{actorId}", cancellationToken);
                if (actorData != null)
                {
                    actorPhotoUrl = actorData["modelsContent"][actorId]["img"].ToString();
                }

                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneName = sceneID[0].Split('|')[0];
            string sceneType = sceneID[0].Split('|')[2] + "Content";

            var json = await GetJsonFromPage($"{Helper.GetSearchSearchURL(siteNum)}{sceneName}", cancellationToken);
            var detailsPageElements = json?.SelectToken($"{sceneType}.{sceneName}");
            if (detailsPageElements != null)
            {
                var img = detailsPageElements.SelectToken("img")?.ToString();
                if (!string.IsNullOrEmpty(img))
                {
                    images.Add(new RemoteImageInfo { Url = img, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
