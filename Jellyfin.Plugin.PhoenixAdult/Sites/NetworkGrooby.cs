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
    public class NetworkGrooby : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => u.Contains("/trailers/")).Select(u => u.Split('?')[0]);

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//div[@class='trailer_videoinfo']//h3 | //div[@class='trailer_toptitle_left']")?.InnerText.Trim());
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//div[@class='setdesc']//b[contains(., 'Added')]");
                    if (dateNode != null && DateTime.TryParse(dateNode.NextSibling.InnerText.Split('-').Last().Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

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
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//div[@class='trailer_videoinfo']//h3 | //div[@class='trailer_toptitle_left']")?.InnerText.Trim());
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='trailer_videoinfo']//p | //div[@class='trailerpage_info']/p[not(@class)]")?.InnerText;
            movie.AddStudio("Grooby");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='setdesc']/*[contains(., 'Added')] | //div[@class='trailer_videoinfo']/*[contains(., 'Added')]");
            if (dateNode != null && DateTime.TryParse(dateNode.NextSibling.InnerText.Split('-').Last().Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='trailer_videoinfo']//p[contains(., 'Featuring')]//a | //div[@class='setdesc']//a[contains(@href, '/models/')]");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorUrl = actor.GetAttributeValue("href", string.Empty);
                    if (actorUrl.StartsWith("//"))
                    {
                        actorUrl = "http:" + actorUrl;
                    }
                    else if (!actorUrl.StartsWith("http"))
                    {
                        actorUrl = Helper.GetSearchBaseURL(siteNum) + actorUrl;
                    }

                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='model_photo']//img[@id] | //div[@class='model_photo']/img")?.GetAttributeValue("src0_1x", string.Empty);
                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                        {
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                        }
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

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='trailerpage_photoblock_fullsize']//a | //div[@class='trailerposter']//img | //div[@class='player-thumb']//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("href", string.Empty) ?? img.GetAttributeValue("src0_4x", string.Empty);
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl });
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
