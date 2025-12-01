using System;
using System.Collections.Generic;
using System.Linq;
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
    public class SiteMeloneChallenge : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(url => url.Contains("/video/")).ToList();

            foreach (var sceneURL in searchResults)
            {
                var http = await HTTP.Request(sceneURL, cancellationToken);
                if (http.IsOK)
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(http.Content);
                    var titleNode = doc.DocumentNode.SelectSingleNode(".//a[@class='dark']");
                    var titleNoFormatting = titleNode?.InnerText.Trim();
                    var curID = Helper.Encode(sceneURL);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
                        SearchProviderName = Plugin.Instance.Name,
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
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            movie.ExternalId = sceneURL;
            movie.Name = doc.DocumentNode.SelectSingleNode(".//a[@class='dark']").InnerText.Trim();
            movie.AddStudio("Melone Challenge");
            movie.AddCollection("Melone Challenge");

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imageNode = doc.DocumentNode.SelectSingleNode(".//figure/img");
                if (imageNode != null)
                {
                    var imgUrl = imageNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Primary });
                    }
                }
            }

            return images;
        }
    }
}
