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
    public class NetworkMetArt : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}/search-results?query[contentType]=movies&searchPhrase={Uri.EscapeDataString(searchTitle)}";
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchResults = JObject.Parse(httpResult.Content);
            if (searchResults["items"] != null)
            {
                foreach (var searchResult in searchResults["items"])
                {
                    string subSite = Helper.GetSearchSiteName(siteNum);
                    string titleNoFormatting = searchResult["item"]["name"].ToString();
                    var sceneUrlParts = searchResult["item"]["path"].ToString().Split('/').Skip(1).ToArray();
                    string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}/movie?name={sceneUrlParts[4]}&date={sceneUrlParts[3]}";
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    if (DateTime.TryParse(searchResult["item"]["publishedAt"].ToString(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [MetArt/{subSite}] {releaseDate}",
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

            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = JObject.Parse(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements["name"].ToString();
            movie.Overview = detailsPageElements["description"].ToString();
            movie.AddStudio("MetArt");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            if (DateTime.TryParse(detailsPageElements["publishedAt"].ToString(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genre in detailsPageElements["tags"])
            {
                movie.AddGenre(genre.ToString().Capitalize());
            }

            movie.AddGenre("Glamorous");

            foreach (var actor in detailsPageElements["models"])
            {
                string actorName = actor["name"].ToString();
                string actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actor["headshotImagePath"];
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            foreach (var director in detailsPageElements["photographers"])
            {
                ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = director["name"].ToString(), Type = PersonKind.Director });
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = JObject.Parse(httpResult.Content);

            string siteUUID = detailsPageElements["siteUUID"].ToString();
            string cdnUrl = $"https://cdn.metartnetwork.com/{siteUUID}";
            images.Add(new RemoteImageInfo { Url = cdnUrl + detailsPageElements["coverImagePath"], Type = ImageType.Primary });
            images.Add(new RemoteImageInfo { Url = cdnUrl + detailsPageElements["splashImagePath"] });

            return images;
        }
    }
}
