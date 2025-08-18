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
    public class SiteFuckingAwesome : IProviderBase
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
            var searchNodes = searchPageElements.SelectNodes("//div[@class='gallery']/div");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//div[@class='video-title truncate']/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(Helper.GetSearchBaseURL(siteNum) + titleNode?.GetAttributeValue("href", string.Empty).Trim());
                    string releaseDate = string.Empty;
                    var dateNode = node.SelectSingleNode(".//span[@class='small date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    string firstActor = node.SelectSingleNode(".//span[@class='subtitle small']/a")?.InnerText.Trim();
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{releaseDate} {firstActor} in {titleNoFormatting} [FuckingAwesome]",
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
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='more text-justify']")?.InnerText.Trim();
            movie.AddStudio("FuckingAwesome");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='videodate']/strong");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='tags']/ul/li/a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='pornstarnames']/ul/li/a[contains(@href, 'pornstars')]");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actorNodes.Count == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actorNodes.Count > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='pornstar-pic ']/img")?.GetAttributeValue("src", string.Empty);
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

            var imageNodes = detailsPageElements.SelectNodes("//span[@class='et_pb_image_wrap ']/img");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("content", string.Empty) });
                }
            }

            try
            {
                string photoPageUrl = Helper.GetSearchBaseURL(siteNum) + detailsPageElements.SelectSingleNode("//li[@class='photos']/a")?.GetAttributeValue("href", string.Empty);
                var photoHttp = await HTTP.Request(photoPageUrl, HttpMethod.Get, cancellationToken);
                if(photoHttp.IsOK)
                {
                    var photoPage = await HTML.ElementFromString(photoHttp.Content, cancellationToken);
                    var unlockedPhotos = photoPage.SelectNodes("//div[@class='my-gallery']/a");
                    if(unlockedPhotos != null)
                    {
                        foreach(var photo in unlockedPhotos)
                        {
                            string imageUrl = photo.GetAttributeValue("href", string.Empty);
                            if (!imageUrl.StartsWith("http"))
                            {
                                imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                            }

                            images.Add(new RemoteImageInfo { Url = imageUrl });
                        }
                    }
                }
            } catch {}

            if (images.Any())
            {
                images.First().Type = ImageType.Primary;
            }

            return images;
        }
    }
}
