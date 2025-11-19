using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
    public class NetworkBadoinkVR : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            var parts = searchTitle.Split(' ');
            if (int.TryParse(parts[0], out _))
            {
                sceneId = parts[0];
                searchTitle = string.Join(" ", parts.Skip(1));
            }

            if (sceneId != null)
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/vrpornvideo/{sceneId}";
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'video-title')]")?.InnerText;
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//p[@itemprop='uploadDate']");
                    if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", string.Empty), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curId } },
                        Name = $"[{Helper.GetSearchSiteName(siteNum)}] {titleNoFormatting} {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                    });
                }
            }
            else
            {
                string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchPageElements = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchPageElements.SelectNodes("//div[@class='tile-grid-item']");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string titleNoFormatting = Helper.ParseTitle(node.SelectSingleNode(".//a[contains(@class, 'video-card-title')]")?.GetAttributeValue("title", string.Empty), siteNum);
                            string curId = Helper.Encode(node.SelectSingleNode(".//a[contains(@class, 'video-card-title')]")?.GetAttributeValue("href", string.Empty));
                            string girlName = node.SelectSingleNode(".//a[@class='video-card-link']")?.InnerText;
                            string releaseDate = string.Empty;
                            var dateNode = node.SelectSingleNode(".//span[@class='video-card-upload-date']");
                            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", string.Empty), out var parsedDate))
                            {
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");
                            }

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, curId } },
                                Name = $"[{Helper.GetSearchSiteName(siteNum)}] {girlName} in {titleNoFormatting} {releaseDate}",
                                SearchProviderName = Plugin.Instance.Name,
                            });
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
            movie.Name = Helper.ParseTitle(detailsPageElements.SelectSingleNode("//h1[contains(@class, 'video-title')]")?.InnerText.Trim(), siteNum);
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='video-description']")?.InnerText.Trim();
            movie.AddStudio("BadoinkVR");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//p[@itemprop='uploadDate']");
            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", string.Empty), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[@class='video-tag']");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[contains(@class, 'video-actor-link')]");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    string actorUrl = Helper.GetSearchBaseURL(siteNum) + actorNode.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//img[@class='girl-details-photo']")?.GetAttributeValue("src", string.Empty).Split('?')[0];
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

            var xpaths = new[] { "//img[@class='video-image']/@src", "//div[contains(@class, 'gallery-item')]/@data-big-image" };
            foreach (var xpath in xpaths)
            {
                var imageNodes = detailsPageElements.SelectNodes(xpath);
                if (imageNodes != null)
                {
                    foreach (var image in imageNodes)
                    {
                        string imageUrl = image.GetAttributeValue(xpath.Split('@').Last(), string.Empty).Split('?')[0];
                        if (!images.Any(i => i.Url == imageUrl))
                        {
                            images.Add(new RemoteImageInfo { Url = imageUrl });
                        }
                    }
                }
            }

            var galleryNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'gallery-item')]");
            if (galleryNode != null)
            {
                string sceneBaseUrl = galleryNode.GetAttributeValue("data-big-image", string.Empty).Split(new[] { ".jpg" }, StringSplitOptions.None)[0];
                sceneBaseUrl = Regex.Replace(sceneBaseUrl, @"_\d+$", string.Empty);
                var photoNumNode = detailsPageElements.SelectSingleNode("//span[@class='gallery-zip-info']");
                if (photoNumNode != null && int.TryParse(photoNumNode.InnerText.Split(' ')[0], out var photoNum))
                {
                    for (int i = 1; i < photoNum + 2; i++)
                    {
                        string imageUrl = $"{sceneBaseUrl}_{i}.jpg";
                        if (!images.Any(img => img.Url == imageUrl))
                        {
                            images.Add(new RemoteImageInfo { Url = imageUrl });
                        }
                    }
                }
            }

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
                foreach (var image in images.Skip(1))
                {
                    image.Type = ImageType.Backdrop;
                }
            }

            return images;
        }
    }
}
