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
    public class NetworkWankz : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{Uri.EscapeDataString(searchTitle)}";
            var http = await HTTP.Request(searchUrl, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);

                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='scene']"))
                {
                    var titleNode = searchResult.SelectSingleNode(".//div[@class='title-wrapper']//a[@class='title']");
                    var titleNoFormatting = titleNode?.InnerText.Trim();
                    var sceneURL = searchResult.SelectSingleNode(".//a").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);
                    var subSiteNode = searchResult.SelectSingleNode(".//div[@class='series-container']//a[@class='sitename']");
                    var subSite = subSiteNode?.InnerText.Trim();

                    var item = new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curID}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [{subSite}]",
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
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = doc.DocumentNode.SelectSingleNode(@"//div[@class='title']//h1")?.InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode(@"//div[@class='description']//p")?.InnerText.Trim();
            movie.AddStudio("Wankz");
            movie.AddTag("Wankz");

            var dateNode = doc.DocumentNode.SelectSingleNode(@"//div[@class='views']//span");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Replace("Added", string.Empty).Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes(@"//div[contains(@class, 'actors')]//a[@class='model']"))
            {
                var actorName = actorLink.SelectSingleNode(".//span")?.InnerText.Trim();
                var actorPhotoURL = actorLink.SelectSingleNode(".//img").GetAttributeValue("src", string.Empty);
                if (!string.IsNullOrEmpty(actorName))
                {
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
                }
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes(@"//a[@class='cat'] | //p[@style]/a"))
            {
                var genreName = genreLink.InnerText.Trim();
                if (!string.IsNullOrEmpty(genreName))
                {
                    movie.AddGenre(genreName);
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            var providerIds = sceneID[0].Split('|');
            var sceneURL = Helper.Decode(providerIds[0]);
            if (!sceneURL.StartsWith("http"))
            {
                sceneURL = Helper.GetSearchBaseURL(siteNum) + sceneURL;
            }

            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imageNodes = doc.DocumentNode.SelectNodes(@"//a[@class='noplayer']/img/@src");
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src", string.Empty);
                        images.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Primary });
                    }
                }
            }
            return images;
        }
    }
}
