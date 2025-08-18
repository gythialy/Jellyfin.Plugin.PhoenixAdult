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
    public class SiteHoloGirlsVR : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? string.Empty, out var id))
            {
                sceneId = id.ToString();
                searchTitle = searchTitle.Replace(sceneId, string.Empty).Trim();
            }

            if (sceneId != null && string.IsNullOrEmpty(searchTitle))
            {
                string sceneUrl = $"{Helper.GetSearchBaseURL(siteNum)}/Scenes/Videos/{sceneId}";
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var searchResults = HTML.ElementFromString(httpResult.Content);
                    string titleNoFormatting = searchResults.SelectSingleNode("//div[@class='col-xs-12 video-title']//h3")?.InnerText.Trim();
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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
                    var searchResults = HTML.ElementFromString(httpResult.Content);
                    var searchNodes = searchResults.SelectNodes("//div[@class='memVid']");
                    if(searchNodes != null)
                    {
                        foreach(var node in searchNodes)
                        {
                            var titleNode = node.SelectSingleNode(".//div[@class='memVidTitle']//a");
                            string titleNoFormatting = titleNode?.GetAttributeValue("title", string.Empty);
                            string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                            string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                            result.Add(new RemoteSearchResult
                            {
                                ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                                Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='col-xs-12 video-title']//h3")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectNodes("//div[@class='col-sm-6 col-md-6 vidpage-info']/text()")?.LastOrDefault()?.InnerText.Trim();
            movie.AddStudio(Helper.GetSearchSiteName(siteNum));

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='videopage-tags']/a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='col-xs-6 col-sm-4 col-md-3']");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.SelectSingleNode(".//div[@class='vidpage-mobilePad']//a//strong")?.InnerText.Trim();
                    string actorPhotoUrl = actor.SelectSingleNode(".//img[@class='img-responsive imgHover']")?.GetAttributeValue("src", string.Empty);
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

            var posterNode = detailsPageElements.SelectSingleNode("//div[@class='col-xs-12 col-sm-6 col-md-6 vidCover']//img");
            if(posterNode != null)
            {
                images.Add(new RemoteImageInfo { Url = posterNode.GetAttributeValue("src", string.Empty), Type = ImageType.Primary });
            }

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='vid-flex-container']//span");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.SelectSingleNode(".//img")?.GetAttributeValue("src", string.Empty).Replace("_thumb", string.Empty) });
                }
            }

            return images;
        }
    }
}
