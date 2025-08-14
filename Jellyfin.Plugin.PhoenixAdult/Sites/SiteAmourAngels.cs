using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SiteAmourAngels : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = searchTitle.Split(' ')[0];
            string sceneTitle = searchTitle.Contains(" ") ? searchTitle.Split(' ')[1] : string.Empty;

            string sceneUrl = $"{Helper.GetSearchSearchURL(siteNum)}{sceneId}.html";
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (httpResult.IsOK)
            {
                var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                string titleNoFormatting = detailsPageElements.SelectSingleNode("//td[@class='blox-bg']//td[2]//b")?.InnerText.Replace("Video", "").Trim();
                string curId = Helper.Encode(sceneUrl);
                var dateNode = detailsPageElements.SelectSingleNode("//td[@class='blox-bg']//td[2]");
                string date = dateNode?.InnerText.Split(new[] { "Added" }, StringSplitOptions.None)[1].Trim().Substring(0, 10);
                string releaseDate = DateTime.Parse(date).ToString("yyyy-MM-dd");

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                    Name = $"{titleNoFormatting} [AmourAngels] {releaseDate}",
                    SearchProviderName = Plugin.Instance.Name
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
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//td[@class='blox-bg']//td[2]//b")?.InnerText.Replace("Video", "").Trim();
            movie.AddStudio("AmourAngels");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });
            movie.AddGenre("Softcore");
            movie.AddGenre("European Girls");

            var dateNode = detailsPageElements.SelectSingleNode("//td[@class='blox-bg']//td[2]");
            if (dateNode != null)
            {
                string date = dateNode.InnerText.Split(new[] { "Added" }, StringSplitOptions.None)[1].Trim().Substring(0, 10);
                if (DateTime.TryParse(date, out var parsedDate))
                {
                    movie.PremiereDate = parsedDate;
                    movie.ProductionYear = parsedDate.Year;
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//td[@class='modinfo']//a");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorUrl = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", "");
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = await HTML.ElementFromString(actorHttp.Content, cancellationToken);
                        actorPhotoUrl = actorPage.SelectSingleNode("//td[@class='modelinfo-bg']//td[1]//img")?.GetAttributeValue("src", "");
                        if(!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
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

            var imageNodes = detailsPageElements.SelectNodes("//td[@class='noisebg']//div//img");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                    images.Add(new RemoteImageInfo { Url = img.GetAttributeValue("src", ""), Type = ImageType.Backdrop });
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
