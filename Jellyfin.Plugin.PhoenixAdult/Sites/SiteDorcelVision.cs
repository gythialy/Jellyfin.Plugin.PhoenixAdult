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
    public class SiteDorcelVision : IProviderBase
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

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//a[contains(@class, 'movies')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    string titleNoFormatting = node.SelectSingleNode(".//img")?.GetAttributeValue("alt", string.Empty).Trim();
                    string curId = Helper.Encode(node.GetAttributeValue("href", string.Empty));
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string releaseDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = (detailsPageElements.SelectSingleNode("//meta[@name='twitter:description']")?.GetAttributeValue("content", string.Empty).Trim() ??
                              detailsPageElements.SelectSingleNode("//div[@id='summaryList']")?.InnerText.Trim())
                              .Replace("</br>", "\n").Replace("<br>", "\n").Trim();

            string tagline = "Dorcel Vision";
            var studioNode = detailsPageElements.SelectSingleNode("//div[@class='entries']//strong[contains(., 'Studio')]/following-sibling::a");
            if (studioNode != null)
            {
                    tagline = studioNode.InnerText.Trim();
            }

            movie.AddTag(tagline);
            movie.AddStudio(tagline);

            if (!string.IsNullOrEmpty(releaseDate) && DateTime.TryParse(releaseDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else
            {
                var yearNode = detailsPageElements.SelectSingleNode("//div[@class='entries']//strong[contains(., 'Production year')]/following-sibling::text()");
                if (yearNode != null && int.TryParse(yearNode.InnerText.Trim(), out var parsedYear))
                {
                    movie.ProductionYear = parsedYear;
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'casting')]//div[contains(@class, 'slider-xl')]//div[@class='col-xs-2']");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.SelectSingleNode(".//a/strong")?.InnerText.Trim();
                    string actorPhotoUrl = actor.SelectSingleNode(".//img")?.GetAttributeValue("data-src", string.Empty);
                    if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http:"))
                    {
                        actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
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

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'covers')]/a[contains(@class, 'cover')] | //div[contains(@class, 'screenshots')]//div[contains(@class, 'slider-xl')]/div[@class='slides']/div[@class='col-xs-2']/a");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = (img.GetAttributeValue("href", string.Empty)).Replace("blur9/", "/");
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
