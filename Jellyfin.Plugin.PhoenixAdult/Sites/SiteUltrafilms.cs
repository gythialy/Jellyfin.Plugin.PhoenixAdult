using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

namespace PhoenixAdult.Sites
{
    public class SiteUltrafilms : IProviderBase
    {


        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {

        // Simplified search logic, may need adjustments
        var result = new List<RemoteSearchResult>();
        var googleResults = await GoogleSearch.PerformSearch(searchTitle, Helper.GetSearchSiteName(siteNum));
        foreach (var sceneURL in googleResults)
        {
            var http = await new HTTP().Get(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var titleNode = doc.DocumentNode.SelectSingleNode(@"//h1[@class=""entry-title""]");
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
        var http = await new HTTP().Get(sceneURL, cancellationToken);
        if (!http.IsOK) return result;

        var doc = new HtmlDocument();
        doc.LoadHtml(http.Content);

        movie.Name = doc.DocumentNode.SelectSingleNode(@"//h1[@class=""entry-title""]")?.InnerText.Trim();
        movie.Overview = doc.DocumentNode.SelectSingleNode(@"//p")?.InnerText.Trim();
        movie.AddStudio("Unknown");

        var dateNode = doc.DocumentNode.SelectSingleNode(@"//meta[@property=""article:published_time""]/@content");
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
        var http = await new HTTP().Get(sceneURL, cancellationToken);
        if (http.IsOK)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);
            var imageNodes = doc.DocumentNode.SelectNodes("//img/@src");
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    var imgUrl = img.GetAttributeValue("src", string.Empty);
                    if (!imgUrl.StartsWith("http"))
                        imgUrl = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), imgUrl).ToString();
                    images.Add(new RemoteImageInfo { Url = imgUrl });
                }
            }
        }
        return images;


        }
    }
}
