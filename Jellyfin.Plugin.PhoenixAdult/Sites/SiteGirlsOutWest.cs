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
    public class SiteGirlsOutWest : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string directUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.ToLower().Replace(' ', '-')}.html";
            var searchResults = new List<string> { directUrl };

            var googleResults = await GoogleSearch.Search(searchTitle, siteNum, cancellationToken);
            searchResults.AddRange(googleResults.Where(u => u.Contains("/trailers/")));

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK && !httpResult.Content.Contains("Page not found"))
                {
                    var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", "").Trim();
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='trailer topSpace']/div[2]/p");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('\\')[1].Trim(), out var parsedDate))
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [GirlsOutWest] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name
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
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//meta[@name='twitter:title']")?.GetAttributeValue("content", "").Trim();
            movie.AddStudio("GirlsOutWest");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='trailer topSpace']/div[2]/p");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('\\')[1].Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("Amateur");
            movie.AddGenre("Australian");

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='trailer topSpace']/div[2]/p/a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3) movie.AddGenre("Threesome");
                if (actorNodes.Count == 4) movie.AddGenre("Foursome");
                if (actorNodes.Count > 4) movie.AddGenre("Orgy");

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", "");
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = await HTML.ElementFromString(actorHttp.Content, cancellationToken);
                        actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPage.SelectSingleNode("//div[@class='profilePic']/img")?.GetAttributeValue("src0_3x", "");
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
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='videoplayer']/img");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                    images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + img.GetAttributeValue("src0_3x", "") });
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
