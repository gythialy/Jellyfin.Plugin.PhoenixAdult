using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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
    public class NetworkAdultEmpire : IProviderBase
    {
        private IDictionary<string, string> GetCookies()
        {
            return new Dictionary<string, string> { { "ageConfirmed", "true" } };
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (siteNum == null)
            {
                return result;
            }

            string sceneID = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneID = parts[0];
                searchTitle = searchTitle.Substring(sceneID.Length).Trim();
            }

            if (!string.IsNullOrEmpty(sceneID))
            {
                string directURL = $"{Helper.GetSearchBaseURL(siteNum)}/{sceneID}/{searchTitle.Slugify()}.html";
                var directHtml = await HTML.ElementFromURL(directURL, cancellationToken, null, GetCookies());
                if (directHtml != null)
                {
                    var titleNode = directHtml.SelectSingleNode("//h1[@class='description']");
                    if (titleNode != null)
                    {
                        string titleNoFormatting = titleNode.InnerText.Trim();
                        string curID = Helper.Encode(directURL);
                        string releaseDate = string.Empty;
                        var dateNode = directHtml.SelectSingleNode("//div[@class='release-date']");
                        if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }
                        else if (searchDate.HasValue)
                        {
                            releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                        }

                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curID } },
                            Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
                    }
                }
            }

            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var searchHtml = await HTML.ElementFromURL(searchUrl, cancellationToken, null, GetCookies());
            if (searchHtml == null)
            {
                return result;
            }

            var searchResults = searchHtml.SelectNodes("//div[contains(@class, 'item-grid')]/div[@class='grid-item']");
            if (searchResults == null)
            {
                return result;
            }

            foreach (var searchResult in searchResults)
            {
                try
                {
                    string titleNoFormatting;
                    string curID;

                    int currentSiteNum = siteNum[0];
                    if (currentSiteNum == 815 || currentSiteNum == 1337 || currentSiteNum == 1776 || currentSiteNum == 1800)
                    {
                        titleNoFormatting = searchResult.SelectSingleNode(".//img[contains(@class, 'img-full-fluid')]").GetAttributeValue("title", string.Empty).Trim();
                        curID = Helper.Encode(searchResult.SelectSingleNode(".//article[contains(@class, 'scene-update')]/a").GetAttributeValue("href", string.Empty));
                    }
                    else if (currentSiteNum == 1766 || currentSiteNum == 1779 || currentSiteNum == 1790 || currentSiteNum == 1792)
                    {
                        titleNoFormatting = searchResult.SelectSingleNode(".//a[@class='scene-title']/p").InnerText.Split(new[] { " | " }, StringSplitOptions.None)[0].Trim();
                        curID = Helper.Encode(searchResult.SelectSingleNode(".//a[@class='scene-title']").GetAttributeValue("href", string.Empty));
                    }
                    else
                    {
                        titleNoFormatting = searchResult.SelectSingleNode(".//a[@class='scene-title']/h6").InnerText.Trim();
                        curID = Helper.Encode(searchResult.SelectSingleNode(".//a[@class='scene-title']").GetAttributeValue("href", string.Empty));
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
                catch
                {
                    // Ignore parsing errors
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

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, GetCookies());
            if (detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1[@class='description']").InnerText.Trim();

            var summaryNode = detailsPageElements.SelectSingleNode("//div[@class='synopsis']/p");
            if (summaryNode != null)
            {
                movie.Overview = summaryNode.InnerText.Trim();
            }

            movie.AddStudio("Adult Empire Cash");

            var taglineNode = detailsPageElements.SelectSingleNode("//div[@class='studio']//span/text()[2]");
            if (taglineNode != null)
            {
                string tagline = taglineNode.InnerText.Trim();
                movie.AddTag(tagline);
            }

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='release-date']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genres = detailsPageElements.SelectNodes("//div[@class='tags']//a");
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    movie.AddGenre(genre.InnerText);
                }
            }

            var actorsWithHeadshots = detailsPageElements.SelectNodes("//div[@class='video-performer']//img");
            if (actorsWithHeadshots != null)
            {
                foreach (var actor in actorsWithHeadshots)
                {
                    result.People.Add(new PersonInfo
                    {
                        Name = actor.GetAttributeValue("title", string.Empty).Trim(),
                        Type = PersonKind.Actor,
                        ImageUrl = actor.GetAttributeValue("data-bgsrc", string.Empty).Trim(),
                    });
                }
            }

            var actorsWithoutHeadshots = detailsPageElements.SelectNodes("//div[contains(@class, 'video-performer-container')][2]/a");
            if (actorsWithoutHeadshots != null)
            {
                foreach (var actor in actorsWithoutHeadshots)
                {
                    string actorName = actor.InnerText.Trim();
                    if (!result.People.Any(p => p.Name.Equals(actorName, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
                    }
                }
            }

            var directorNode = detailsPageElements.SelectSingleNode("//div[@class='director']");
            if (directorNode != null)
            {
                string directorName = directorNode.InnerText.Trim();
                if (!result.People.Any(p => p.Name.Equals(directorName, StringComparison.OrdinalIgnoreCase)))
                {
                    result.People.Add(new PersonInfo { Name = directorName, Type = PersonKind.Director });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();

            string sceneURL = Helper.Decode(sceneID[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var detailsPageElements = await HTML.ElementFromURL(sceneURL, cancellationToken, null, GetCookies());
            if (detailsPageElements == null)
            {
                return images;
            }

            var posters = detailsPageElements.SelectNodes("//div[@id='dv_frames']//img");
            if (posters == null)
            {
                return images;
            }

            bool first = true;
            foreach (var poster in posters)
            {
                string imageUrl = poster.GetAttributeValue("src", string.Empty).Replace("/320/", "/1280/");
                var imageInfo = new RemoteImageInfo { Url = imageUrl };
                if (first)
                {
                    imageInfo.Type = ImageType.Primary;
                    first = false;
                }
                else
                {
                    imageInfo.Type = ImageType.Backdrop;
                }

                images.Add(imageInfo);
            }

            return images;
        }
    }
}
