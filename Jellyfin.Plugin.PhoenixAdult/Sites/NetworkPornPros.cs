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
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class NetworkPornPros : IProviderBase
    {
        private static readonly Dictionary<string, string[]> GenresDB = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Anal4K", new[] { "Anal", "Ass", "Creampie" } }, { "BBCPie", new[] { "Interracial", "BBC", "Creampie" } },
            { "Cum4K", new[] { "Creampie" } }, { "DeepThroatLove", new[] { "Blowjob", "Deep Throat" } },
            { "GirlCum", new[] { "Orgasms", "Girl Orgasm", "Multiple Orgasms" } }, { "Holed", new[] { "Anal", "Ass" } },
            { "Lubed", new[] { "Lube", "Raw", "Wet" } }, { "MassageCreep", new[] { "Massage", "Oil" } },
            { "PassionHD", new[] { "Hardcore" } }, { "POVD", new[] { "Gonzo", "POV" } },
            { "PureMature", new[] { "MILF", "Mature" } },
        };
        private static readonly Dictionary<string, string[]> ActorsDB = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Poke Her In The Front", new[] { "Sara Luvv", "Dillion Harper" } },
            { "Best Friends With Nice Tits!", new[] { "April O'Neil", "Victoria Rae Black" } },
        };

        private async Task<JObject> GetDataFromAPI(int[] siteNum, string searchType, string slug, CancellationToken cancellationToken)
        {
            string site = Helper.GetSearchBaseURL(siteNum);
            string searchSite = Helper.GetSearchSearchURL(siteNum);

            var headers = new Dictionary<string, string> { { "x-site", site } };
            string url = $"{searchSite}/{searchType}/{slug}";

            var req = await HTTP.Request(url, cancellationToken, headers: headers);
            if (!req.IsOK)
            {
                string subSite = Helper.GetSearchSiteName(siteNum).Replace(" ", string.Empty).ToLower();
                if (!searchSite.Contains(subSite) && site.Contains("pornplus"))
                {
                    string newSite = site.Replace("pornplus", subSite);
                    string newSearchSite = searchSite.Replace("pornplus", subSite);
                    headers["x-site"] = newSite;
                    url = $"{newSearchSite}/{searchType}/{slug}";
                    req = await HTTP.Request(url, cancellationToken, headers: headers);
                }
            }

            if (!req.IsOK && slug.Contains('-') && !slug.Contains("--"))
            {
                int lastIndex = slug.LastIndexOf('-');
                string newSlug = slug.Remove(lastIndex, 1).Insert(lastIndex, "--");
                return await GetDataFromAPI(siteNum, searchType, newSlug, cancellationToken);
            }

            return req.IsOK ? JObject.Parse(req.Content) : null;
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();

            string slug = searchTitle.Slugify();

            var searchResult = await GetDataFromAPI(siteNum, "releases", slug, cancellationToken);
            if (searchResult == null)
            {
                return result;
            }

            string titleNoFormatting = Helper.ParseTitle(searchResult["title"]?.ToString() ?? string.Empty, siteNum[0]);
            string subSite = searchResult["sponsor"]?["name"]?.ToString() ?? string.Empty;
            string curID = Helper.Encode(slug);

            string releaseDate = string.Empty;
            if (DateTime.TryParse(searchResult["releasedAt"]?.ToString(), out var parsedDate))
            {
                releaseDate = parsedDate.ToString("yyyy-MM-dd");
            }

            result.Add(new RemoteSearchResult
            {
                ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}|{releaseDate}" } },
                Name = $"{titleNoFormatting} [{subSite}] {releaseDate}",
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
            string slug = Helper.Decode(providerIds[0]);
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : string.Empty;

            var detailsPageElements = await GetDataFromAPI(siteNum, "releases", slug, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;

            movie.Name = Helper.ParseTitle(detailsPageElements["title"]?.ToString() ?? string.Empty, siteNum[0]);

            string summary = detailsPageElements["description"]?.ToString().Trim();
            if (!string.IsNullOrEmpty(summary) && !summary.Equals("n/a", StringComparison.OrdinalIgnoreCase))
            {
                movie.Overview = summary;
            }

            movie.AddStudio("PornPros");

            string tagline = detailsPageElements["sponsor"]?["name"]?.ToString() ?? string.Empty;
            movie.AddTag(tagline);
            movie.AddCollection(tagline);

            if (DateTime.TryParse(detailsPageElements["releasedAt"]?.ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (DateTime.TryParse(sceneDate, out var parsedSceneDate))
            {
                 movie.PremiereDate = parsedSceneDate;
                 movie.ProductionYear = parsedSceneDate.Year;
            }

            var junkTags = new List<string> { tagline.Replace(" ", string.Empty).ToLower() };
            var actors = detailsPageElements["actors"] as JArray;
            if (actors != null)
            {
                foreach (var actor in actors)
                {
                    junkTags.Add(actor["name"].ToString().Replace(" ", string.Empty).ToLower());
                }
            }

            var genres = detailsPageElements["tags"] as JArray;
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    movie.AddGenre(genre.ToString());
                }
            }

            if (GenresDB.TryGetValue(Helper.GetSearchSiteName(siteNum).Replace(" ", string.Empty), out var dbGenres))
            {
                foreach (var genre in dbGenres)
                {
                    movie.AddGenre(genre);
                }
            }

            if (actors != null)
            {
                foreach (var actor in actors)
                {
                    string actorName = actor["name"].ToString();
                    string actorPhotoURL = string.Empty;

                    var modelPageElements = await GetDataFromAPI(siteNum, "actors", actor["cached_slug"].ToString(), cancellationToken);
                    if (modelPageElements != null)
                    {
                        actorPhotoURL = modelPageElements["thumbUrl"]?.ToString().Split('?')[0] ?? string.Empty;
                    }

                    foreach (var name in actorName.Split('&'))
                    {
                        result.People.Add(new PersonInfo { Name = name.Trim(), Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                    }
                }
            }

            if (ActorsDB.ContainsKey(movie.Name))
            {
                foreach (var actor in ActorsDB[movie.Name])
                {
                    result.People.Add(new PersonInfo { Name = actor, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();

            string slug = Helper.Decode(sceneID[0].Split('|')[0]);
            var detailsPageElements = await GetDataFromAPI(siteNum, "releases", slug, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            string posterUrl = detailsPageElements["posterUrl"]?.ToString().Split('?')[0];
            if (!string.IsNullOrEmpty(posterUrl))
            {
                result.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
            }

            var thumbUrls = detailsPageElements["thumbUrls"] as JArray;
            string thumbUrl = thumbUrls?.FirstOrDefault()?.ToString().Split('?')[0] ?? detailsPageElements["thumbUrl"]?.ToString().Split('?')[0];

            if (!string.IsNullOrEmpty(thumbUrl))
            {
                if (thumbUrl.Contains("handtouched"))
                {
                    string baseUrl = thumbUrl.Substring(0, thumbUrl.LastIndexOf('/'));
                    for (int i = 1; i < 20; i++)
                    {
                        result.Add(new RemoteImageInfo { Url = $"{baseUrl}/{i:D3}.jpg", Type = ImageType.Backdrop });
                    }
                }
                else if (thumbUrls != null)
                {
                    foreach (var tUrl in thumbUrls)
                    {
                        result.Add(new RemoteImageInfo { Url = tUrl.ToString().Split('?')[0], Type = ImageType.Backdrop });
                    }
                }
                else
                {
                    result.Add(new RemoteImageInfo { Url = thumbUrl, Type = ImageType.Backdrop });
                }
            }

            return result;
        }
    }
}
