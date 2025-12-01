using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteArchAngel : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string baseUrl = Helper.GetSearchBaseURL(siteNum);
            string initialSearchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle).Replace("%20", "+");

            var pagesToScrape = new HashSet<string> { initialSearchUrl };
            var pagesScraped = new HashSet<string>();

            while (pagesToScrape.Any())
            {
                string currentUrl = pagesToScrape.First();
                pagesToScrape.Remove(currentUrl);

                if (pagesScraped.Contains(currentUrl))
                {
                    continue;
                }

                var httpResult = await HTTP.Request(currentUrl, HttpMethod.Get, cancellationToken: cancellationToken);
                if (!httpResult.IsOK)
                {
                    continue;
                }

                pagesScraped.Add(currentUrl);

                var searchPageElements = HTML.ElementFromString(httpResult.Content);

                var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'latest-updates')]//div[contains(@class, 'flex-col')]");
                if (searchNodes != null)
                {
                    foreach (var node in searchNodes)
                    {
                        var linkNode = node.SelectSingleNode(".//div[contains(@class, 'thumbnail')]/a");
                        var titleNode = node.SelectSingleNode(".//div[contains(@class, 'title')]//a");
                        var imageNode = node.SelectSingleNode(".//div[contains(@class, 'thumbnail')]//img");

                        if (linkNode == null || titleNode == null)
                        {
                            continue;
                        }

                        string sceneUrl = linkNode.GetAttributeValue("href", string.Empty);
                        string title = titleNode.InnerText.Trim();
                        string imageUrl = imageNode?.GetAttributeValue("data-src", string.Empty);

                        if (!string.IsNullOrEmpty(sceneUrl) && !string.IsNullOrEmpty(title))
                        {
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneUrl) } },
                                Name = $"{title} [{Helper.GetSearchSiteName(siteNum)}]",
                                SearchProviderName = Plugin.Instance.Name,
                                ImageUrl = imageUrl,
                            });
                        }
                    }
                }

                var paginationNodes = searchPageElements.SelectNodes("//div[@class='pagination']//a");
                if (paginationNodes != null)
                {
                    foreach (var pageNode in paginationNodes)
                    {
                        string pageUrl = pageNode.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(pageUrl) && !pagesScraped.Contains(pageUrl) && !pageUrl.StartsWith("http"))
                        {
                            pagesToScrape.Add(baseUrl + "/" + pageUrl);
                        }
                    }
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

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.Name = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'trailer_title')]")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='description-text']")?.InnerText.Trim();

            var dateNode = detailsPageElements.SelectSingleNode("//p[preceding-sibling::label[text()='Date Added']]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[contains(@href, '/porn-categories/')]");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().Replace(",", string.Empty));
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//span[@class='update_models']/a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
                }
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

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='relative']/img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src0_3x", string.Empty);
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
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
