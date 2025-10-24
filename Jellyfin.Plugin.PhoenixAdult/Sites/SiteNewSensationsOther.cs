using System;
using System.Collections.Generic;
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
    public class SiteNewSensationsOther : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
        // Simplified search logic, may need adjustments
        var result = new List<RemoteSearchResult>();
        var googleResults = await GoogleSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
        foreach (var sceneURL in googleResults)
        {
            var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (doc != null)
            {
                var titleNode = doc.SelectSingleNode(@"//div[@class=""update_title""]");
                var titleNoFormatting = titleNode?.InnerText.Trim();
                var curID = Helper.Encode(sceneURL);
                var item = new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                    Name = titleNoFormatting,
                    SearchProviderName = Plugin.Instance.Name,
                };
                result.Add(item);
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
        var movie = (Movie)result.Item;
        var providerIds = sceneID[0].Split('|');
        var sceneURL = Helper.Decode(providerIds[0]);
        var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
        if (doc == null)
            {
                return result;
            }

        movie.Name = doc.SelectSingleNode(@"//div[@class=""update_title""]")?.InnerText.Trim();
        movie.Overview = doc.SelectSingleNode(@"//span[@class=""update_description""]")?.InnerText.Trim();
        movie.AddStudio("New Sensations");

        var dateNode = doc.SelectSingleNode(@"//*[@class='date']");
        if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
        {
            movie.PremiereDate = parsedDate;
            movie.ProductionYear = parsedDate.Year;
        }

        // Actor and Genre logic needs to be manually added for each site
        return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
        // Simplified image logic, may need adjustments
        var images = new List<RemoteImageInfo>();
        var providerIds = sceneID[0].Split('|');
        var sceneURL = Helper.Decode(providerIds[0]);
        var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
        if (doc != null)
        {
            var imageNodes = doc.SelectNodes("//img/@src");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    var imgUrl = img.GetAttributeValue("src", string.Empty);
                    if (!imgUrl.StartsWith("http"))
                        {
                            imgUrl = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), imgUrl).ToString();
                        }

                    images.Add(new RemoteImageInfo { Url = imgUrl });
                }
            }
        }

        return images;
        }
    }
}
