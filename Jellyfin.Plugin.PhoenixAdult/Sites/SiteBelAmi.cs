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
    public class SiteBelAmi : IProviderBase
    {
        private async Task<(string title, string releaseDate, HtmlNode detailsPageElements)> GetSceneInfo(int[] siteNum, string sceneId, CancellationToken cancellationToken)
        {
            string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}VideoID={sceneId}"; // siteNum is not used, so hardcode 0
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                string title = detailsPageElements.SelectSingleNode("//div[contains(@class, 'video_detail')]//span[contains(@id, 'ContentPlaceHolder1_LabelTitle')]")?.InnerText.Trim();
                string releaseDate = detailsPageElements.SelectSingleNode("//div[contains(@class, 'video_detail')]//span[contains(@id, 'ContentPlaceHolder1_LabelReleased')]")?.InnerText;
                return (title, releaseDate, detailsPageElements);
            }

            return (null, null, null);
        }

        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = searchTitle.Split(' ').First();
            var sceneInfo = await GetSceneInfo(siteNum, sceneId, cancellationToken);
            if (sceneInfo.title != null)
            {
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, sceneId } },
                    Name = $"{sceneInfo.title} [{Helper.GetSearchSiteName(siteNum)}] {sceneInfo.releaseDate}",
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

            string sceneId = sceneID[2];
            var sceneInfo = await GetSceneInfo(siteNum, sceneId, cancellationToken);
            if (sceneInfo.detailsPageElements == null)
            {
                return result;
            }

            var movie = (Movie)result.Item;
            movie.Name = sceneInfo.title;
            movie.Overview = sceneInfo.detailsPageElements.SelectSingleNode("//div[contains(@class, 'video_detail')]//div[contains(@class, 'bottom')]//p[2]")?.InnerText.Trim();
            movie.AddStudio("Bel Ami Online");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            if (DateTime.TryParse(sceneInfo.releaseDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = sceneInfo.detailsPageElements.SelectNodes("//div[contains(@class, 'video_detail')]//span[contains(@id, 'ContentPlaceHolder1_LabelTags')]//a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText);
                }
            }

            var actorNodes = sceneInfo.detailsPageElements.SelectNodes("//div[contains(@class, 'video_detail')]//div[contains(@class, 'right')]//div[contains(@class, 'actors_list')]//div[contains(@class, 'actor')]//a");
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
                    string actorName = actor.InnerText;
                    string actorPhotoUrl = actor.SelectSingleNode("//img")?.GetAttributeValue("src", string.Empty);
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneId = sceneID[0];
            images.Add(new RemoteImageInfo { Url = $"https://freecdn.belamionline.com/Data/Contents/Content_{sceneId}/Thumbnail8.jpg", Type = ImageType.Primary });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
