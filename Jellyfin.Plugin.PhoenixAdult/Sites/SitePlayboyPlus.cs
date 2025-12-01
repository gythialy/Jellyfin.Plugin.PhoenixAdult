using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class SitePlayboyPlus : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = $"{Helper.GetSearchSearchURL(siteNum)}/{searchTitle.Replace(" ", "+")}";
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@id='search-results-gallery']//li[@class='item']"))
                {
                    var titleNoFormatting = searchResult.SelectSingleNode(".//h3[@class='title']").InnerText.Trim();
                    var releaseDate = string.Empty;
                    var dateNode = searchResult.SelectSingleNode(".//p[@class='date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var image = searchResult.SelectSingleNode(".//img[contains(@class, 'image')]").GetAttributeValue("data-src", string.Empty).Split('?').First();
                    var sceneURL = Helper.GetSearchBaseURL(siteNum) + searchResult.SelectSingleNode(".//a[@class='cardLink']").GetAttributeValue("href", string.Empty);
                    var curID = Helper.Encode(sceneURL);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [Playboy Plus] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = image,
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//h1[@class='title']").InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode("//p[@class='description-truncated']").InnerText.Trim().Replace("...", string.Empty);
            movie.AddStudio("Playboy Plus");
            movie.AddCollection("Playboy Plus");
            movie.AddGenre("Glamour");

            var dateNode = doc.DocumentNode.SelectSingleNode("//p[contains(@class, 'date')]");
            if (dateNode != null && DateTime.TryParseExact(dateNode.InnerText.Trim(), "MMMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var actorLink in doc.DocumentNode.SelectNodes("//p[@class='contributorName']//a"))
            {
                var actorName = actorLink.InnerText.Trim();
                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor });
            }

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
                var posterUrl = doc.DocumentNode.SelectSingleNode("//img[contains(@class, 'image')]")?.GetAttributeValue("data-src", string.Empty).Split('?').First();
                if (!string.IsNullOrEmpty(posterUrl))
                {
                    images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                }

                var galleryNodes = doc.DocumentNode.SelectNodes("//section[@class='gallery']//img[contains(@class, 'image')]");
                if (galleryNodes != null)
                {
                    foreach (var img in galleryNodes)
                    {
                        var imgUrl = img.GetAttributeValue("data-src", string.Empty).Split('?').First();
                        if (!string.IsNullOrEmpty(imgUrl))
                        {
                            images.Add(new RemoteImageInfo { Url = imgUrl, Type = ImageType.Backdrop });
                        }
                    }
                }
            }

            return images;
        }
    }
}
