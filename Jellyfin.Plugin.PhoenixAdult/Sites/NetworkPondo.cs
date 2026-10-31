using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Newtonsoft.Json.Linq;
using PhoenixAdult.Configuration;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    /// <summary>
    /// Shared implementation for the 10musume family: 0=10musume, 1=1Pondo, 2=MuraMura, 3=PacoPacomama.
    /// The scene id is "date_sequence" (042922_01); there is no usable site search, so the id comes
    /// from the file name and is used to fetch the official JSON directly.
    /// {base}/dyn/phpauto/movie_details/movie_id/{id}.json。
    /// Galleries: new {base}/dyn/dla/json/movie_gallery/{id}.json,
    /// old {base}/dyn/phpauto/movie_galleries/movie_id/{id}.json.
    /// </summary>
    public class NetworkPondo : IProviderBase
    {
        // Date style code: 042922_01, 071319-870 or 042922 01.
        private static readonly Regex MovieIdRegex = new Regex(
            @"(?<![0-9])([0-9]{6})[_\- ]([0-9]{2,3})(?![0-9])",
            RegexOptions.CultureInvariant);

        // 1pondo occasionally returns a hostless thumbnail URL (https:///moviepages/...).
        private static readonly Regex BrokenSchemeRegex = new Regex(
            @"^https?:///",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Resolution and frame rate are not genres; drop them from UCNAME.
        private static readonly Regex SpecGenreRegex = new Regex(
            @"^(?:[0-9]{3,4}p|[0-9]{2,4}fps)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd",
            "yyyy/M/d",
            "yyyy-M-d",
        };

        private static readonly PondoSite[] SiteConfigs =
        {
            // 0 = 10musume
            new PondoSite
            {
                Maker = "天然むすめ",
                GalleryPath = "/dyn/dla/images/{0}",
                LegacyGalleryPath = "/assets/sample/{0}/{1}",
            },

            // 1 = 1Pondo
            new PondoSite
            {
                Maker = "一本道",
                GalleryPath = "/dyn/dla/images/{0}",
                LegacyGalleryPath = "/assets/sample/{0}/popu/{1}",
            },

            // 2 = MuraMura, which has no gallery API.
            new PondoSite
            {
                Maker = "ムラムラってくる素人",
                GalleryPath = null,
                LegacyGalleryPath = null,
            },

            // 3 = PacoPacomama
            new PondoSite
            {
                Maker = "パコパコママ",
                GalleryPath = "/dyn/dla/images/{0}",
                LegacyGalleryPath = "/assets/sample/{0}/l/{1}",
            },
        };

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || siteNum.Length < 2 || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            try
            {
                var baseUrl = GetBaseUrl(siteNum);
                var movieID = ExtractMovieID(searchTitle);
                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(movieID))
                {
                    return result;
                }

                var scene = await GetScene(baseUrl, movieID, cancellationToken).ConfigureAwait(false);
                if (scene == null || string.IsNullOrEmpty(scene.Title))
                {
                    return result;
                }

                var res = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(GetScenePath(movieID)) } },
                    Name = scene.Title,
                    ImageUrl = scene.CoverUrl,
                };

                if (scene.ReleaseDate.HasValue)
                {
                    res.PremiereDate = scene.ReleaseDate;
                }
                else if (searchDate.HasValue)
                {
                    res.PremiereDate = searchDate;
                }

                result.Add(res);
            }
            catch (Exception e)
            {
                Logger.Error($"NetworkPondo search failed ({searchTitle}): {e.Message}");
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

            if (siteNum == null || siteNum.Length < 2 || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var baseUrl = GetBaseUrl(siteNum);
            var movieID = ExtractMovieID(Helper.Decode(sceneID[0]));
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(movieID))
            {
                return result;
            }

            var scene = await GetScene(baseUrl, movieID, cancellationToken).ConfigureAwait(false);
            if (scene == null || string.IsNullOrEmpty(scene.Title))
            {
                return result;
            }

            result.Item.Name = scene.Title;
            result.Item.Overview = scene.Summary;
            result.Item.OriginalTitle = movieID;
            result.Item.ExternalId = scene.Homepage;
            result.Item.SetProviderId(Plugin.Instance.Name, Helper.Encode(GetScenePath(movieID)));

            var site = GetSiteConfig(siteNum);
            if (!string.IsNullOrEmpty(site?.Maker))
            {
                result.Item.AddStudio(site.Maker);
            }

            if (scene.ReleaseDate.HasValue)
            {
                result.Item.PremiereDate = scene.ReleaseDate;
                result.Item.ProductionYear = scene.ReleaseDate.Value.Year;
            }

            if (scene.Runtime > 0)
            {
                result.Item.RunTimeTicks = TimeSpan.FromMinutes(scene.Runtime).Ticks;
            }

            foreach (var genre in scene.Genres)
            {
                result.Item.AddGenre(genre);
            }

            foreach (var actorName in scene.Actors)
            {
                var name = actorName;
                switch (Plugin.Instance.Configuration.JAVActorNamingStyle)
                {
                    case JAVActorNamingStyle.JapaneseStyle:
                        name = string.Join(" ", name.Split().Reverse());

                        break;
                }

                result.AddPerson(new PersonInfo { Name = name });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            if (siteNum == null || siteNum.Length < 2 || sceneID == null || sceneID.Length == 0)
            {
                return result;
            }

            var baseUrl = GetBaseUrl(siteNum);
            var movieID = ExtractMovieID(Helper.Decode(sceneID[0]));
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(movieID))
            {
                return result;
            }

            var scene = await GetScene(baseUrl, movieID, cancellationToken).ConfigureAwait(false);

            var cover = scene?.CoverUrl;
            if (string.IsNullOrEmpty(cover))
            {
                // Even with a broken detail JSON, return the deterministic cover.
                cover = FixUrl(baseUrl, $"/moviepages/{movieID}/images/str.jpg");
            }

            result.Add(new RemoteImageInfo
            {
                Type = ImageType.Primary,
                Url = cover,
            });

            if (!string.IsNullOrEmpty(scene?.ThumbUrl) && !string.Equals(scene.ThumbUrl, cover, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new RemoteImageInfo
                {
                    Type = ImageType.Primary,
                    Url = scene.ThumbUrl,
                });
            }

            if (scene == null)
            {
                return result;
            }

            var site = GetSiteConfig(siteNum);
            if (site == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cover };

            // New gallery: Rows[].Img.
            if (scene.HasDlaGallery && !string.IsNullOrEmpty(site.GalleryPath))
            {
                var gallery = await GetJson($"{baseUrl}/dyn/dla/json/movie_gallery/{movieID}.json", cancellationToken).ConfigureAwait(false);
                foreach (var row in gallery?["Rows"] as JArray ?? new JArray())
                {
                    if (GetBoolValue(row, "Protected"))
                    {
                        continue;
                    }

                    var img = GetStringValue(row, "Img");
                    if (string.IsNullOrEmpty(img))
                    {
                        continue;
                    }

                    AddImage(result, seen, FixUrl(baseUrl, string.Format(CultureInfo.InvariantCulture, site.GalleryPath, img)));
                }
            }

            // Old gallery: Rows[].MovieID plus Rows[].Filename.
            if (!scene.HasDlaGallery && scene.HasLegacyGallery && !string.IsNullOrEmpty(site.LegacyGalleryPath))
            {
                var gallery = await GetJson($"{baseUrl}/dyn/phpauto/movie_galleries/movie_id/{movieID}.json", cancellationToken).ConfigureAwait(false);
                foreach (var row in gallery?["Rows"] as JArray ?? new JArray())
                {
                    if (GetBoolValue(row, "Protected"))
                    {
                        continue;
                    }

                    var rowID = GetStringValue(row, "MovieID");
                    var filename = GetStringValue(row, "Filename");
                    if (string.IsNullOrEmpty(rowID) || string.IsNullOrEmpty(filename))
                    {
                        continue;
                    }

                    AddImage(result, seen, FixUrl(baseUrl, string.Format(CultureInfo.InvariantCulture, site.LegacyGalleryPath, rowID, filename)));
                }
            }

            return result;
        }

        private static void AddImage(List<RemoteImageInfo> result, HashSet<string> seen, string url)
        {
            if (string.IsNullOrEmpty(url) || !seen.Add(url))
            {
                return;
            }

            result.Add(new RemoteImageInfo
            {
                Type = ImageType.Backdrop,
                Url = url,
            });
        }

        private static async Task<PondoScene> GetScene(string baseUrl, string movieID, CancellationToken cancellationToken)
        {
            var data = await GetJson($"{baseUrl}/dyn/phpauto/movie_details/movie_id/{movieID}.json", cancellationToken).ConfigureAwait(false);
            if (data == null)
            {
                return null;
            }

            var id = GetStringValue(data, "MovieID");
            var title = GetStringValue(data, "Title");
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            if (string.IsNullOrEmpty(id))
            {
                id = movieID;
            }

            var scene = new PondoScene
            {
                ID = id,
                Homepage = $"{baseUrl}/movies/{id}/",
                Title = title,
                Summary = GetStringValue(data, "Desc"),
                Series = GetStringValue(data, "Series"),
                ReleaseDate = ParseDate(GetStringValue(data, "Release")),
                CoverUrl = GetCoverUrl(baseUrl, data),
                ThumbUrl = FixUrl(baseUrl, GetStringValue(data, "MovieThumb")),
                HasDlaGallery = GetBoolValue(data, "Gallery"),
                HasLegacyGallery = GetBoolValue(data, "HasGallery"),
            };

            var duration = GetIntValue(data, "Duration");
            if (duration > 0)
            {
                scene.Runtime = (int)Math.Round(duration / 60.0, MidpointRounding.AwayFromZero);
            }

            foreach (var genre in GetStringArray(data, "UCNAME"))
            {
                if (!SpecGenreRegex.IsMatch(genre))
                {
                    scene.Genres.Add(genre);
                }
            }

            var actors = GetStringArray(data, "ActressesJa");
            if (actors.Count == 0)
            {
                actors = GetStringArray(data, "ActressesEn");
            }

            foreach (var actor in actors)
            {
                // The official JSON occasionally contains "-" placeholders.
                var name = actor.Trim('-', ' ', '　');
                if (!string.IsNullOrEmpty(name))
                {
                    scene.Actors.Add(name);
                }
            }

            return scene;
        }

        // Fetcher can throw on error pages; absorb it so Search and Update never crash.
        private static async Task<JObject> GetJson(string url, CancellationToken cancellationToken)
        {
            try
            {
                return await Fetcher.Json(url, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Debug($"NetworkPondo fetch failed ({url}): {e.Message}");
                return null;
            }
        }

        private static string GetCoverUrl(string baseUrl, JObject data)
        {
            // The Go version picks the first non-empty of Ultra, High, Med, Low.
            foreach (var key in new[] { "ThumbUltra", "ThumbHigh", "ThumbMed", "ThumbLow" })
            {
                var url = FixUrl(baseUrl, GetStringValue(data, key));
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }
            }

            return string.Empty;
        }

        private static string GetBaseUrl(int[] siteNum)
            => Helper.GetSearchBaseURL(siteNum)?.TrimEnd('/') ?? string.Empty;

        private static PondoSite GetSiteConfig(int[] siteNum)
        {
            var index = siteNum[1];
            if (index < 0 || index >= SiteConfigs.Length)
            {
                return SiteConfigs[0];
            }

            return SiteConfigs[index];
        }

        private static string GetScenePath(string movieID)
            => $"/movies/{movieID}/";

        private static string ExtractMovieID(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var match = MovieIdRegex.Match(text);
            return match.Success ? $"{match.Groups[1].Value}_{match.Groups[2].Value}" : string.Empty;
        }

        private static string FixUrl(string baseUrl, string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            url = url.Trim();

            if (BrokenSchemeRegex.IsMatch(url))
            {
                // https:///moviepages/x/images/str.jpg -> {base}/moviepages/x/images/str.jpg
                return baseUrl + "/" + url.Substring(url.IndexOf("///", StringComparison.Ordinal) + 3);
            }

            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                return "https:" + url;
            }

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return baseUrl + (url.StartsWith("/", StringComparison.Ordinal) ? url : "/" + url);
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ? date : null;
        }

        private static string GetStringValue(JToken data, string key)
        {
            var token = data?[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            return token.Type == JTokenType.String ? (string)token : token.ToString();
        }

        private static int GetIntValue(JToken data, string key)
        {
            var token = data?[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            if (token.Type == JTokenType.Integer)
            {
                return (int)token;
            }

            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static bool GetBoolValue(JToken data, string key)
        {
            var token = data?[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return (bool)token;
            }

            return bool.TryParse(token.ToString(), out var value) && value;
        }

        private static List<string> GetStringArray(JToken data, string key)
        {
            var result = new List<string>();
            if (data?[key] is JArray array)
            {
                foreach (var item in array)
                {
                    var value = item?.ToString().Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        result.Add(value);
                    }
                }
            }

            return result;
        }

        private sealed class PondoSite
        {
            public string Maker { get; set; }

            public string GalleryPath { get; set; }

            public string LegacyGalleryPath { get; set; }
        }

        private sealed class PondoScene
        {
            public string ID { get; set; }

            public string Homepage { get; set; }

            public string Title { get; set; }

            public string Summary { get; set; }

            public string Series { get; set; }

            public DateTime? ReleaseDate { get; set; }

            public int Runtime { get; set; }

            public string CoverUrl { get; set; }

            public string ThumbUrl { get; set; }

            public bool HasDlaGallery { get; set; }

            public bool HasLegacyGallery { get; set; }

            public List<string> Actors { get; } = new List<string>();

            public List<string> Genres { get; } = new List<string>();
        }
    }
}
