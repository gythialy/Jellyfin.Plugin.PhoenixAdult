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
    public class SiteBAMVisions : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='category_listing_wrapper_updates']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//h3/a");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string curId = Helper.Encode(titleNode?.GetAttributeValue("href", ""));
                    string videoBg = Helper.Encode(node.SelectSingleNode(".//img")?.GetAttributeValue("src0_3x", ""));
                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{videoBg}" } },
                        Name = $"{titleNoFormatting} [BAMVisions]",
                        SearchProviderName = Plugin.Instance.Name
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='item-info']/h4/a")?.InnerText.Trim();

            var description = detailsPageElements.SelectSingleNode("//p[@class='description']");
            if (description != null)
                movie.Overview = description.InnerText.Trim();

            movie.AddStudio("BAMVisions");
            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            var dateNode = detailsPageElements.SelectSingleNode("//ul[@class='item-meta']/li[1]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Split(new[] { "Release Date:" }, StringSplitOptions.None).Last().Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            movie.AddGenre("Anal");
            movie.AddGenre("Hardcore");

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='item-info']/h5/a");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = actor.GetAttributeValue("href", "");
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = await HTML.ElementFromString(actorHttp.Content, cancellationToken);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='profile-pic']/img")?.GetAttributeValue("src0_3x", "");
                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                    }
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string scenePoster = Helper.Decode(sceneID[0].Split('|')[2]);
            images.Add(new RemoteImageInfo { Url = Helper.GetSearchBaseURL(siteNum) + scenePoster, Type = ImageType.Primary });
            return Task.FromResult<IEnumerable<RemoteImageInfo>>(images);
        }
    }
}
