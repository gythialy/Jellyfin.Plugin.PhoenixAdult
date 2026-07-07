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
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkReptyle : IProviderBase
    {
        private static readonly HashSet<string> familystrokesDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ask Your Mother", "Black Step Dad", "Dad Crush", "Family Strokes", "Family Strokes Features",
            "Foster Tapes", "Not My Grandpa", "Perv Mom", "Perv Nana", "Sis Loves Me", "Tiny Sis"
        };

        private static readonly HashSet<string> freeuseDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Freaky Fembots", "FreeUse", "FreeUse Fantasy", "FreeUse MILF", "FreeUse Singles", "Use POV"
        };

        private static readonly HashSet<string> mylfDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Anal Mom", "BBC Paradise", "Blue Collar Babes", "Full Of JOI", "Got MYLF", "Hijab MYLFs",
            "Hookup Pad", "Lone MILF", "MILF Body", "Milfty", "Mom Drips", "Mom Shoot", "Mommy's Little Man",
            "MYLF", "MYLF After Dark", "MYLF Blows", "MYLF Boss", "MYLF Features", "MYLF of the Month",
            "MYLF Singles", "Mylfdom", "Mylfed", "MylfWood", "New MYLFs", "Oye Mami", "Secrets", "Shag Street",
            "Stay Home MILF", "Tiger Moms"
        };

        private static readonly HashSet<string> pervzDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Charmed", "MILF Taxi", "Perv Doctor", "Perv Driver", "Perv Massage", "Perv Principal",
            "Perv Singles", "Perv Therapy", "Pervz", "Pervz Features", "Shoplyfter MYLF", "Shoplyfter"
        };

        private static readonly HashSet<string> swappzDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Daughter Swap", "Mom Swap", "Sis Swap", "Swappz"
        };

        private static readonly HashSet<string> teamskeetDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "After Dark", "Anal Euro", "Bad MILFs", "BFFs", "Black Valley Girls", "Brace Faced", "Brat Tamer",
            "Breeding Material", "CFNM Teens", "Ciao Bella", "Daddy Pounds", "Dyked", "Exxxtra Small", "Ginger Patch",
            "Glowupz", "Her Freshman Year", "Hijab Hookup", "Hussie Pass", "I Made Porn", "Innocent High", "Kissing Sis",
            "Latina Team", "Little Asians", "Lust HD", "Messy Jessy", "Mormon Girlz", "My Babysitters Club", "My Dirty Uncle",
            "My First", "MYLF Classics", "MYLF Labs", "Our Little Secret", "Oye Loca", "Passport Bros", "Petite Teens 18",
            "POV Life", "Reptyle Classics", "Reptyle Labs", "Rub A Teen", "Self Desire", "Sex and Grades",
            "She's New", "Solo Interviews", "Spanish 18", "Stay Home POV", "Step Siblings", "TeamSkeet AllStars",
            "TeamSkeet Classics", "TeamSkeet Extras", "TeamSkeet Features", "TeamSkeet Labs", "TeamSkeet Singles",
            "TeamSkeet VIP", "TeamSkeet", "Teen Curves", "Teen JOI", "Teen Pies", "Teens Do Porn", "Teens Love Anal",
            "Teens Love Black Cocks", "Teens Love Money", "Teeny Black", "The Loft", "The Real Workout", "Thickumz",
            "This Girl Sucks", "Titty Attack", "Tomboyz"
        };

        private static readonly Dictionary<string, string[]> data18ManualMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "169646", new[] { "thats-better-than-stealing-it" } },
            { "1313219", new[] { "delicious-firsts" } },
            { "1349311", new[] { "thanksgiving-the-hijab-way" } },
            { "1341218", new[] { "the-vamp-next-door" } },
            { "1341212", new[] { "home-for-the-holidays" } }
        };

        private static readonly Dictionary<string, string> ageCookie = new Dictionary<string, string> { { "age_verified", "yes" } };

        private string GetSubNetwork(string subSite, string type = null)
        {
            string subSiteLower = subSite.Replace(" ", "").ToLowerInvariant();

            if (subSiteLower.StartsWith("teamskeetx") || (type == "search" && subSiteLower.StartsWith("mylfx")))
            {
                return "TeamSkeet";
            }

            if (type == null && subSiteLower.StartsWith("mylfx"))
            {
                return "MYLF";
            }

            var databases = new (HashSet<string> db, string name)[]
            {
                (mylfDB, "MYLF"),
                (teamskeetDB, "TeamSkeet"),
                (swappzDB, "Swappz"),
                (freeuseDB, "FreeUse"),
                (pervzDB, "Pervz"),
                (familystrokesDB, "Family Strokes")
            };

            var cleanSubSite = Regex.Replace(subSite, @"\W", "").ToLowerInvariant();
            foreach (var (db, name) in databases)
            {
                if (db.Any(x => Regex.Replace(x, @"\W", "").ToLowerInvariant() == cleanSubSite))
                {
                    return name;
                }
            }

            return null;
        }

        private string GetSubSite(string subSite)
        {
            var databases = new[] { mylfDB, teamskeetDB, swappzDB, freeuseDB, pervzDB, familystrokesDB };
            var cleanSubSite = Regex.Replace(subSite, @"\W", "").ToLowerInvariant();
            foreach (var db in databases)
            {
                foreach (var site in db)
                {
                    if (Regex.Replace(site, @"\W", "").ToLowerInvariant() == cleanSubSite)
                    {
                        return site;
                    }
                }
            }

            return subSite;
        }

        private async Task<JObject> GetJSONfromPage(string url, CancellationToken cancellationToken)
        {
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken, null, ageCookie);
            if (httpResult.IsOK)
            {
                var match = Regex.Match(httpResult.Content, @"window\.__INITIAL_STATE__\s*=\s*(.*?);\s*(?:window\b|<\/script>)", RegexOptions.Singleline);
                if (match.Success)
                {
                    try
                    {
                        var json = JObject.Parse(match.Groups[1].Value);
                        return json["content"] as JObject;
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            return null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string directURL = searchTitle.Replace("'", string.Empty).Slugify();

            string subSite = Regex.Replace(Helper.GetSearchSiteName(siteNum), @"\W", string.Empty);
            string searchNetwork = GetSubNetwork(subSite, "search");
            if (string.IsNullOrEmpty(searchNetwork))
            {
                searchNetwork = "Reptyle";
            }
            string searchNetworkCleanLower = Regex.Replace(searchNetwork, @"\W", string.Empty).ToLowerInvariant();

            string directURL1 = Helper.GetSearchSearchURL(siteNum) + directURL;
            string directURL2 = $"https://www.{searchNetworkCleanLower}.com/movies/{directURL}";

            var searchResultsURLs = new List<string> { directURL1 };
            if (directURL1 != directURL2)
            {
                searchResultsURLs.Add(directURL2);
            }

            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var cleanURL = sceneURL.Split('?')[0];
                if (!searchResultsURLs.Contains(cleanURL) && cleanURL.Contains("/movies/"))
                {
                    searchResultsURLs.Add(cleanURL);
                }
            }

            foreach (var sceneURL in searchResultsURLs)
            {
                var content = await GetJSONfromPage(sceneURL, cancellationToken);
                if (content != null)
                {
                    string sceneType = null;
                    foreach (var type in new[] { "moviesContent", "videosContent" })
                    {
                        if (content[type] != null && content[type].HasValues)
                        {
                            sceneType = type;
                            break;
                        }
                    }

                    if (sceneType != null)
                    {
                        var section = content[sceneType] as JObject;
                        string curID = section.Properties().First().Name;
                        var details = section[curID];
                        if (details == null)
                        {
                            continue;
                        }

                        string titleNoFormatting = Helper.ParseTitle(details["title"]?.ToString(), siteNum);
                        string detailsSubSite = details["site"]?["name"]?.ToString() ?? Helper.GetSearchSiteName(siteNum);

                        string releaseDate = string.Empty;
                        if (details["publishedDate"] != null)
                        {
                            if (DateTime.TryParse(details["publishedDate"].ToString(), out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }
                        }
                        else if (searchDate.HasValue)
                        {
                            releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                        }

                        var score = 100 - LevenshteinDistance.Calculate(searchTitle, titleNoFormatting, StringComparison.OrdinalIgnoreCase);

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}|{sceneType}" } },
                            Name = $"{titleNoFormatting} [{GetSubSite(detailsSubSite)}] {releaseDate}",
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

            string[] idParts = sceneID[0].Split('|');
            string sceneName = idParts[0];
            string sceneDate = idParts.Length > 2 ? idParts[1] : string.Empty;
            string sceneType = idParts.Length > 3 ? idParts[2].Replace("content", "Content") : "moviesContent";

            string searchNetwork = GetSubNetwork(Helper.GetSearchSiteName(siteNum), "search");
            if (string.IsNullOrEmpty(searchNetwork))
            {
                searchNetwork = "Reptyle";
            }
            string searchNetworkCleanLower = Regex.Replace(searchNetwork, @"\W", string.Empty).ToLowerInvariant();

            var detailsPageJson = await GetJSONfromPage(Helper.GetSearchSearchURL(siteNum) + sceneName, cancellationToken);
            JToken detailsPageElements = null;

            if (detailsPageJson != null)
            {
                if (detailsPageJson[sceneType] != null && detailsPageJson[sceneType][sceneName] != null)
                {
                    detailsPageElements = detailsPageJson[sceneType][sceneName];
                }
                else if (detailsPageJson["videosContent"] != null && detailsPageJson["videosContent"][sceneName] != null)
                {
                    detailsPageElements = detailsPageJson["videosContent"][sceneName];
                }
            }

            if (detailsPageElements == null)
            {
                var fallbackJson = await GetJSONfromPage($"https://www.{searchNetworkCleanLower}.com/movies/{sceneName}", cancellationToken);
                if (fallbackJson != null)
                {
                    if (fallbackJson[sceneType] != null && fallbackJson[sceneType][sceneName] != null)
                    {
                        detailsPageElements = fallbackJson[sceneType][sceneName];
                    }
                    else if (fallbackJson["videosContent"] != null && fallbackJson["videosContent"][sceneName] != null)
                    {
                        detailsPageElements = fallbackJson["videosContent"][sceneName];
                    }
                }
            }

            if (detailsPageElements == null)
            {
                return result;
            }

            string detailsSubSite = detailsPageElements["site"]?["name"]?.ToString() ?? Helper.GetSearchSiteName(siteNum);
            string subSite = GetSubSite(detailsSubSite);
            string subNetwork = GetSubNetwork(subSite);

            var movie = (Movie)result.Item;
            movie.ExternalId = $"{Helper.GetSearchBaseURL(siteNum)}/movies/{sceneName}";
            movie.Name = Helper.ParseTitle(detailsPageElements["title"]?.ToString(), siteNum);
            movie.Overview = HTML.StripHtml(detailsPageElements["description"]?.ToString() ?? string.Empty);
            movie.AddStudio(string.IsNullOrEmpty(subNetwork) ? "Reptyle" : subNetwork);

            if (subSite != subNetwork)
            {
                movie.AddStudio(subSite);
            }
            movie.AddCollection(subSite);

            if (DateTime.TryParse(sceneDate, out var date))
            {
                movie.PremiereDate = date;
                movie.ProductionYear = date.Year;
            }
            else if (detailsPageElements["publishedDate"] != null && DateTime.TryParse(detailsPageElements["publishedDate"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var models = detailsPageElements["models"] as JArray;
            if (models != null)
            {
                foreach (var model in models)
                {
                    string actorID = model["modelId"]?.ToString() ?? model["id"]?.ToString();
                    string actorName = model["modelName"]?.ToString() ?? model["name"]?.ToString();
                    if (string.IsNullOrEmpty(actorName))
                    {
                        continue;
                    }

                    string actorPhotoURL = string.Empty;
                    try
                    {
                        var actorData = await GetJSONfromPage($"{Helper.GetSearchBaseURL(siteNum)}/models/{actorID}", cancellationToken);
                        if (actorData != null && actorData["modelsContent"] != null && actorData["modelsContent"][actorID] != null)
                        {
                            actorPhotoURL = actorData["modelsContent"][actorID]["img"]?.ToString();
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    if (string.IsNullOrEmpty(actorPhotoURL))
                    {
                        try
                        {
                            var actorData = await GetJSONfromPage($"https://www.{searchNetworkCleanLower}.com/models/{actorID}", cancellationToken);
                            if (actorData != null && actorData["modelsContent"] != null && actorData["modelsContent"][actorID] != null)
                            {
                                actorPhotoURL = actorData["modelsContent"][actorID]["img"]?.ToString();
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            var tags = detailsPageElements["tags"] as JArray;
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    string genreName = tag?.ToString().Trim();
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            if (models != null && models.Count > 1 && subSite != "Mylfed")
            {
                movie.AddGenre("Threesome");
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] idParts = sceneID[0].Split('|');
            string sceneName = idParts[0];
            string sceneType = idParts.Length > 2 ? idParts[2].Replace("content", "Content") : "moviesContent";

            var detailsPageJson = await GetJSONfromPage(Helper.GetSearchSearchURL(siteNum) + sceneName, cancellationToken);
            JToken detailsPageElements = null;

            if (detailsPageJson != null)
            {
                if (detailsPageJson[sceneType] != null && detailsPageJson[sceneType][sceneName] != null)
                {
                    detailsPageElements = detailsPageJson[sceneType][sceneName];
                }
                else if (detailsPageJson["videosContent"] != null && detailsPageJson["videosContent"][sceneName] != null)
                {
                    detailsPageElements = detailsPageJson["videosContent"][sceneName];
                }
            }

            if (detailsPageElements != null)
            {
                string imageUrl = detailsPageElements["img"]?.ToString();
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
