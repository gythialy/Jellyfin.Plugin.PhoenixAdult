using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class SitePorndoePremium : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            var url = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var data = await HTML.ElementFromURL(url, cancellationToken);
            if (data == null)
            {
                return result;
            }

            var searchResults = data.SelectNodes("//div[contains(@class, 'main-content')]//div[@class='-g-vc-grid']");
            if (searchResults == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults)
            {
                var titleNode = searchResult.SelectSingleNode("./div[@class='-g-vc-item-title']//a");
                string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty);
                string subSite = searchResult.SelectSingleNode("./div[@class='-g-vc-item-channel']//a")?.GetAttributeValue("title", string.Empty);
                string sceneURL = titleNode?.GetAttributeValue("href", string.Empty);
                string curID = Helper.Encode(sceneURL);
                string date = searchResult.SelectSingleNode("./div[@class='-g-vc-item-date']")?.InnerText.Trim();
                DateTime.TryParse(date, out var releaseDate);

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, curID } },
                    Name = $"{titleNoFormatting} [LetsDoeIt/{subSite}] {releaseDate:yyyy-MM-dd}",
                    SearchProviderName = Plugin.Instance.Name,
                });
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

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1[@class='-mvd-heading']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='-mvd-description']")?.InnerText.Trim();
            movie.AddStudio("Porndoe Premium");

            var tagline = detailsPageElements.SelectSingleNode("//div[@class='-mvd-grid-actors']/span/a")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(tagline))
            {
                movie.AddStudio(tagline);
            }

            var genreNodes = detailsPageElements.SelectNodes("//span[@class='-mvd-list-item']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='-mvd-grid-stats']")?.InnerText.Trim();
            if (!string.IsNullOrEmpty(dateNode))
            {
                var date = dateNode.Split('•').Last().Trim();
                if (DateTime.TryParse(date, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='-mvd-grid-actors']/span/a[@title]");
            if (actorNodes != null)
            {
                foreach (var actorLink in actorNodes)
                {
                    string actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                    var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                    if (actorPage != null)
                    {
                        string actorName = actorPage.SelectSingleNode("//div[@class='-aph-heading']//h1")?.InnerText.Trim();
                        string actorPhotoURL = actorPage.SelectSingleNode("//div[@class='-api-poster-item']//img")?.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(actorPhotoURL) && !actorPhotoURL.StartsWith("http"))
                        {
                            actorPhotoURL = Helper.GetSearchBaseURL(siteNum) + actorPhotoURL;
                        }

                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
                    }
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var xpaths = new[] { "//picture[@class='-vcc-picture']//img/@src", "//div[@class='swiper-wrapper']/div/a/div/@data-bg" };
            foreach (var xpath in xpaths)
            {
                var posterNodes = detailsPageElements.SelectNodes(xpath);
                if (posterNodes != null)
                {
                    foreach (var poster in posterNodes)
                    {
                        result.Add(new RemoteImageInfo { Url = poster.GetAttributeValue(xpath.Split('@').Last(), string.Empty) });
                    }
                }
            }

            if (result.Any())
            {
                result.First().Type = ImageType.Primary;
                foreach (var image in result.Skip(1))
                {
                    image.Type = ImageType.Backdrop;
                }
            }

            return result;
        }
    }
}
