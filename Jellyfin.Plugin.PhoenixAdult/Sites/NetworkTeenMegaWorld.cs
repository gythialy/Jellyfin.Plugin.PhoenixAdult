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
    public class NetworkTeenMegaWorld : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            for (int page = 1; page < 3; page++)
            {
                string searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}&page={page}";
                var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
                if (!httpResult.IsOK)
                {
                    continue;
                }

                var searchPageElements = HTML.ElementFromString(httpResult.Content);
                var searchNodes = searchPageElements.SelectNodes("//div[contains(@class,'thumb thumb-video')]");
                if (searchNodes != null)
                {
                    foreach (var node in searchNodes)
                    {
                        var titleNode = node.SelectSingleNode(".//a[@class='thumb__title-link']");
                        string titleNoFormatting = titleNode?.InnerText.Trim();
                        string curId = Helper.Encode(titleNode?.GetAttributeValue("href", string.Empty));
                        string releaseDate = string.Empty;
                        var dateNode = node.SelectSingleNode(".//time");
                        if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
                        {
                            releaseDate = parsedDate.ToString("yyyy-MM-dd");
                        }

                        string subSite = node.SelectSingleNode(".//a[@class='thumb__detail__site-link clr-grey']")?.InnerText.Trim();
                        result.Add(new RemoteSearchResult
                        {
                            ProviderIds = { { Plugin.Instance.Name, curId } },
                            Name = $"{titleNoFormatting} [TMW/{subSite}] {releaseDate}",
                            SearchProviderName = Plugin.Instance.Name,
                        });
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
            movie.Name = detailsPageElements.SelectSingleNode("//h1[@id='video-title']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='video-description-text']")?.InnerText.Trim();
            movie.AddStudio("Teen Mega World");

            string tagline = detailsPageElements.SelectSingleNode("//a[@class='video-site-link btn btn-ghost btn--rounded']")?.InnerText.Trim();
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//span[@title='Video release date']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[@class='video-actor-link actor__link']");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", string.Empty);
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        actorPhotoUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{actorPage.SelectSingleNode("//div[@class='model-profile-image-wrap']//img")?.GetAttributeValue("src", string.Empty)}";
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            var genreNodes = detailsPageElements.SelectNodes("//a[@class='video-tag-link']");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim());
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

            var posterNode = detailsPageElements.SelectSingleNode("//img[@id='video-cover-image']");
            if (posterNode != null)
            {
                string imageUrl = posterNode.GetAttributeValue("src", string.Empty);
                if (!imageUrl.StartsWith("http"))
                {
                    imageUrl = $"{Helper.GetSearchBaseURL(siteNum)}/{imageUrl}";
                }

                images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
            }

            return images;
        }
    }
}
