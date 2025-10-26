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

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteNewSensations : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null || string.IsNullOrEmpty(searchTitle))
            {
                return result;
            }

            var searchResultsURLs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"{Helper.GetSearchSearchURL(siteNum)}updates/{searchTitle.Replace(" ", "-")}.html",
                $"{Helper.GetSearchSearchURL(siteNum)}updates/{searchTitle.Replace(" ", "-")}-.html",
                $"{Helper.GetSearchSearchURL(siteNum)}updates/{searchTitle.Replace(" ", "-")}-4k.html",
                $"{Helper.GetSearchSearchURL(siteNum)}dvds/{searchTitle.Replace(" ", "-")}.html",
            };

            var googleResults = await Search.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var url in googleResults)
            {
                if ((url.Contains("/updates/") || url.Contains("/dvds/") || url.Contains("/scenes/")) && (url.Contains("/tour_ns/") || url.Contains("/tour_famxxx/")))
                {
                    searchResultsURLs.Add(url);
                }
            }

            foreach (var sceneURL in searchResultsURLs)
            {
                var req = await HTTP.Request(sceneURL, cancellationToken);
                if (req.IsOK)
                {
                    var searchResult = HTML.ElementFromString(req.Content);
                    var titleNode = searchResult.SelectSingleNode("(//div[@class='indScene']/h1 | //div[@class='indSceneDVD']/h1) | (//div[@class='indScene']/h2 | //div[@class='indSceneDVD']/h2)");
                    if (titleNode != null)
                    {
                        string titleNoFormatting = titleNode.InnerText.Trim();
                        string curID = Helper.Encode(sceneURL);
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                            Name = $"{titleNoFormatting} [New Sensations]",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>() { Item = new Movie(), People = new List<PersonInfo>() };
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
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
            movie.AddStudio("New Sensations");

            bool isDvd = sceneURL.Contains("dvds");
            if (isDvd)
            {
                movie.Name = detailsPageElements.SelectSingleNode("//div[@class='indSceneDVD']/h1")?.InnerText.Trim();
                movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='description']/h2")?.InnerText.Replace("Description:", string.Empty).Trim();
                movie.AddTag(movie.Name); // DVD Name as collection

                var dateNode = detailsPageElements.SelectSingleNode("//div[@class='datePhotos']")?.InnerText.Replace("RELEASED:", string.Empty).Trim();
                if (!string.IsNullOrEmpty(dateNode))
                {
                    if (DateTime.TryParse(dateNode, out var parsedDate))
                    {
                        movie.PremiereDate = parsedDate;
                        movie.ProductionYear = parsedDate.Year;
                    }
                }

                var genreNodes = detailsPageElements.SelectNodes("//div[@class='textLink']//a");
                if (genreNodes != null)
                {
                    foreach (var genre in genreNodes)
                    {
                        movie.AddGenre(genre.InnerText.Trim());
                    }
                }
            }
            else // Is a Scene
            {
                movie.Name = detailsPageElements.SelectSingleNode("//div[@class='indScene']/h1 | //div[@class='indScene']/h2")?.InnerText.Trim();
                movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='description']/h2/text() | //div[@class='description']//span/following-sibling::text()")?.InnerText.Replace("Description:", string.Empty).Trim();
                movie.AddTag(Helper.GetSearchSiteName(siteNum));

                var dateNode = detailsPageElements.SelectSingleNode("//div[@class='sceneDateP']/span")?.InnerText.Split(',')[0].Trim();
                if (DateTime.TryParse(dateNode, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//span[@class='tour_update_models']/a");
            if (actorNodes != null)
            {
                 if (actorNodes.Count == 3 && !isDvd)
                {
                    movie.AddGenre("Threesome");
                }

                 if (actorNodes.Count == 4 && !isDvd)
                {
                    movie.AddGenre("Foursome");
                }

                 if (actorNodes.Count > 4 && !isDvd)
                {
                    movie.AddGenre("Orgy");
                }

                 foreach (var actorLink in actorNodes)
                {
                    string actorName = actorLink.InnerText.Trim();
                    string actorPageURL = actorLink.GetAttributeValue("href", string.Empty);
                    var actorPage = await HTML.ElementFromURL(actorPageURL, cancellationToken);
                    string actorPhotoURL = actorPage?.SelectSingleNode("//div[@class='modelBioPic']/img")?.GetAttributeValue("src0_3x", string.Empty);
                    result.People.Add(new PersonInfo { Name = actorName, ImageUrl = actorPhotoURL, Type = PersonKind.Actor });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var result = new List<RemoteImageInfo>();
            string sceneURL = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (detailsPageElements == null)
            {
                return result;
            }

            var posterNode = detailsPageElements.SelectSingleNode("//span[@id='trailer_thumb']//img");
            if (posterNode != null)
            {
                result.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("src", string.Empty), Type = ImageType.Primary });
            }

            if (sceneURL.Contains("dvds"))
            {
                var imageNodes = detailsPageElements.SelectNodes("//div[@class='videoBlock']//img/@src0_3x");
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        result.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src0_3x", string.Empty), Type = ImageType.Backdrop });
                    }
                }
            }

            return result;
        }
    }
}
