using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteHotwifeXXX : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => u.Contains("/updates/") && u.Contains("/tour_hwxxx/"));

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//div[@class='trailerInfo']/h2")?.InnerText.Trim();
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='trailerInfo']/div[@class='released2 trailerStarr']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim().Split(',')[0], out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string curId = Helper.Encode(sceneUrl);
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='trailerInfo']/h2")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='dvdDescription']/p")?.InnerText.Replace("description: ", string.Empty).Trim();
            movie.AddStudio("HotwifeXXX");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='trailerInfo']/div[@class='released2 trailerStarr']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim().Split(',')[0], out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='trailerMInfo']//span[@class='tour_update_models']/a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);

                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='modelBioPic']/img")?.GetAttributeValue("src0_3x", string.Empty);
                    }

                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//span[@id='trailer_thumb']//img");

            if (posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("src", string.Empty), Type = ImageType.Primary });
            }

            return images;
        }
    }
}
