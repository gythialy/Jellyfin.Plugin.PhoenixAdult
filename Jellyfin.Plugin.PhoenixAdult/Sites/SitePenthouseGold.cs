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
    public class SitePenthouseGold : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            // Simplified search logic, may need adjustments
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            foreach (var sceneURL in googleResults)
            {
                var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
                if (doc != null)
                {
                    var titleNode = doc.SelectSingleNode(@"//div[@class=""content-desc content-new-scene""]//h1");
                    var titleNoFormatting = titleNode?.InnerText.Trim();
                    var curID = Helper.Encode(sceneURL);
                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
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
            var sceneURL = Helper.Decode(sceneID[0]);
            var doc = await HTML.ElementFromURL(sceneURL, cancellationToken);
            if (doc == null)
            {
                return result;
            }

            movie.Name = doc.SelectSingleNode(@"//div[@class=""content-desc content-new-scene""]//h1")?.InnerText.Trim();
            movie.Overview = doc.SelectSingleNode(@"//div[@class=""content-desc content-new-scene""]//p")?.InnerText.Trim();
            movie.AddStudio("Unknown");

            var dateNode = doc.SelectSingleNode(@"//meta[@itemprop=""uploadDate""]");
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
            var sceneURL = Helper.Decode(sceneID[0]);
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
