using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
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
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class NetworkGammaEnt : IProviderBase
    {
        public static async Task<string> GetAPIKey(int[] siteNum, CancellationToken cancellationToken)
        {
            var result = string.Empty;

            if (siteNum == null)
            {
                return result;
            }

            var db = new JObject();
            if (!string.IsNullOrEmpty(Plugin.Instance.Configuration.TokenStorage))
            {
                db = JObject.Parse(Plugin.Instance.Configuration.TokenStorage);
            }

            var keyName = new Uri(Helper.GetSearchBaseURL(siteNum)).Host;
            if (db.ContainsKey(keyName))
            {
                string token = (string)db[keyName],
                    res = Encoding.UTF8.GetString(Helper.ConvertFromBase64String(token) ?? Array.Empty<byte>());

                if (res.Contains("validUntil") && int.TryParse(res.Split("validUntil=")[1].Split("&")[0], out var timestamp))
                {
                    if (timestamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        result = token;
                    }
                }
            }

            if (string.IsNullOrEmpty(result))
            {
                // Algolia apiKey comes from the home page (/en/login rate-limits with 429).
                var http = await HTTP.Request(Helper.GetSearchBaseURL(siteNum) + "/en", cancellationToken).ConfigureAwait(false);
                if (http.IsOK)
                {
                    var regEx = Regex.Match(http.Content, "\"apiKey\":\"(.*?)\"");
                    if (regEx.Groups.Count > 0)
                    {
                        result = regEx.Groups[1].Value;
                    }
                }

                // No key on the home page (standalone template): use sub-site 0, Algolia shares the index.
                if (string.IsNullOrEmpty(result) && siteNum.Length > 1 && siteNum[1] != 0)
                {
                    var groupBase = Helper.GetSearchBaseURL(new[] { siteNum[0], 0 });
                    if (!string.Equals(groupBase, Helper.GetSearchBaseURL(siteNum), StringComparison.OrdinalIgnoreCase))
                    {
                        http = await HTTP.Request(groupBase + "/en", cancellationToken).ConfigureAwait(false);
                        if (http.IsOK)
                        {
                            var regExFallback = Regex.Match(http.Content, "\"apiKey\":\"(.*?)\"");
                            if (regExFallback.Groups.Count > 0)
                            {
                                result = regExFallback.Groups[1].Value;
                            }
                        }
                    }
                }

                if (db.ContainsKey(keyName))
                {
                    db[keyName] = result;
                }
                else
                {
                    db.Add(keyName, result);
                }

                Plugin.Instance.Configuration.TokenStorage = JsonConvert.SerializeObject(db);
                Plugin.Instance.SaveConfiguration();
            }

            return result;
        }

        public static async Task<JObject> GetDataFromAPI(string url, string indexName, string referer, string searchParams, CancellationToken cancellationToken)
        {
            JObject json = null;

            var text = $"{{'requests':[{{'indexName':'{indexName}','params':'{searchParams}'}}]}}".Replace('\'', '"');
            var param = new StringContent(text, Encoding.UTF8, "application/json");
            var headers = new Dictionary<string, string>
            {
                { "Referer",  referer },
            };

            var http = await HTTP.Request(url, HttpMethod.Post, param, cancellationToken, headers).ConfigureAwait(false);
            if (http.IsOK)
            {
                json = JObject.Parse(http.Content);
            }

            return json;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            // all_movies has no pictures; fetch one scene preview per movie_id from the scenes index.
            var moviesWithoutImage = new List<KeyValuePair<RemoteSearchResult, string>>();

            var searchSceneID = searchTitle.Split()[0];
            if (!int.TryParse(searchSceneID, out _))
            {
                searchSceneID = null;
            }

            string apiKEY = await GetAPIKey(siteNum, cancellationToken).ConfigureAwait(false),
                   searchParams;

            var sceneTypes = new List<string> { "scenes", "movies" };
            foreach (var sceneType in sceneTypes)
            {
                if (!string.IsNullOrEmpty(searchSceneID))
                {
                    if (sceneType == "scenes")
                    {
                        searchParams = $"filters=clip_id={searchSceneID}";
                    }
                    else
                    {
                        searchParams = $"filters=movie_id={searchSceneID}";
                    }
                }
                else
                {
                    var searchTitleCleaned = searchTitle.Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase);
                    searchParams = $"query={searchTitleCleaned}";
                }

                var url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
                var searchResults = await GetDataFromAPI(url, $"all_{sceneType}", Helper.GetSearchBaseURL(new[] { siteNum[0], 0 }), searchParams, cancellationToken).ConfigureAwait(false);

                if (searchResults == null)
                {
                    return result;
                }

                foreach (JObject searchResult in searchResults["results"].First["hits"])
                {
                    string sceneID,
                        sceneName = (string)searchResult["title"],
                        curID;
                    DateTime? sceneDateObj;

                    if (sceneType == "scenes")
                    {
                        sceneDateObj = (DateTime?)searchResult["release_date"];
                        sceneID = (string)searchResult["clip_id"];
                    }
                    else
                    {
                        // all_movies.last_modified is always JSON null, so fall back to date_created
                        // for dates, sorting and the date prefix.
                        var dateToken = searchResult["last_modified"];
                        if (dateToken == null || dateToken.Type == JTokenType.Null)
                        {
                            dateToken = searchResult["date_created"];
                        }

                        sceneDateObj = dateToken == null || dateToken.Type == JTokenType.Null ? null : (DateTime?)dateToken;
                        sceneID = (string)searchResult["movie_id"];
                    }

                    var res = new RemoteSearchResult
                    {
                        Name = sceneName,
                    };

                    curID = $"{sceneID}#{sceneType}";

                    if (sceneDateObj.HasValue)
                    {
                        var sceneDate = sceneDateObj.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        curID += $"#{sceneDate}";
                        res.PremiereDate = sceneDateObj;
                    }

                    res.ProviderIds.Add(Plugin.Instance.Name, curID);

                    if (searchResult["pictures"] is JObject pictures)
                    {
                        var pictureURL = GetBestPictureURL(pictures);
                        if (!string.IsNullOrEmpty(pictureURL))
                        {
                            res.ImageUrl = "https://images-fame.gammacdn.com/movies" + pictureURL;
                        }
                    }

                    if (string.IsNullOrEmpty(res.ImageUrl) && sceneType != "scenes")
                    {
                        // The movie cover only exists for some titles, so leave it empty
                        // and fill it from a scene preview later.
                        moviesWithoutImage.Add(new KeyValuePair<RemoteSearchResult, string>(res, sceneID));
                    }

                    result.Add(res);
                }
            }

            if (moviesWithoutImage.Count > 0)
            {
                await FillMoviePreviewImages(siteNum, apiKEY, moviesWithoutImage, cancellationToken).ConfigureAwait(false);
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

            string apiKEY = await GetAPIKey(siteNum, cancellationToken).ConfigureAwait(false),
                   sceneType = sceneID[1] == "scenes" ? "clip_id" : "movie_id",
                   url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
            var sceneData = await GetDataFromAPI(url, $"all_{sceneID[1]}", Helper.GetSearchBaseURL(new[] { siteNum[0], 0 }), $"filters={sceneType}={sceneID[0]}", cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            sceneData = (JObject)sceneData["results"].First["hits"].First;

            string domain = new Uri(Helper.GetSearchBaseURL(siteNum)).Host,
                sceneTypeURL = sceneID[1] == "scenes" ? "video" : "movie";

            if (sceneTypeURL.Equals("movie", StringComparison.OrdinalIgnoreCase))
            {
                switch (domain)
                {
                    case "freetour.adulttime.com":
                        sceneTypeURL = string.Empty;
                        break;

                    case "www.burningangel.com":
                    case "www.devilsfilm.com":
                    case "www.roccosiffredi.com":
                    case "www.genderx.com":
                        sceneTypeURL = "dvd";
                        break;
                }
            }

            var sceneURL = Helper.GetSearchBaseURL(siteNum) + $"/en/{sceneTypeURL}/0/{sceneID[0]}/";

            if (!string.IsNullOrWhiteSpace(sceneTypeURL))
            {
                result.Item.ExternalId = sceneURL;
            }

            result.Item.Name = (string)sceneData["title"];
            var description = (string)sceneData["description"];
            result.Item.Overview = description.Replace("</br>", "\n", StringComparison.OrdinalIgnoreCase);

            var network = (string)sceneData["network_name"];
            if (network != null)
            {
                result.Item.AddStudio(network);
            }

            if (sceneData.ContainsKey("studio_name"))
            {
                result.Item.AddStudio((string)sceneData["studio_name"]);
            }

            if (DateTime.TryParseExact(sceneID[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sceneDateObj))
            {
                result.Item.PremiereDate = sceneDateObj;
            }

            foreach (var genreLink in sceneData["categories"])
            {
                var genreName = (string)genreLink["name"];

                if (!string.IsNullOrEmpty(genreName))
                {
                    result.Item.AddGenre(genreName);
                }
            }

            foreach (var actorLink in sceneData["actors"])
            {
                string actorName = (string)actorLink["name"],
                       actorPhotoURL = string.Empty;

                var data = await GetDataFromAPI(url, "all_actors", Helper.GetSearchBaseURL(siteNum), $"filters=actor_id={actorLink["actor_id"]}", cancellationToken).ConfigureAwait(false);
                if (data != null)
                {
                    var actorData = data["results"].First["hits"].First;
                    if (actorData["pictures"] != null)
                    {
                        actorPhotoURL = (string)actorData["pictures"].Last;
                    }

                    var actor = new PersonInfo
                    {
                        Name = actorName,
                    };

                    if (actorPhotoURL != null)
                    {
                        actor.ImageUrl = $"https://images-fame.gammacdn.com/actors{actorPhotoURL}";
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

            string apiKEY = await GetAPIKey(siteNum, cancellationToken).ConfigureAwait(false),
                   sceneType = sceneID[1] == "scenes" ? "clip_id" : "movie_id",
                   url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
            var sceneData = await GetDataFromAPI(url, $"all_{sceneID[1]}", Helper.GetSearchBaseURL(new[] { siteNum[0], 0 }), $"filters={sceneType}={sceneID[0]}", cancellationToken).ConfigureAwait(false);
            if (sceneData == null)
            {
                return result;
            }

            sceneData = (JObject)sceneData["results"].First["hits"].First;

            var ignore = false;
            string imageURL;

            if (sceneID[1].Equals("scenes", StringComparison.OrdinalIgnoreCase))
            {
                ignore = true;
            }
            else
            {
                var siteList = new List<string>
                {
                    "girlsway.com", "puretaboo.com",
                };

                foreach (var site in siteList)
                {
                    if (Helper.GetSearchBaseURL(siteNum).EndsWith(site, StringComparison.OrdinalIgnoreCase))
                    {
                        ignore = true;
                        break;
                    }
                }
            }

            if (!ignore)
            {
                imageURL = GetMovieCoverURL(sceneData);

                if (!string.IsNullOrEmpty(imageURL))
                {
                    result.Add(new RemoteImageInfo
                    {
                        Url = imageURL,
                        Type = ImageType.Primary,
                    });
                }
            }

            if (sceneData["pictures"] is JObject pictures)
            {
                var pictureURL = GetBestPictureURL(pictures);
                if (!string.IsNullOrEmpty(pictureURL))
                {
                    imageURL = "https://images-fame.gammacdn.com/movies" + pictureURL;

                    result.Add(new RemoteImageInfo
                    {
                        Url = imageURL,
                        Type = ImageType.Primary,
                    });
                    result.Add(new RemoteImageInfo
                    {
                        Url = imageURL,
                        Type = ImageType.Backdrop,
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Front cover URL for a movie (the scenes index holds scene previews).
        /// Reachable for the EvilAngel/AdultTime family.
        /// </summary>
        internal static string GetMovieCoverURL(JObject movie)
        {
            if (movie == null)
            {
                return null;
            }

            var movieID = (string)(movie["movie_id"] ?? movie["movieId"]);
            var title = (string)(movie["url_movie_title"] ?? movie["url_title"]);
            if (string.IsNullOrEmpty(movieID) || string.IsNullOrEmpty(title))
            {
                return null;
            }

            var slug = title.ToLowerInvariant().Replace('-', '_');

            return $"https://images-fame.gammacdn.com/movies/{movieID}/{movieID}_{slug}_front_400x625.jpg";
        }

        /// <summary>
        /// Largest image path from an Algolia pictures object.
        /// Keys are WxH resolutions; resized is a thumbnail and nsfw is nested,
        /// so take the widest key and fall back to any string value.
        /// </summary>
        internal static string GetBestPictureURL(JObject pictures)
        {
            if (pictures == null)
            {
                return null;
            }

            string best = null;
            var bestWidth = 0;
            string fallback = null;

            foreach (var property in pictures.Properties())
            {
                if (property.Value.Type != JTokenType.String)
                {
                    continue; // nsfw 等嵌套对象
                }

                var value = (string)property.Value;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                fallback ??= value;

                if (Regex.IsMatch(property.Name, @"^\d+x\d+$", RegexOptions.IgnoreCase)
                    && int.TryParse(property.Name.Split('x')[0], out var width)
                    && width > bestWidth)
                {
                    bestWidth = width;
                    best = value;
                }
            }

            return best ?? fallback;
        }

        /// <summary>
        /// all_movies has no pictures, so one batch query against all_scenes fetches the preview.
        /// </summary>
        private static async Task FillMoviePreviewImages(int[] siteNum, string apiKEY, List<KeyValuePair<RemoteSearchResult, string>> movies, CancellationToken cancellationToken)
        {
            var movieIDs = movies.Select(o => o.Value).Where(o => !string.IsNullOrEmpty(o)).Distinct().Take(20).ToList();
            if (movieIDs.Count == 0)
            {
                return;
            }

            var searchParams = $"filters={string.Join(" OR ", movieIDs.Select(o => $"movie_id={o}"))}&hitsPerPage={movieIDs.Count * 2}&attributesToRetrieve=movie_id,pictures";
            var url = $"{Helper.GetSearchSearchURL(siteNum)}?x-algolia-application-id=TSMKFA364Q&x-algolia-api-key={apiKEY}";
            var data = await GetDataFromAPI(url, "all_scenes", Helper.GetSearchBaseURL(new[] { siteNum[0], 0 }), searchParams, cancellationToken).ConfigureAwait(false);
            if (data == null)
            {
                return;
            }

            var pictures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject hit in data["results"].First["hits"])
            {
                var movieID = (string)hit["movie_id"];
                if (string.IsNullOrEmpty(movieID) || pictures.ContainsKey(movieID) || hit["pictures"] is not JObject scenePictures)
                {
                    continue;
                }

                var pictureURL = GetBestPictureURL(scenePictures);
                if (!string.IsNullOrEmpty(pictureURL))
                {
                    pictures[movieID] = "https://images-fame.gammacdn.com/movies" + pictureURL;
                }
            }

            foreach (var movie in movies)
            {
                if (!string.IsNullOrEmpty(movie.Value) && pictures.TryGetValue(movie.Value, out var imageURL))
                {
                    movie.Key.ImageUrl = imageURL;
                }
            }
        }
    }
}
