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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkPervCity : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => u.Contains("trailers") && !u.Contains("as3")).Select(u => u.Replace("www.", string.Empty));

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
                    string subSite = detailsPageElements.SelectSingleNode("//div[@class='about']//h3")?.InnerText.Replace("About", string.Empty).Trim();
                    string curId = Helper.Encode(sceneUrl);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"{titleNoFormatting} [PervCity/{subSite}]",
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
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='infoBox clear']/p")?.InnerText.Trim();
            movie.AddStudio("PervCity");

            string tagline = detailsPageElements.SelectSingleNode("//div[@class='about']//h3")?.InnerText.Replace("About", string.Empty).Trim();
            movie.AddStudio(tagline);

            var actorNodes = detailsPageElements.SelectNodes("//h3/span/a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string modelUrl = actor.GetAttributeValue("href", string.Empty).Replace(Helper.GetSearchBaseURL(siteNum).Replace("www.", string.Empty), Helper.GetSearchBaseURL(new[] { 1165, 0 }).Replace("www.", string.Empty));
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='starPic']/img")?.GetAttributeValue("src", string.Empty);

                        var sceneNodes = actorPage.SelectNodes("//div[@class='videoBlock']");
                        if (sceneNodes != null)
                        {
                            var sceneNode = sceneNodes.FirstOrDefault(s => (s.SelectSingleNode(".//h3")?.InnerText.Replace("...", string.Empty).Trim().ToLower() ?? string.Empty).Contains(movie.Name.ToLower()));
                            if (sceneNode != null)
                            {
                                var dateNode = sceneNode.SelectSingleNode(".//div[@class='date']");
                                if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
                                {
                                    movie.PremiereDate = parsedDate;
                                    movie.ProductionYear = parsedDate.Year;
                                }
                            }
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='snap']");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src0_3x", string.Empty);
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
