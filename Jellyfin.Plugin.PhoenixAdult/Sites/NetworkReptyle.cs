using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkReptyle : IProviderBase
    {
        private static readonly HashSet<string> familystrokesDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Ask Your Mother", "Black Step Dad", "Dad Crush", "Family Strokes", "Family Strokes Features",
            "Foster Tapes", "Not My Grandpa", "Perv Mom", "Perv Nana", "Sis Loves Me", "Tiny Sis",
        };

        private static readonly HashSet<string> freeuseDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Freaky Fembots", "FreeUse", "FreeUse Fantasy", "FreeUse MILF", "FreeUse Singles", "Use POV",
        };

        private static readonly HashSet<string> mylfDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Anal Mom", "BBC Paradise", "Blue Collar Babes", "Full Of JOI", "Got MYLF", "Hijab MYLFs",
            "Hookup Pad", "Lone MILF", "MILF Body", "Milfty", "Mom Drips", "Mom Shoot", "Mommy's Little Man",
            "MYLF", "MYLF After Dark", "MYLF Blows", "MYLF Boss", "MYLF Features", "MYLF of the Month",
            "MYLF Singles", "Mylfdom", "Mylfed", "MylfWood", "New MYLFs", "Oye Mami", "Secrets", "Shag Street",
            "Stay Home MILF", "Tiger Moms",
        };

        private static readonly HashSet<string> pervzDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Charmed", "MILF Taxi", "Perv Doctor", "Perv Driver", "Perv Massage", "Perv Principal",
            "Perv Singles", "Perv Therapy", "Pervz", "Pervz Features", "Shoplyfter MYLF", "Shoplyfter",
        };

        private static readonly HashSet<string> swappzDB = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Daughter Swap", "Mom Swap", "Sis Swap", "Swappz",
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
            "This Girl Sucks", "Titty Attack", "Tomboyz",
        };

        private static readonly Dictionary<string, string[]> data18ManualMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "169646", new[] { "thats-better-than-stealing-it" } },
            { "1313219", new[] { "delicious-firsts" } },
            { "1349311", new[] { "thanksgiving-the-hijab-way" } },
            { "1341218", new[] { "the-vamp-next-door" } },
            { "1341212", new[] { "home-for-the-holidays" } },
        };

        private static readonly Dictionary<string, string> ageCookie = new Dictionary<string, string> { { "age_verified", "yes" } };

        private class ReptyleSceneDetails
        {
            public string Id { get; set; }

            public string Title { get; set; }

            public string Description { get; set; }

            public string ImageUrl { get; set; }

            public string PublishedDate { get; set; }

            public DateTime? ParsedDate { get; set; }

            public string SubSite { get; set; }

            public string SceneType { get; set; } = "videosContent";

            public List<ReptyleActorDetails> Models { get; set; } = new List<ReptyleActorDetails>();

            public List<string> Tags { get; set; } = new List<string>();
        }

        private class ReptyleActorDetails
        {
            public string Id { get; set; }

            public string Name { get; set; }

            public string ImageUrl { get; set; }
        }

        private string GetSubNetwork(string subSite, string type = null)
        {
            string subSiteLower = subSite.Replace(" ", string.Empty).ToLowerInvariant();

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
                (familystrokesDB, "Family Strokes"),
            };

            var cleanSubSite = Regex.Replace(subSite, @"\W", string.Empty).ToLowerInvariant();
            foreach (var (db, name) in databases)
            {
                if (db.Any(x => Regex.Replace(x, @"\W", string.Empty).ToLowerInvariant() == cleanSubSite))
                {
                    return name;
                }
            }

            return null;
        }

        private string GetSubSite(string subSite)
        {
            var databases = new[] { mylfDB, teamskeetDB, swappzDB, freeuseDB, pervzDB, familystrokesDB };
            var cleanSubSite = Regex.Replace(subSite, @"\W", string.Empty).ToLowerInvariant();
            foreach (var db in databases)
            {
                foreach (var site in db)
                {
                    if (Regex.Replace(site, @"\W", string.Empty).ToLowerInvariant() == cleanSubSite)
                    {
                        return site;
                    }
                }
            }

            return subSite;
        }

        private async Task<ReptyleSceneDetails> GetSceneDetailsFromPage(string url, CancellationToken cancellationToken, string expectedSlug = null)
        {
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken, null, ageCookie);
            if (!httpResult.IsOK || string.IsNullOrEmpty(httpResult.Content))
            {
                return null;
            }

            // 1. Try window.__INITIAL_STATE__ (Legacy React SPA layout)
            var match = Regex.Match(httpResult.Content, @"window\.__INITIAL_STATE__\s*=\s*(.*?);\s*(?:window\b|<\/script>)", RegexOptions.Singleline);
            if (match.Success)
            {
                try
                {
                    var json = JObject.Parse(match.Groups[1].Value);
                    if (json["content"] is JObject content)
                    {
                        foreach (var type in new[] { "moviesContent", "videosContent" })
                        {
                            if (content[type] is JObject section && section.HasValues)
                            {
                                JToken details = null;
                                string slug = expectedSlug;
                                if (!string.IsNullOrEmpty(slug) && section[slug] != null)
                                {
                                    details = section[slug];
                                }
                                else
                                {
                                    var prop = section.Properties().FirstOrDefault();
                                    if (prop != null)
                                    {
                                        slug = prop.Name;
                                        details = prop.Value;
                                    }
                                }

                                if (details != null)
                                {
                                    var scene = new ReptyleSceneDetails
                                    {
                                        Id = slug,
                                        Title = details["title"]?.ToString() ?? details["videoTitle"]?.ToString(),
                                        Description = details["description"]?.ToString(),
                                        ImageUrl = details["img"]?.ToString(),
                                        SubSite = details["site"]?["name"]?.ToString(),
                                        SceneType = type,
                                    };

                                    if (details["publishedDate"] != null && DateTime.TryParse(details["publishedDate"].ToString(), out var parsedDate))
                                    {
                                        scene.ParsedDate = parsedDate;
                                        scene.PublishedDate = parsedDate.ToString("yyyy-MM-dd");
                                    }

                                    if (details["models"] is JArray models)
                                    {
                                        foreach (var m in models)
                                        {
                                            string actorId = m["id"]?.ToString() ?? m["modelId"]?.ToString();
                                            string actorName = m["name"]?.ToString() ?? m["title"]?.ToString() ?? m["modelName"]?.ToString();
                                            string actorImg = m["img"]?.ToString();
                                            if (!string.IsNullOrEmpty(actorName))
                                            {
                                                scene.Models.Add(new ReptyleActorDetails { Id = actorId, Name = actorName, ImageUrl = actorImg });
                                            }
                                        }
                                    }

                                    if (details["tags"] is JArray tags)
                                    {
                                        foreach (var tag in tags)
                                        {
                                            string genreName = tag?.ToString()?.Trim();
                                            if (!string.IsNullOrEmpty(genreName))
                                            {
                                                scene.Tags.Add(genreName);
                                            }
                                        }
                                    }

                                    return scene;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Error parsing __INITIAL_STATE__: {ex.Message}");
                }
            }

            // 2. Try JSON-LD and HTML DOM (Astro SSR / new layout)
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(httpResult.Content);
                var root = doc.DocumentNode;

                JObject videoLd = null;
                var ldScripts = root.SelectNodesSafe("//script[@type='application/ld+json']");
                foreach (var s in ldScripts)
                {
                    var text = s.InnerText?.Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    try
                    {
                        var parsed = JToken.Parse(text);
                        if (parsed is JObject jObj && jObj["@type"]?.ToString() == "VideoObject")
                        {
                            videoLd = jObj;
                            break;
                        }
                        else if (parsed is JArray jArr)
                        {
                            var vo = jArr.OfType<JObject>().FirstOrDefault(x => x["@type"]?.ToString() == "VideoObject");
                            if (vo != null)
                            {
                                videoLd = vo;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (videoLd != null || root.SelectSingleNodeSafe("//h1") != null)
                {
                    string slug = expectedSlug;
                    if (string.IsNullOrEmpty(slug))
                    {
                        try
                        {
                            var uri = new Uri(url);
                            slug = uri.AbsolutePath.TrimEnd('/').Split('/').Last();
                        }
                        catch
                        {
                            slug = string.Empty;
                        }
                    }

                    string title = videoLd?["name"]?.ToString()
                        ?? root.SelectSingleNodeSafe("//h1[contains(@class, 'meta-title')] | //h1")?.InnerText?.Trim()
                        ?? root.SelectSingleNodeSafe("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty);

                    if (string.IsNullOrEmpty(title))
                    {
                        return null;
                    }

                    string desc = videoLd?["description"]?.ToString()
                        ?? root.SelectSingleNodeSafe("//meta[@name='description'] | //meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);

                    string img = videoLd?["thumbnailUrl"]?.ToString()
                        ?? root.SelectSingleNodeSafe("//meta[@property='og:image'] | //meta[@name='twitter:image']")?.GetAttributeValue("content", string.Empty);

                    var scene = new ReptyleSceneDetails
                    {
                        Id = slug,
                        Title = title,
                        Description = desc,
                        ImageUrl = img,
                        SceneType = "videosContent",
                    };

                    string dateStr = videoLd?["uploadDate"]?.ToString();
                    if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var pDate))
                    {
                        scene.ParsedDate = pDate;
                        scene.PublishedDate = pDate.ToString("yyyy-MM-dd");
                    }

                    // Extract Actors
                    if (videoLd?["actor"] is JArray actorArr)
                    {
                        foreach (var a in actorArr)
                        {
                            string aName = a["name"]?.ToString();
                            string aUrl = a["url"]?.ToString();
                            string aId = !string.IsNullOrEmpty(aUrl) ? aUrl.TrimEnd('/').Split('/').Last() : string.Empty;
                            if (!string.IsNullOrEmpty(aName))
                            {
                                scene.Models.Add(new ReptyleActorDetails { Id = aId, Name = aName });
                            }
                        }
                    }

                    if (!scene.Models.Any())
                    {
                        var modelNodes = root.SelectNodesSafe("//p[contains(@class, 'starring')]//a | //a[starts-with(@href, '/models/')]");
                        var seenActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var mNode in modelNodes)
                        {
                            string aName = mNode.InnerText?.Trim();
                            string aHref = mNode.GetAttributeValue("href", string.Empty);
                            string aId = aHref.TrimEnd('/').Split('/').Last();
                            if (!string.IsNullOrEmpty(aName) && !seenActors.Contains(aName) && !aName.Equals("See All", StringComparison.OrdinalIgnoreCase) && !aName.Equals("Models", StringComparison.OrdinalIgnoreCase))
                            {
                                seenActors.Add(aName);
                                scene.Models.Add(new ReptyleActorDetails { Id = aId, Name = aName });
                            }
                        }
                    }

                    // Extract Tags
                    if (videoLd?["genre"] is JArray genreArr)
                    {
                        foreach (var g in genreArr)
                        {
                            string genre = g?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(genre))
                            {
                                scene.Tags.Add(genre);
                            }
                        }
                    }
                    else if (videoLd?["genre"] != null)
                    {
                        string genre = videoLd["genre"].ToString().Trim();
                        if (!string.IsNullOrEmpty(genre))
                        {
                            scene.Tags.Add(genre);
                        }
                    }

                    var tagNodes = root.SelectNodesSafe("//ul[contains(@class, 'tags')]//li | //a[starts-with(@href, '/categories/')]");
                    foreach (var tNode in tagNodes)
                    {
                        string tag = tNode.InnerText?.Trim();
                        if (!string.IsNullOrEmpty(tag) && !scene.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        {
                            scene.Tags.Add(tag);
                        }
                    }

                    // Extract SubSite
                    string subSite = root.SelectSingleNodeSafe("//a[contains(@class, 'series-link')]")?.InnerText?.Trim();
                    if (string.IsNullOrEmpty(subSite))
                    {
                        subSite = root.SelectSingleNodeSafe("//*[contains(@class, 'head-logo')]//img")?.GetAttributeValue("alt", string.Empty)?.Trim();
                    }

                    if (string.IsNullOrEmpty(subSite))
                    {
                        subSite = root.SelectSingleNodeSafe("//meta[@property='og:site_name']")?.GetAttributeValue("content", string.Empty)?.Trim();
                    }

                    scene.SubSite = subSite;

                    return scene;
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error parsing JSON-LD / HTML: {ex.Message}");
            }

            return null;
        }

        private async Task<string> GetActorPhoto(string actorID, string baseURL, string searchNetworkCleanLower, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(actorID))
            {
                return string.Empty;
            }

            var urls = new List<string>
            {
                $"{baseURL}/models/{actorID}",
                $"https://www.{searchNetworkCleanLower}.com/models/{actorID}",
            };

            foreach (var actorUrl in urls.Distinct())
            {
                try
                {
                    var httpResult = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken, null, ageCookie);
                    if (!httpResult.IsOK || string.IsNullOrEmpty(httpResult.Content))
                    {
                        continue;
                    }

                    var match = Regex.Match(httpResult.Content, @"window\.__INITIAL_STATE__\s*=\s*(.*?);\s*(?:window\b|<\/script>)", RegexOptions.Singleline);
                    if (match.Success)
                    {
                        try
                        {
                            var json = JObject.Parse(match.Groups[1].Value);
                            if (json["content"]?["modelsContent"]?[actorID]?["img"] != null)
                            {
                                string img = json["content"]["modelsContent"][actorID]["img"].ToString();
                                if (!string.IsNullOrEmpty(img))
                                {
                                    return img;
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    var doc = new HtmlDocument();
                    doc.LoadHtml(httpResult.Content);
                    var ogImg = doc.DocumentNode.SelectSingleNodeSafe("//meta[@property='og:image'] | //meta[@name='twitter:image']")?.GetAttributeValue("content", string.Empty);
                    if (!string.IsNullOrEmpty(ogImg) && !ogImg.Contains("logo", StringComparison.OrdinalIgnoreCase))
                    {
                        return ogImg;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return string.Empty;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            string cleanTitle = searchTitle;
            var idPrefixMatch = Regex.Match(searchTitle, @"^\s*\d+\s*[-_–—]\s*(.+)$");
            if (idPrefixMatch.Success && !string.IsNullOrWhiteSpace(idPrefixMatch.Groups[1].Value))
            {
                cleanTitle = idPrefixMatch.Groups[1].Value.Trim();
            }
            else
            {
                var split = searchTitle.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length > 1 && int.TryParse(split[0], out _) && split[0].Length >= 3)
                {
                    cleanTitle = string.Join(" ", split.Skip(1)).Trim();
                }
            }

            var idSuffixMatch = Regex.Match(cleanTitle, @"^(.+?)\s*[-_–—]\s*\d+$");
            if (idSuffixMatch.Success && !string.IsNullOrWhiteSpace(idSuffixMatch.Groups[1].Value))
            {
                cleanTitle = idSuffixMatch.Groups[1].Value.Trim();
            }

            string directURL = cleanTitle.Replace("'", string.Empty).Slugify();

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

            var googleResults = await WebSearch.GetSearchResults(cleanTitle, siteNum, cancellationToken);
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
                string expectedSlug = sceneURL.Split('?')[0].TrimEnd('/').Split('/').Last();
                var details = await GetSceneDetailsFromPage(sceneURL, cancellationToken, expectedSlug);
                if (details != null)
                {
                    string curID = details.Id;
                    string titleNoFormatting = Helper.ParseTitle(details.Title, siteNum);
                    string detailsSubSite = !string.IsNullOrEmpty(details.SubSite) ? details.SubSite : Helper.GetSearchSiteName(siteNum);

                    string releaseDate = details.PublishedDate ?? string.Empty;
                    if (string.IsNullOrEmpty(releaseDate) && searchDate.HasValue)
                    {
                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                    }

                    var score = 100 - LevenshteinDistance.Calculate(cleanTitle, titleNoFormatting, StringComparison.OrdinalIgnoreCase);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{releaseDate}|{details.SceneType}" } },
                        Name = $"{titleNoFormatting} [{GetSubSite(detailsSubSite)}] {releaseDate}",
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

            string[] idParts = sceneID[0].Split('|');
            string sceneName = idParts[0];
            string sceneDate = idParts.Length > 1 ? idParts[1] : string.Empty;
            string sceneType = idParts.Length > 2 ? idParts[2].Replace("content", "Content") : "videosContent";

            string searchNetwork = GetSubNetwork(Helper.GetSearchSiteName(siteNum), "search");
            if (string.IsNullOrEmpty(searchNetwork))
            {
                searchNetwork = "Reptyle";
            }

            string searchNetworkCleanLower = Regex.Replace(searchNetwork, @"\W", string.Empty).ToLowerInvariant();

            var details = await GetSceneDetailsFromPage(Helper.GetSearchSearchURL(siteNum) + sceneName, cancellationToken, sceneName);

            if (details == null)
            {
                details = await GetSceneDetailsFromPage($"https://www.{searchNetworkCleanLower}.com/movies/{sceneName}", cancellationToken, sceneName);
            }

            if (details == null)
            {
                return result;
            }

            string detailsSubSite = !string.IsNullOrEmpty(details.SubSite) ? details.SubSite : Helper.GetSearchSiteName(siteNum);
            string subSite = GetSubSite(detailsSubSite);
            string subNetwork = GetSubNetwork(subSite);

            var movie = (Movie)result.Item;
            movie.ExternalId = $"{Helper.GetSearchBaseURL(siteNum)}/movies/{sceneName}";
            movie.Name = Helper.ParseTitle(details.Title, siteNum);
            movie.Overview = HTML.StripHtml(details.Description ?? string.Empty);
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
            else if (details.ParsedDate.HasValue)
            {
                movie.PremiereDate = details.ParsedDate.Value;
                movie.ProductionYear = details.ParsedDate.Value.Year;
            }

            if (details.Models != null)
            {
                foreach (var model in details.Models)
                {
                    string actorID = model.Id;
                    string actorName = model.Name;
                    if (string.IsNullOrEmpty(actorName))
                    {
                        continue;
                    }

                    string actorPhotoURL = model.ImageUrl;
                    if (string.IsNullOrEmpty(actorPhotoURL))
                    {
                        actorPhotoURL = await GetActorPhoto(actorID, Helper.GetSearchBaseURL(siteNum), searchNetworkCleanLower, cancellationToken);
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            if (details.Tags != null)
            {
                foreach (var tag in details.Tags)
                {
                    string genreName = tag?.Trim();
                    if (!string.IsNullOrEmpty(genreName))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            if (details.Models != null && details.Models.Count > 1 && subSite != "Mylfed")
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

            string searchNetwork = GetSubNetwork(Helper.GetSearchSiteName(siteNum), "search");
            if (string.IsNullOrEmpty(searchNetwork))
            {
                searchNetwork = "Reptyle";
            }

            string searchNetworkCleanLower = Regex.Replace(searchNetwork, @"\W", string.Empty).ToLowerInvariant();

            var details = await GetSceneDetailsFromPage(Helper.GetSearchSearchURL(siteNum) + sceneName, cancellationToken, sceneName);

            if (details == null)
            {
                details = await GetSceneDetailsFromPage($"https://www.{searchNetworkCleanLower}.com/movies/{sceneName}", cancellationToken, sceneName);
            }

            if (details != null && !string.IsNullOrEmpty(details.ImageUrl))
            {
                images.Add(new RemoteImageInfo { Url = details.ImageUrl, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
