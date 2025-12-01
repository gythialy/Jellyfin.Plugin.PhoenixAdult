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
    public class SitePenthouseGold : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchURL = Helper.GetSearchSearchURL(siteNum) + searchTitle.Replace(" ", "+");
            var http = await HTTP.Request(searchURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                foreach (var searchResult in doc.DocumentNode.SelectNodes("//div[@class='scene']"))
                {
                    var url = searchResult.SelectSingleNode(".//a[@data-track='TITLE_LINK']").GetAttributeValue("href", string.Empty);
                    if (!url.Contains("/scenes/"))
                    {
                        continue;
                    }

                    var curID = Helper.Encode(url);
                    var titleNoFormatting = searchResult.SelectSingleNode(".//a[@data-track='TITLE_LINK']").InnerText;
                    var releaseDate = string.Empty;
                    var dateNode = searchResult.SelectSingleNode("./span[@class='scene-date']");
                    if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }

                    var image = searchResult.SelectSingleNode(".//img").GetAttributeValue("src", string.Empty);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, curID } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
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
            movie.Name = doc.DocumentNode.SelectSingleNode("//div[@class='content-desc content-new-scene']//h1").InnerText.Replace("Video -", string.Empty).Replace("Movie -", string.Empty).Trim();
            var overviewNode = doc.DocumentNode.SelectSingleNode("//div[@class='content-desc content-new-scene']//p");
            if (overviewNode != null)
            {
                movie.Overview = overviewNode.InnerText.Trim();
            }

            movie.AddStudio(Helper.GetSearchSiteName(siteNum));
            movie.AddCollection(Helper.GetSearchSiteName(siteNum));

            var dateNode = doc.DocumentNode.SelectSingleNode("//meta[@itemprop='uploadDate']");
            if (dateNode != null && DateTime.TryParseExact(dateNode.GetAttributeValue("content", string.Empty), "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            foreach (var genreLink in doc.DocumentNode.SelectNodes("//ul[contains(@class, 'scene-tags')]/li/a"))
            {
                var genreName = genreLink.InnerText.ToLower();
                movie.AddGenre(genreName);
            }

            foreach (var actorPage in doc.DocumentNode.SelectNodes("//ul[@id='featured_pornstars']//div[@class='model']"))
            {
                var actorName = actorPage.SelectSingleNode(".//h3").InnerText.Trim();
                var actorPhotoURL = actorPage.SelectSingleNode(".//img").GetAttributeValue("src", string.Empty);
                result.AddPerson(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoURL });
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
                var posterNode = doc.DocumentNode.SelectSingleNode("//div[@id='trailer_player_finished']//img");
                if (posterNode != null)
                {
                    var posterUrl = posterNode.GetAttributeValue("src", string.Empty);
                    if (!string.IsNullOrEmpty(posterUrl))
                    {
                        images.Add(new RemoteImageInfo { Url = posterUrl, Type = ImageType.Primary });
                    }
                }
            }

            return images;
        }
    }
}
