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
                // 从站点首页抓 Algolia apiKey（/en/login 有 429 限流，stash/TPDB 均从首页抓取）
                var http = await HTTP.Request(Helper.GetSearchBaseURL(siteNum) + "/en", cancellationToken).ConfigureAwait(false);
                if (http.IsOK)
                {
                    var regEx = Regex.Match(http.Content, "\"apiKey\":\"(.*?)\"");
                    if (regEx.Groups.Count > 0)
                    {
                        result = regEx.Groups[1].Value;
                    }
                }

                // 站点首页无 Algolia apiKey（如 My Pervy Family 独立模板）→ 用组内子站 0 的 key（Algolia 是共享索引）
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

            // all_movies 索引恒无 pictures，需要按 movie_id 回 scenes 索引取一张场景预览图（见循环后的批量查询）
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
                        // all_movies 的 last_modified 字段恒为 JSON null（ContainsKey 为 true 但值为 null），
                        // 必须回退到 date_created，否则电影结果既没有首播日期、也拿不到日期排序/日期前缀。
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
                        // 电影封面（_front_400x625.jpg）只对部分影片存在，缺失时会变成破图；
                        // 这里留着空，稍后统一按 movie_id 取该片第一个场景的预览图。
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
        /// 构造电影封面前端图 URL（scenes 索引的 pictures 是场景预览图，movies 索引只有这两个字段）。
        /// 形如 https://images-fame.gammacdn.com/movies/165967/165967_cherry_kiss_anal_a2m_squirting_front_400x625.jpg，
        /// 已实测对 EvilAngel/AdultTime 家族的 movies 记录全部可达（200 image/jpeg）。
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
        /// 从 Algolia pictures 对象中选取最高清的图片路径。
        /// pictures 形如 { "nsfw": {...}, "76x55": "...", "1920x1080": "...", "resized": "..." }，
        /// 键为 WxH 分辨率，resized 是低清缩略图，nsfw 是嵌套对象。
        /// 解析所有 WxH 键取宽度最大者，避免写死某个分辨率；无 WxH 键时回退到任意字符串值。
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
        /// all_movies 索引没有 pictures 字段，用一次批量查询回到 all_scenes 索引按 movie_id 取预览图。
        /// 相比电影封面 {id}_{slug}_front_400x625.jpg（大量 404），场景预览图实测稳定可达。
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
