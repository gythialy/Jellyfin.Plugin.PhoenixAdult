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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteFemjoy : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = JObject.Parse(httpResult.Content);
            if (searchResults["results"] != null)
            {
                string curId = Helper.Encode(searchUrl);
                foreach (var searchResult in searchResults["results"])
                {
                    string titleNoFormatting = searchResult["title"].ToString();
                    string sceneId = searchResult["id"].ToString();
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(searchResult["release_date"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var actors = searchResult["actors"].Select(a => a["name"].ToString());
                    string actorsString = string.Join(", ", actors);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{sceneId}" } },
                        Name = $"{titleNoFormatting} - {actorsString} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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

            string[] providerIds = sceneID[0].Split('|');
            string searchUrl = Helper.Decode(providerIds[0]);
            string sceneId = providerIds[2];

            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = JObject.Parse(httpResult.Content);
            if (searchResults["results"] == null)
            {
                return result;
            }

            var detailsPageElements = searchResults["results"].FirstOrDefault(r => r["id"].ToString() == sceneId);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements["title"].ToString();
            movie.Overview = Regex.Replace(detailsPageElements["long_description"].ToString(), @"<.*?>", string.Empty).Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            if (DateTime.TryParse(detailsPageElements["release_date"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            if (detailsPageElements["actors"] != null)
            {
                var actors = detailsPageElements["actors"];
                if (actors.Count() == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actors.Count() == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actors.Count() > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actors)
                {
                    string actorName = actor["name"].ToString();
                    string actorPhotoUrl = actor["thumb"]["image"].ToString();
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            if (detailsPageElements["directors"] != null)
            {
                foreach (var director in detailsPageElements["directors"])
                {
                    result.People.Add(new PersonInfo { Name = director["name"].ToString(), Type = PersonKind.Director });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string[] providerIds = sceneID[0].Split('|');
            string searchUrl = Helper.Decode(providerIds[0]);
            string sceneId = providerIds[2];

            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var searchResults = JObject.Parse(httpResult.Content);
            if (searchResults["results"] == null)
            {
                return images;
            }

            var detailsPageElements = searchResults["results"].FirstOrDefault(r => r["id"].ToString() == sceneId);
            if (detailsPageElements != null)
            {
                var imageUrl = detailsPageElements.SelectToken("thumb.image")?.ToString();
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
