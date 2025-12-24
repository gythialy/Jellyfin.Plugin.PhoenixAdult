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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class SiteCumbizz : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(' ', '-')}";
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1[@class='har_h1_title']")?.InnerText.Trim();
                string curId = Helper.Encode(sceneUrl);
                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{Helper.Encode(titleNoFormatting)}" } },
                    Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;
            movie.Name = Helper.Decode(providerIds[1]);
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='container text-center']//h2")?.InnerText.Trim();
            movie.AddStudio("Cumbizz");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var genreNodes = detailsPageElements.SelectNodes("//span[@class='label label-primary']/a");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    movie.AddGenre(genre.InnerText.Trim().ToLower());
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='breadcrumbs']/a");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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

            var imageNodes = detailsPageElements.SelectNodes("//section[@class='har_section har_image_bck har_wht_txt har_fixed'] | //img[@class='vidgal unos'] | //img[@class='vidgal dos'] | //img[@class='vidgal tres'] | //img[@class='vidgal quatros']");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("data-image", string.Empty) ?? img.GetAttributeValue("src", string.Empty) });
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
