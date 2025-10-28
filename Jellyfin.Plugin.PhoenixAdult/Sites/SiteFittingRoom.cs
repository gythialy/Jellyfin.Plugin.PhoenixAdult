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
    public class SiteFittingRoom : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = searchTitle.Split(' ').First();
            string sceneTitle = searchTitle.Contains(" ") ? searchTitle.Split(' ')[1] : string.Empty;
            string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}{sceneId}/1";

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                string titleNoFormatting = detailsPageElements.SelectSingleNode("//title")?.InnerText.Split('|')[1].Trim();
                string curId = Helper.Encode(sceneUrl);
                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}|{sceneId}" } },
                    Name = $"{titleNoFormatting} [Fitting-Room]",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds[1];

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            string title = detailsPageElements.SelectSingleNode("//title")?.InnerText.Trim();
            if (title?.Contains("|") == true)
            {
                title = title.Split('|')[1].Trim();
            }

            movie.Name = title;
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='description']")?.InnerText.Trim();
            movie.AddStudio("Fitting-Room");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var collectionNode = detailsPageElements.SelectSingleNode("//div[@id='list_videos_related_videos_items']/div[1]/div[2]/a");
            string collection = collectionNode?.InnerText.Trim();
            if (string.IsNullOrEmpty(collection))
            {
                if (movie.Name == "Huge Tits")
                {
                    collection = "Busty";
                }
                else if (movie.Name == "Pool Table")
                {
                    collection = "Fetishouse";
                }
                else if (movie.Name == "Spanish Milf")
                {
                    collection = "Milf";
                }
                else if (movie.Name == "Cotton Panty")
                {
                    collection = "Pantyhose";
                }
            }

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var actorLink = detailsPageElements.SelectSingleNode("//a[@class='model']/div[1]/img");
            if (actorLink != null)
            {
                string actorName = actorLink.GetAttributeValue("alt", string.Empty).Trim();
                string actorPhotoUrl = actorLink.GetAttributeValue("src", string.Empty).Trim();
                result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
            }

            var genreNodes = detailsPageElements.SelectNodes("//meta[@property='article:tag']");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.GetAttributeValue("content", string.Empty).Replace(result.People.FirstOrDefault()?.Name ?? string.Empty, string.Empty).Trim().ToLower());
                }
            }

            movie.AddGenre("Fitting Room");

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneId = sceneID[0].Split('|')[2];
            images.Add(new RemoteImageInfo { Url = $"https://www.fitting-room.com/contents/videos_screenshots/0/{sceneId}/preview.jpg", Type = ImageType.Primary });
            for (int i = 2; i < 6; i++)
            {
                images.Add(new RemoteImageInfo { Url = $"https://www.fitting-room.com/contents/videos_screenshots/0/{sceneId}/3840x1400/{i}.jpg" });
            }

            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
