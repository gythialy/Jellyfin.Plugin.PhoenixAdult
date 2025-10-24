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
    public class SiteClubFilly : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = searchTitle.Split(' ').First();
            string url = Helper.GetSearchSearchURL(siteNum) + sceneId;
            var httpResult = await HTTP.Request(url, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                string titleNoFormatting = detailsPageElements.SelectSingleNode("//div[@class='fltWrap']/h1/span")?.InnerText.Trim();
                string curId = Helper.Encode(url);
                string releaseDate = detailsPageElements.SelectSingleNode("//div[@class='fltRight']")?.InnerText.Replace("Release Date :", string.Empty).Trim();
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                    Name = $"{titleNoFormatting} [ClubFilly] {releaseDate}",
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
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='fltWrap']/h1/span")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//p[@class='description']")?.InnerText.Replace("Description:", string.Empty).Trim();
            movie.AddStudio("ClubFilly");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//div[@class='fltRight']");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Release Date :", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("Lesbian");
            var actorText = detailsPageElements.SelectSingleNode("//p[@class='starring']")?.InnerText.Replace("Starring:", string.Empty).Trim();
            if (actorText != null)
            {
                var actors = actorText.Split(',');
                if (actors.Length == 3)
                {
                    movie.AddGenre("Threesome");
                }

                if (actors.Length == 4)
                {
                    movie.AddGenre("Foursome");
                }

                if (actors.Length > 4)
                {
                    movie.AddGenre("Orgy");
                }

                foreach (var actor in actors)
                {
                    result.People.Add(new PersonInfo { Name = actor.Trim(), Type = PersonKind.Actor });
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

            var imageNodes = detailsPageElements.SelectNodes("//ul[@id='lstSceneFocus']/li/img");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", string.Empty), Type = ImageType.Backdrop });
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
