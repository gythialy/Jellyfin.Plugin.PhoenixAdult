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
    public class SiteInterracialPass : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(' ', '+');
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var searchPageElements = HTML.ElementFromString(httpResult.Content);
            var searchNodes = searchPageElements.SelectNodes("//div[contains(@class, 'item-video')]");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//a");
                    string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty);
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//div[@class='more-info-div']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split('|').Last().Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
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
            var titleNode = detailsPageElements.SelectSingleNode("//*[@class='video-player']//h3[@class='section-title']") ??
                            detailsPageElements.SelectSingleNode("//*[@class='video-player']//h1[@class='section-title']") ??
                            detailsPageElements.SelectSingleNode("//*[@class='video-player']//h2[@class='section-title']");
            if (titleNode != null)
            {
                movie.Name = titleNode.InnerText.Trim();
            }

            movie.Overview = detailsPageElements.SelectNodes("//div[@class='update-info-block']")?.LastOrDefault()?.InnerText.Replace("Description:", string.Empty).Trim();

            string studio = Helper.GetSearchSiteName(siteNum);
            if ((siteNum[1] >= 1 && siteNum[1] <= 3) || siteNum[1] == 6)
            {
                studio = "ExploitedX";
            }

            movie.AddStudio(studio);

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='update-info-row']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Released:", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//ul[@class='tags']//li//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[contains(@class, 'models-list-thumbs')]//li");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.SelectSingleNode(".//span")?.InnerText;
                    string actorPhotoUrl = actor.SelectSingleNode(".//img")?.GetAttributeValue("src0_3x", string.Empty);
                    if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                    {
                        actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                    }

                    if (siteNum[1] == 2 && actorName == "Twins")
                    {
                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = "Joey White", Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = "Sami White", Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                    }
                    else
                    {
                        ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                    }
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

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='player-thumb']//img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src0_1x", string.Empty);
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
