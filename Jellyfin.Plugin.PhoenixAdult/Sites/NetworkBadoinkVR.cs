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
                    var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'video-title')]")?.InnerText;
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//p[@itemprop='uploadDate']");
                    if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", ""), out var parsedDate))
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
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
                    var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    var searchNodes = searchPageElements.SelectNodes("//div[@class='tile-grid-item']");
                    if (searchNodes != null)
                    {
                        foreach (var node in searchNodes)
                        {
                            string titleNoFormatting = node.SelectSingleNode(".//a[contains(@class, 'video-card-title')]")?.GetAttributeValue("title", "");
                            string curId = Helper.Encode(node.SelectSingleNode(".//a[contains(@class, 'video-card-title')]")?.GetAttributeValue("href", ""));
                            string girlName = node.SelectSingleNode(".//a[@class='video-card-link']")?.InnerText;
                            string releaseDate = string.Empty;
                            var dateNode = node.SelectSingleNode(".//span[@class='video-card-upload-date']");
                            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", ""), out var parsedDate))
                                releaseDate = parsedDate.ToString("yyyy-MM-dd");

                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'video-title')]")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='video-description']")?.InnerText.Trim();
            movie.AddStudio("BadoinkVR");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            var dateNode = detailsPageElements.SelectSingleNode("//p[@itemprop='uploadDate']");
            if (dateNode != null && DateTime.TryParse(dateNode.GetAttributeValue("content", ""), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[@class='video-tag']");
            if (genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[contains(@class, 'video-actor-link')]");
            if (actorNodes != null)
            {
                foreach (var actorNode in actorNodes)
                {
                    string actorName = actorNode.InnerText.Trim();
                    string actorUrl = Helper.GetSearchBaseURL(siteNum) + actorNode.GetAttributeValue("href", "");
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = await HTML.ElementFromString(actorHttp.Content, cancellationToken);
                        actorPhotoUrl = actorPage.SelectSingleNode("//img[@class='girl-details-photo']")?.GetAttributeValue("src", "").Split('?')[0];
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

            var posterNodes = detailsPageElements.SelectNodes("//img[@class='video-image'] | //div[contains(@class, 'gallery-item')]");
            if(posterNodes != null)
            {
                foreach(var node in posterNodes)
                {
                    string imageUrl = node.GetAttributeValue("src", "") ?? node.GetAttributeValue("data-big-image", "");
                    images.Add(new RemoteImageInfo { Url = imageUrl.Split('?')[0] });
                }
            }

            var galleryNode = detailsPageElements.SelectSingleNode("//div[contains(@class, 'gallery-item')]");
            if (galleryNode != null)
            {
                string sceneBaseUrl = galleryNode.GetAttributeValue("data-big-image", "").Split(new[] { ".jpg" }, StringSplitOptions.None)[0].Split('_').First();
                var photoNumNode = detailsPageElements.SelectSingleNode("//span[@class='gallery-zip-info']");
                if (photoNumNode != null && int.TryParse(photoNumNode.InnerText.Split(' ')[0], out var photoNum))
                {
                    for (int i = 1; i < photoNum + 2; i++)
                    {
                        images.Add(new RemoteImageInfo { Url = $"{sceneBaseUrl}_{i}.jpg" });
                    }
                }
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
